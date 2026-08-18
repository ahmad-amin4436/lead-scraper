using System.Text.Json;
using LeadMine.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LeadMine.Infrastructure.Scraping.Verification;

/// <summary>
/// The free-form part of a validation result — everything that doesn't need
/// its own indexed column on <c>Business</c>. Serialized to
/// <c>Business.EmailValidationDetailsJson</c>.
/// </summary>
public sealed record EmailValidationDetails(
    string NormalizedEmail,
    string Reason,
    IReadOnlyList<string> MxHosts,
    string? SmtpProbeResult,
    DateTimeOffset EvaluatedAt);

/// <param name="IsCatchAll">Null when the SMTP probe never ran; see <see cref="EmailValidationOptions.EnableSmtpProbe"/>.</param>
public sealed record EmailValidationOutcome(
    EmailStatus Status,
    int Confidence,
    bool IsDisposable,
    bool IsRoleAccount,
    bool? IsCatchAll,
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
    IOptionsMonitor<EmailValidationOptions> optionsMonitor,
    ILogger<EmailValidationPipeline> logger)
{
    public async Task<EmailValidationOutcome?> ValidateAsync(string? email, CancellationToken ct)
    {
        var normalized = (email ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length == 0) return null;

        var baseResult = await verifier.VerifyAsync(normalized, ct);
        var mxHosts = baseResult.MxHosts ?? [];

        var status = baseResult.Status;
        var confidence = BaseConfidence(status, baseResult.IsDisposable, baseResult.IsRoleAccount);
        string? smtpOutcome = null;
        bool? isCatchAll = null;

        var options = optionsMonitor.CurrentValue;

        // No point probing an address DNS already ruled out, and never probe a
        // disposable domain — the mailbox may well exist, but the address is
        // still not one worth using.
        var worthProbing = options.EnableSmtpProbe
            && status is EmailStatus.Valid or EmailStatus.Risky or EmailStatus.Unknown
            && !baseResult.IsDisposable
            && mxHosts.Count > 0;

        if (worthProbing)
        {
            try
            {
                isCatchAll = await smtpProbe.IsCatchAllAsync(mxHosts, ExtractDomain(normalized), options.SmtpProbeTimeoutMs, ct);
                var probeResult = await smtpProbe.ProbeAsync(mxHosts, normalized, options.SmtpProbeTimeoutMs, ct);
                smtpOutcome = probeResult.ToString();

                (status, confidence) = ApplyProbeResult(status, confidence, probeResult, isCatchAll);
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
            DateTimeOffset.UtcNow);

        return new EmailValidationOutcome(status, confidence, baseResult.IsDisposable, baseResult.IsRoleAccount, isCatchAll, details);
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
        EmailStatus status, int confidence, SmtpProbeResult probe, bool? isCatchAll)
    {
        // A live rejection from the recipient's own mail server outranks every
        // DNS-only signal, regardless of what came before it.
        if (probe == SmtpProbeResult.Rejected) return (EmailStatus.Invalid, 3);

        if (probe == SmtpProbeResult.Accepted)
        {
            // A catch-all domain accepts every address, so "accepted" here
            // proves nothing about this specific mailbox — keep the DNS-based
            // status but don't let confidence claim more certainty than exists.
            if (isCatchAll == true) return (status, Math.Max(confidence, 55));

            return (EmailStatus.Valid, 97);
        }

        // Inconclusive: the probe didn't help either way.
        return (status, confidence);
    }

    private static string ExtractDomain(string email)
    {
        var at = email.LastIndexOf('@');
        return at >= 0 ? email[(at + 1)..] : email;
    }
}
