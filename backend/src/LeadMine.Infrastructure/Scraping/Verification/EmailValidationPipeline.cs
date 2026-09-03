using System.Text.Json;
using LeadMine.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LeadMine.Infrastructure.Scraping.Verification;

/// <summary>
/// The free-form part of a validation result — everything that doesn't need
/// its own indexed column on <c>Business</c>. Serialized to
/// <c>Business.EmailValidationDetailsJson</c>. Every field here is what makes
/// a validation decision explainable after the fact, not just a bare status.
/// </summary>
public sealed record EmailValidationDetails(
    string NormalizedEmail,
    string Reason,
    IReadOnlyList<string> MxHosts,
    string? SmtpProbeResult,
    SmtpResponseClass? SmtpClass,
    int? SmtpCode,
    string? DsnCode,
    DateTimeOffset EvaluatedAt);

/// <param name="IsCatchAll">Null when the SMTP probe never ran; see <see cref="EmailValidationOptions.EnableSmtpProbe"/>.</param>
/// <param name="ProbeAttempted">
/// True only when the RCPT TO probe actually ran — false whenever it was
/// skipped, for any reason (feature off, already ruled out by DNS,
/// disposable, or the per-domain-per-hour budget was exhausted). Distinct
/// from <paramref name="IsCatchAll"/> being null, which can also mean "ran,
/// but the catch-all check itself came back inconclusive".
/// </param>
/// <param name="NeedsRetry">
/// True when the probe got a temporary failure (4xx after its own bounded
/// retry) — the caller should count this against <c>Business.EmailRetryCount</c>
/// and leave the address eligible to be probed again on a later sweep,
/// rather than treating <paramref name="ProbeAttempted"/> as "done".
/// </param>
public sealed record EmailValidationOutcome(
    EmailStatus Status,
    int Confidence,
    bool IsDisposable,
    bool IsRoleAccount,
    bool? IsCatchAll,
    bool ProbeAttempted,
    bool NeedsRetry,
    EmailValidationDetails Details)
{
    public string DetailsJson => JsonSerializer.Serialize(Details);
}

