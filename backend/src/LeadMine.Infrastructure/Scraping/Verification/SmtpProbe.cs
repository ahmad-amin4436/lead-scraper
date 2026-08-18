using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;

namespace LeadMine.Infrastructure.Scraping.Verification;

public enum SmtpProbeResult
{
    /// <summary>The server accepted RCPT TO for the address (250/251).</summary>
    Accepted,

    /// <summary>The server rejected the address as non-existent (550/551/553).</summary>
    Rejected,

    /// <summary>Connection refused, timed out, TLS-only, greylisted, or any other non-definitive outcome.</summary>
    Inconclusive,
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
    private const int SmtpPort = 25;

    /// <summary>
    /// Probes one address against the domain's MX hosts, trying each in
    /// preference order until one gives a definitive answer. Never throws for
    /// network-level failures — those become <see cref="SmtpProbeResult.Inconclusive"/>,
    /// since many hosts (including this app's own, on some deployments) block
    /// outbound port 25 entirely, and that must never be read as "invalid".
    /// </summary>
    public async Task<SmtpProbeResult> ProbeAsync(
        IReadOnlyList<string> mxHosts, string toAddress, int timeoutMs, CancellationToken ct)
    {
        foreach (var host in mxHosts)
        {
            var result = await ProbeHostAsync(host, toAddress, timeoutMs, ct);
            if (result != SmtpProbeResult.Inconclusive) return result;
        }

        return SmtpProbeResult.Inconclusive;
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
            var result = await ProbeHostAsync(host, probe, timeoutMs, ct);
            if (result == SmtpProbeResult.Accepted) return true;
            if (result == SmtpProbeResult.Rejected) return false;
        }

        return null;
    }

    private async Task<SmtpProbeResult> ProbeHostAsync(string host, string toAddress, int timeoutMs, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(timeoutMs);

        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(host, SmtpPort, timeout.Token);

            await using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
            await using var writer = new StreamWriter(stream, Encoding.ASCII, leaveOpen: true) { AutoFlush = true, NewLine = "\r\n" };

            var greeting = await ReadResponseAsync(reader, timeout.Token);
            if (greeting.Code is not (>= 200 and < 300)) return SmtpProbeResult.Inconclusive;

            await writer.WriteLineAsync($"EHLO leadmine-verify.local".AsMemory(), timeout.Token);
            var ehlo = await ReadResponseAsync(reader, timeout.Token);
            if (ehlo.Code is not (>= 200 and < 300)) return SmtpProbeResult.Inconclusive;

            // Null reverse-path: standard for a verification probe, never sends a body.
            await writer.WriteLineAsync("MAIL FROM:<>".AsMemory(), timeout.Token);
            var mailFrom = await ReadResponseAsync(reader, timeout.Token);
            if (mailFrom.Code is not (>= 200 and < 300)) return SmtpProbeResult.Inconclusive;

            await writer.WriteLineAsync($"RCPT TO:<{toAddress}>".AsMemory(), timeout.Token);
            var rcpt = await ReadResponseAsync(reader, timeout.Token);

            await writer.WriteLineAsync("QUIT".AsMemory(), timeout.Token);

            return rcpt.Code switch
            {
                >= 200 and < 300 => SmtpProbeResult.Accepted,
                550 or 551 or 553 => SmtpProbeResult.Rejected,
                _ => SmtpProbeResult.Inconclusive,
            };
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
            return SmtpProbeResult.Inconclusive;
        }
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
