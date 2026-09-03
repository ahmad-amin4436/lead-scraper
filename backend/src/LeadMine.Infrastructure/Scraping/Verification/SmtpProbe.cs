using System.Net.Sockets;
using System.Text;
using LeadMine.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace LeadMine.Infrastructure.Scraping.Verification;

public enum SmtpProbeResult
{
    /// <summary>The server accepted RCPT TO for the address (2xx).</summary>
    Accepted,

    /// <summary>The server rejected the address as non-existent (550/551/553, or a 5.1.x "bad mailbox" extended code).</summary>
    Rejected,

    /// <summary>Connection refused, timed out, TLS-only, greylisted, or any other non-definitive outcome — including a 4xx that didn't clear on retry, and a 5.7.x policy rejection.</summary>
    Inconclusive,
}

/// <param name="Result">The coarse verdict <see cref="EmailValidationPipeline"/> already acts on.</param>
/// <param name="Class">The full classification — distinguishes *why* a result is <see cref="SmtpProbeResult.Inconclusive"/> (temp failure vs. policy rejection vs. no response at all), for diagnostics and for the retry decision.</param>
/// <param name="SmtpCode">The 3-digit RCPT TO reply code, when the probe got one at all (0 if the connection never got that far).</param>
/// <param name="DsnCode">The RFC 3463 extended status code from the reply, when present.</param>
public sealed record SmtpProbeOutcome(SmtpProbeResult Result, SmtpResponseClass Class, int SmtpCode, string? DsnCode)
{
    public static readonly SmtpProbeOutcome Inconclusive = new(SmtpProbeResult.Inconclusive, SmtpResponseClass.Inconclusive, 0, null);
}

/// <summary>
/// A raw SMTP RCPT TO probe — connects directly to a recipient domain's own
/// mail exchanger and asks whether it would accept a message for a given
/// address, without ever sending <c>DATA</c>. No message is composed or
/// transmitted; nothing here can bounce, and nothing lands in anyone's inbox.
/// <para>
/// This is the technique <see cref="EmailVerifier"/>'s own remarks describe as
/// deliberately excluded from that class — see
/// <see cref="EmailValidationOptions.EnableSmtpProbe"/> for why it exists here
/// as a separate, opt-in step instead of being folded into the always-on path.
/// </para>
/// <para>
/// Uses a null reverse-path (<c>MAIL FROM:&lt;&gt;</c>) — the standard,
/// least-suspicious sender for a verification probe, the same convention real
/// bounce messages use.
/// </para>
/// </summary>
public sealed class SmtpProbe(ILogger<SmtpProbe> logger)
{
    private const int DefaultSmtpPort = 25;
    private static readonly TimeSpan DefaultTempFailureRetryDelay = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Test-only seam: the port every probe connects to. Always 25 in
    /// production — nothing in DI or configuration sets this, since real MX
    /// hosts only listen for SMTP there — but tests need a local fake server
    /// on an ephemeral port, which port 25 (privileged, and almost always
    /// already claimed by a real mail service if one's installed) can't be.
    /// </summary>
    internal int Port { get; init; } = DefaultSmtpPort;

    /// <summary>
    /// One bounded retry on a 4xx reply — a greylist or a momentarily busy
    /// server often clears within a couple of seconds. Never retried more than
    /// once per host per call: a probe that's still 4xx after that is exactly
    /// what <see cref="SmtpResponseClass.TempFailure"/> is for, and the
    /// background worker's own next tick is the real retry loop, not a tight
    /// spin here. Overridable (test-only) so retry tests don't have to spend
    /// two real seconds asleep for every run.
    /// </summary>
    internal TimeSpan TempFailureRetryDelay { get; init; } = DefaultTempFailureRetryDelay;

    /// <summary>
    /// Probes one address against the domain's MX hosts, trying each in
    /// preference order until one gives a definitive answer. Never throws for
    /// network-level failures — those become <see cref="SmtpProbeResult.Inconclusive"/>,
    /// since many hosts (including this app's own, on some deployments) block
    /// outbound port 25 entirely, and that must never be read as "invalid".
    /// </summary>
    public async Task<SmtpProbeOutcome> ProbeAsync(
        IReadOnlyList<string> mxHosts, string toAddress, int timeoutMs, CancellationToken ct)
    {
        foreach (var host in mxHosts)
        {
            var outcome = await ProbeHostAsync(host, toAddress, timeoutMs, ct);
            if (outcome.Result != SmtpProbeResult.Inconclusive) return outcome;

            // Only worth trying the next MX host if this one gave no answer at
            // all (connection-level) — a temp-failure or policy reply is this
            // host's real, considered answer, and every MX for a domain
            // enforces the same policy, so moving on wouldn't change it.
            if (outcome.Class is SmtpResponseClass.TempFailure or SmtpResponseClass.PolicyRejection) return outcome;
        }

        return SmtpProbeOutcome.Inconclusive;
    }