/// <summary>
/// Runs steps 1-9 of the email validation pipeline for one address: normalize,
/// syntax, DNS, MX, disposable, role-account (all via <see cref="EmailVerifier"/>),
/// then — only if <see cref="EmailValidationOptions.EnableSmtpProbe"/> is on —
/// catch-all detection and an RCPT TO probe via <see cref="SmtpProbe"/>, and
/// finally a confidence score. Storing the result (step 10) and the bounce
/// check (step 11) are the callers' job:
/// <see cref="EmailValidationWorkerService"/> and
/// <see cref="EmailBounceCheckWorkerService"/> respectively.
/// </summary>
public sealed class EmailValidationPipeline(
    EmailVerifier verifier,
    SmtpProbe smtpProbe,
    SmtpProbeDomainRateLimiter probeRateLimiter,
    IOptionsMonitor<EmailValidationOptions> optionsMonitor,
    ILogger<EmailValidationPipeline> logger)
{
    /// <param name="email">The address to validate.</param>
    /// <param name="forceSmtpProbe">
    /// Overrides <see cref="EmailValidationOptions.EnableSmtpProbe"/> for this
    /// one call — used by the on-demand <c>POST api/email/verify</c> endpoint,
    /// where a caller explicitly asked for the strongest available signal on a
    /// single address, as opposed to the continuous background sweep
    /// (<see cref="EmailValidationWorkerService"/>), which must stay
    /// conservative because it runs across the whole lead table unattended.
    /// Null (the default) defers to the configured option, as before.
    /// </param>
    public async Task<EmailValidationOutcome?> ValidateAsync(string? email, CancellationToken ct, bool? forceSmtpProbe = null)
    {
        var normalized = (email ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length == 0) return null;

        var baseResult = await verifier.VerifyAsync(normalized, ct);
        var mxHosts = baseResult.MxHosts ?? [];

        var status = baseResult.Status;
        var confidence = BaseConfidence(status, baseResult.IsDisposable, baseResult.IsRoleAccount);
        string? smtpOutcome = null;
        bool? isCatchAll = null;
        SmtpResponseClass? smtpClass = null;
        int? smtpCode = null;
        string? dsnCode = null;
        var needsRetry = false;

        var options = optionsMonitor.CurrentValue;
        var smtpProbeEnabled = forceSmtpProbe ?? options.EnableSmtpProbe;

        // No point probing an address DNS already ruled out, and never probe a
        // disposable domain — the mailbox may well exist, but the address is
        // still not one worth using.
        var worthProbing = smtpProbeEnabled
            && status is EmailStatus.Valid or EmailStatus.Risky or EmailStatus.Unknown
            && !baseResult.IsDisposable
            && mxHosts.Count > 0;

        // The actual guard against hammering one mail provider. An explicit,
        // caller-requested check (forceSmtpProbe: true — the on-demand verify
        // endpoint) bypasses it: that's a single ad-hoc lookup, not a sweep
        // that could repeat against the same domain many times a minute.
        if (worthProbing && forceSmtpProbe != true)
        {
            var domain = ExtractDomain(normalized);
            worthProbing = probeRateLimiter.TryAcquire(domain, options.SmtpProbeMaxPerHourPerDomain);
        }

        // Captured before the probe runs — worthProbing already reflects the
        // rate-limit decision above, so a lead skipped for budget reasons is
        // correctly reported as "not attempted" and stays eligible to retry.
        var probeAttempted = worthProbing;

        if (worthProbing)
        {
            try
            {
                isCatchAll = await smtpProbe.IsCatchAllAsync(mxHosts, ExtractDomain(normalized), options.SmtpProbeTimeoutMs, ct);
                var probe = await smtpProbe.ProbeAsync(mxHosts, normalized, options.SmtpProbeTimeoutMs, ct);
                smtpOutcome = probe.Result.ToString();
                smtpClass = probe.Class;
                smtpCode = probe.SmtpCode == 0 ? null : probe.SmtpCode;
                dsnCode = probe.DsnCode;
                needsRetry = probe.Class == SmtpResponseClass.TempFailure;

                logger.LogDebug(
                    "SMTP probe for {Domain}: {Result} ({Class}, code={Code}, dsn={Dsn}, catchAll={CatchAll})",
                    ExtractDomain(normalized), probe.Result, probe.Class, probe.SmtpCode, probe.DsnCode, isCatchAll);

                (status, confidence) = ApplyProbeResult(status, confidence, probe, isCatchAll);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "SMTP probe pipeline step failed for a domain; leaving DNS-based result in place");
            }
        }

        var details = new EmailValidationDetails(
            normalized,
            baseResult.Reason,
            mxHosts,
            smtpOutcome,
            smtpClass,
            smtpCode,
            dsnCode,
            DateTimeOffset.UtcNow);

        return new EmailValidationOutcome(
            status, confidence, baseResult.IsDisposable, baseResult.IsRoleAccount, isCatchAll, probeAttempted, needsRetry, details);
    }

    private static int BaseConfidence(EmailStatus status, bool isDisposable, bool isRoleAccount) => status switch
    {
        EmailStatus.Invalid => 0,
        EmailStatus.Unknown => 20,
        EmailStatus.Risky when isDisposable => 10,
        EmailStatus.Risky => 40,
        EmailStatus.Valid when isRoleAccount => 75,
        EmailStatus.Valid => 85,
        _ => 20,
    };

    private static (EmailStatus Status, int Confidence) ApplyProbeResult(
        EmailStatus status, int confidence, SmtpProbeOutcome probe, bool? isCatchAll)
    {
        // A live rejection from the recipient's own mail server outranks every
        // DNS-only signal, regardless of what came before it — but only a hard
        // failure. Everything else below is deliberately conservative: a 4xx
        // or a 5.7.x is a real reply, just not one that proves the mailbox
        // doesn't exist (see SmtpResponseClass's own remarks).
        if (probe.Class == SmtpResponseClass.HardFailure) return (EmailStatus.Invalid, 3);

        if (probe.Result == SmtpProbeResult.Accepted)
        {
            // A catch-all domain accepts every address, so "accepted" here
            // proves nothing about this specific mailbox — keep the DNS-based
            // status but don't let confidence claim more certainty than exists.
            if (isCatchAll == true) return (status, Math.Max(confidence, 55));

            return (EmailStatus.Valid, 97);
        }

        // Temp failure / policy rejection / no answer at all: the probe
        // didn't produce a trustworthy verdict either way, so the DNS-based
        // result stands. A policy rejection specifically must never be read
        // as "doesn't exist" — it's a decision about this send, not the
        // address — so status is left untouched here on purpose.
        return (status, confidence);
    }

    private static string ExtractDomain(string email)
    {
        var at = email.LastIndexOf('@');
        return at >= 0 ? email[(at + 1)..] : email;
    }
}
