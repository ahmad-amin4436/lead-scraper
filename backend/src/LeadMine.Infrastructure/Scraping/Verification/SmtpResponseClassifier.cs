using System.Text.RegularExpressions;
using LeadMine.Domain.Enums;

namespace LeadMine.Infrastructure.Scraping.Verification;

/// <summary>
/// Turns a raw SMTP reply code plus an optional RFC 3463 extended status code
/// ("enhanced status code", the <c>X.Y.Z</c> triplet servers append to a
/// reply, e.g. <c>550 5.1.1 User unknown</c>) into a <see cref="SmtpResponseClass"/>.
/// <para>
/// Pure and stateless on purpose — no network, no I/O, nothing to mock. Both
/// <see cref="SmtpProbe"/> (pre-send RCPT TO) and <see cref="DsnMessageParser"/>
/// / <c>EmailBounceProcessorService</c> (post-send DSN parsing) read the same
/// codes and must draw the same conclusions from them, so the rule table lives
/// here once instead of being duplicated and drifting between the two.
/// </para>
/// </summary>
public static partial class SmtpResponseClassifier
{
    /// <summary>Matches an RFC 3463 extended status code: one class digit (2/4/5), then two 1-3 digit groups.</summary>
    [GeneratedRegex(@"(?<![\d.])([245])\.(\d{1,3})\.(\d{1,3})(?![\d.])", RegexOptions.None, 500)]
    private static partial Regex EnhancedStatusCodePattern();

    /// <summary>
    /// Matches a bare 3-digit SMTP failure reply code. Deliberately only 4xx/5xx
    /// — the only place this runs is a DSN's own <c>Diagnostic-Code</c>/<c>Status</c>
    /// text for a recipient group already filtered to <c>Action: failed</c>, so a
    /// 2xx substring appearing there would be noise, not the code that matters.
    /// </summary>
    [GeneratedRegex(@"(?<![\d.])([45]\d{2})(?![\d.])", RegexOptions.None, 500)]
    private static partial Regex SmtpCodePattern();

    /// <summary>
    /// 5.1.x sub-codes ("Bad destination mailbox address", RFC 3463 §3.2) that
    /// unambiguously mean the mailbox doesn't exist. Excludes 5.1.5 (address
    /// is explicitly valid — the opposite conclusion), and 5.1.0/5.1.4, which
    /// are too vague to call a hard failure.
    /// </summary>
    private static readonly HashSet<string> HardFailureDsnCodes = new(StringComparer.Ordinal)
    {
        "5.1.1", // Bad destination mailbox address
        "5.1.2", // Bad destination system address
        "5.1.3", // Bad destination mailbox address syntax
        "5.1.6", // Destination mailbox has moved, no forwarding address
        "5.1.10", // Recipient address rejected (RFC 7504)
    };

    private static readonly HashSet<int> HardFailureSmtpCodes = new() { 550, 551, 553 };

    /// <summary>
    /// Classifies one SMTP outcome. <paramref name="smtpCode"/> is the 3-digit
    /// reply code (e.g. 550); <paramref name="dsnCode"/> is the extended
    /// status code if the response carried one (e.g. "5.1.1"), independently
    /// of where it came from — a live RCPT TO reply or a DSN's own
    /// <c>Status:</c>/<c>Diagnostic-Code:</c> field.
    /// </summary>
    public static SmtpResponseClass Classify(int smtpCode, string? dsnCode)
    {
        // Checked first, regardless of the bare code: a policy/security
        // rejection (5.7.x) must never fall through to the hard-failure rule
        // below just because the server also happened to reply 550 — that
        // reply text says nothing about whether the mailbox exists.
        if (dsnCode is not null && dsnCode.StartsWith("5.7", StringComparison.Ordinal))
        {
            return SmtpResponseClass.PolicyRejection;
        }

        if (smtpCode is >= 400 and < 500)
        {
            return SmtpResponseClass.TempFailure;
        }

        if ((dsnCode is not null && HardFailureDsnCodes.Contains(dsnCode)) ||
            (dsnCode is null && HardFailureSmtpCodes.Contains(smtpCode)))
        {
            return SmtpResponseClass.HardFailure;
        }

        if (smtpCode is >= 200 and < 300)
        {
            return SmtpResponseClass.Accepted;
        }

        // Any other 5xx (mailbox full, quota, internal error, an unrecognized
        // 5.x.x sub-code, ...): a real rejection happened, but not one this
        // table can confidently call "the address doesn't exist" — see the
        // class's own remarks on why an over-eager verdict here is worse than
        // an inconclusive one.
        return SmtpResponseClass.Inconclusive;
    }

    /// <summary>The post-send label a caller acts on — see <see cref="EmailBounceType"/>'s own remarks for what each value drives.</summary>
    public static EmailBounceType ToBounceType(SmtpResponseClass smtpClass) => smtpClass switch
    {
        SmtpResponseClass.HardFailure => EmailBounceType.HardBounce,
        SmtpResponseClass.TempFailure => EmailBounceType.SoftBounce,
        SmtpResponseClass.PolicyRejection => EmailBounceType.PolicyRejection,
        _ => EmailBounceType.Unknown,
    };

    /// <summary>
    /// Extracts an RFC 3463 extended status code from free text — an SMTP
    /// response line ("550 5.1.1 User unknown") or a DSN's <c>Status:</c> /
    /// <c>Diagnostic-Code:</c> field ("smtp; 550 5.1.1 User unknown"). Picks
    /// the first match; a response never carries more than one.
    /// </summary>
    public static bool TryExtractDsnCode(string? text, out string? code)
    {
        code = null;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var match = EnhancedStatusCodePattern().Match(text);
        if (!match.Success) return false;

        code = $"{match.Groups[1].Value}.{match.Groups[2].Value}.{match.Groups[3].Value}";
        return true;
    }

    /// <summary>
    /// Extracts the first plain 3-digit SMTP reply code (4xx/5xx — 2xx is
    /// deliberately excluded since a bounce/diagnostic line never opens with
    /// one) from free text. Used when a DSN's <c>Diagnostic-Code:</c> field
    /// has no RFC 3463 triplet, only the bare reply ("smtp; 550 no such user").
    /// </summary>
    public static bool TryExtractSmtpCode(string? text, out int code)
    {
        code = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var match = SmtpCodePattern().Match(text);
        if (!match.Success || !int.TryParse(match.Groups[1].Value, out code)) return false;

        return true;
    }
}