    /// <summary>
    /// True when the domain's mail server accepts RCPT TO for an address that
    /// almost certainly does not exist — meaning it accepts everything, so an
    /// "accepted" result for a real address on this domain proves nothing about
    /// that specific mailbox.
    /// </summary>
    public async Task<bool?> IsCatchAllAsync(IReadOnlyList<string> mxHosts, string domain, int timeoutMs, CancellationToken ct)
    {
        var probe = $"leadmine-catchall-probe-{Guid.NewGuid():N}@{domain}";

        foreach (var host in mxHosts)
        {
            var outcome = await ProbeHostAsync(host, probe, timeoutMs, ct);
            if (outcome.Result == SmtpProbeResult.Accepted) return true;
            if (outcome.Result == SmtpProbeResult.Rejected) return false;
        }

        return null;
    }

    private async Task<SmtpProbeOutcome> ProbeHostAsync(string host, string toAddress, int timeoutMs, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(timeoutMs);

        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);

        try
        {
            var first = await AttemptAsync(host, toAddress, timeout.Token);

            // Controlled retry: exactly one, and only when there's realistically
            // enough time left in the budget for a second full handshake.
            if (first.Class == SmtpResponseClass.TempFailure &&
                DateTime.UtcNow.Add(TempFailureRetryDelay).AddSeconds(2) < deadline)
            {
                logger.LogDebug("SMTP probe to {Host} got a temporary failure ({Code}); retrying once", host, first.SmtpCode);
                await Task.Delay(TempFailureRetryDelay, timeout.Token);
                return await AttemptAsync(host, toAddress, timeout.Token);
            }

            return first;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Connection refused, timed out, host down, TLS-required, port 25
            // blocked by this host's own network — all inconclusive, never a
            // reason to mark an address invalid.
            logger.LogDebug(ex, "SMTP probe to {Host} was inconclusive", host);
            return SmtpProbeOutcome.Inconclusive;
        }
    }

    private async Task<SmtpProbeOutcome> AttemptAsync(string host, string toAddress, CancellationToken ct)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(host, Port, ct);

        await using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
        await using var writer = new StreamWriter(stream, Encoding.ASCII, leaveOpen: true) { AutoFlush = true, NewLine = "\r\n" };

        var greeting = await ReadResponseAsync(reader, ct);
        if (greeting.Code is not (>= 200 and < 300)) return SmtpProbeOutcome.Inconclusive;

        await writer.WriteLineAsync($"EHLO leadmine-verify.local".AsMemory(), ct);
        var ehlo = await ReadResponseAsync(reader, ct);
        if (ehlo.Code is not (>= 200 and < 300)) return SmtpProbeOutcome.Inconclusive;

        // Null reverse-path: standard for a verification probe, never sends a body.
        await writer.WriteLineAsync("MAIL FROM:<>".AsMemory(), ct);
        var mailFrom = await ReadResponseAsync(reader, ct);
        if (mailFrom.Code is not (>= 200 and < 300)) return SmtpProbeOutcome.Inconclusive;

        await writer.WriteLineAsync($"RCPT TO:<{toAddress}>".AsMemory(), ct);
        var rcpt = await ReadResponseAsync(reader, ct);

        await writer.WriteLineAsync("QUIT".AsMemory(), ct);

        if (rcpt.Code == 0) return SmtpProbeOutcome.Inconclusive;

        SmtpResponseClassifier.TryExtractDsnCode(rcpt.Text, out var dsnCode);
        var smtpClass = SmtpResponseClassifier.Classify(rcpt.Code, dsnCode);

        var result = smtpClass switch
        {
            SmtpResponseClass.Accepted => SmtpProbeResult.Accepted,
            SmtpResponseClass.HardFailure => SmtpProbeResult.Rejected,
            _ => SmtpProbeResult.Inconclusive,
        };

        return new SmtpProbeOutcome(result, smtpClass, rcpt.Code, dsnCode);
    }

    private static async Task<(int Code, string Text)> ReadResponseAsync(StreamReader reader, CancellationToken ct)
    {
        var lastLine = string.Empty;
        int code = 0;

        // A multi-line SMTP response continues with "CODE-text" and ends with
        // "CODE text" (space, not hyphen, after the code) on the final line.
        while (true)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line is null) break;

            lastLine = line;
            if (line.Length >= 3 && int.TryParse(line.AsSpan(0, 3), out code) && (line.Length == 3 || line[3] != '-'))
            {
                break;
            }
        }

        return (code, lastLine);
    }
}
