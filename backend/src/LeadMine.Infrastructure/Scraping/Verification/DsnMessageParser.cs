using MimeKit;

namespace LeadMine.Infrastructure.Scraping.Verification;

/// <summary>
/// One recipient's outcome pulled out of a delivery-status notification (DSN)
/// — RFC 3464's structured <c>message/delivery-status</c> part when the
/// sending server sent one (Gmail and most major providers do), or a
/// best-effort heuristic reading of an unstructured bounce otherwise.
/// </summary>
/// <param name="FinalRecipient">The address that failed — from <c>Final-Recipient</c>, falling back to <c>Original-Recipient</c>.</param>
/// <param name="Action">The DSN's <c>Action:</c> field ("failed", "delayed", "delivered", ...), when present.</param>
/// <param name="Status">The DSN's own <c>Status:</c> field — an RFC 3463 code as text, e.g. "5.1.1".</param>
/// <param name="DiagnosticCode">The DSN's <c>Diagnostic-Code:</c> field — usually <c>smtp; 550 5.1.1 User unknown</c> or similar.</param>
/// <param name="RemoteMta">The DSN's <c>Remote-MTA:</c> field — which server actually issued the rejection, when present.</param>
/// <param name="Reason">Best single human-readable line for display: <see cref="DiagnosticCode"/>, else <see cref="Status"/>, else a heuristic subject line.</param>
/// <param name="SmtpCode">The 3-digit SMTP reply code, parsed out of <see cref="DiagnosticCode"/> or <see cref="Status"/> if one could be found.</param>
/// <param name="DsnCode">The RFC 3463 extended status code, parsed the same way.</param>
/// <param name="IsStructured">True when this came from an actual RFC 3464 delivery-status part rather than a heuristic guess.</param>
public sealed record ParsedBounce(
    string? FinalRecipient,
    string? Action,
    string? Status,
    string? DiagnosticCode,
    string? RemoteMta,
    string? Reason,
    int? SmtpCode,
    string? DsnCode,
    bool IsStructured);

/// <summary>
/// Reads bounce/DSN messages. Pure and stateless — no network, nothing to
/// mock — so every branch here is directly unit-testable against an in-memory
/// <see cref="MimeMessage"/>. Shared by <see cref="EmailBounceCheckWorkerService"/>
/// (matches against its own synthetic probe sends) and
/// <c>EmailBounceProcessorService</c> (matches against real campaign sends) —
/// the parsing rules are identical, only what each caller does with the
/// result differs.
/// </summary>
public static class DsnMessageParser
{
    /// <summary>
    /// The RFC 3464 structured format — what Gmail and most major providers
    /// send. Reliable when present: the recipient and its status come
    /// straight from dedicated fields, not a text guess.
    /// </summary>
    public static ParsedBounce? TryParseStructured(MimeMessage message)
    {
        IEnumerable<MimeEntity> bodyParts;
        try
        {
            bodyParts = message.BodyParts;
        }
        catch
        {
            // A malformed MIME structure throws while walking it rather than
            // returning an empty list — never let one bad message take down
            // a whole scan tick.
            return null;
        }

        foreach (var part in bodyParts)
        {
            if (part is not MessageDeliveryStatus status) continue;

            HeaderListCollection groups;
            try
            {
                groups = status.StatusGroups;
            }
            catch
            {
                continue;
            }

            // StatusGroups[0] is the per-message fields; recipient groups follow.
            foreach (var group in groups.Skip(1))
            {
                var action = group["Action"];
                if (action is null || !action.Contains("fail", StringComparison.OrdinalIgnoreCase)) continue;

                var finalRecipient = group["Final-Recipient"] ?? group["Original-Recipient"];
                var cleanedRecipient = finalRecipient is null ? null : CleanAddress(finalRecipient);
                if (cleanedRecipient is null) continue;

                var dsnStatus = group["Status"];
                var diagnosticCode = group["Diagnostic-Code"];
                var remoteMta = group["Remote-MTA"];

                var (smtpCode, dsnCode) = ExtractCodes(diagnosticCode, dsnStatus);
                var reason = diagnosticCode ?? dsnStatus;

                return new ParsedBounce(
                    cleanedRecipient, action, dsnStatus, diagnosticCode, remoteMta, reason, smtpCode, dsnCode, IsStructured: true);
            }
        }

        return null;
    }

    /// <summary>From/subject heuristics for servers that don't send a structured DSN.</summary>
    public static bool LooksLikeBounce(MimeMessage message, out string? reason)
    {
        var from = message.From.Mailboxes.FirstOrDefault()?.Address ?? string.Empty;
        var subject = message.Subject ?? string.Empty;
        reason = subject;

        return
            from.Contains("mailer-daemon", StringComparison.OrdinalIgnoreCase) ||
            from.Contains("postmaster", StringComparison.OrdinalIgnoreCase) ||
            subject.Contains("undeliver", StringComparison.OrdinalIgnoreCase) ||
            subject.Contains("delivery status notification", StringComparison.OrdinalIgnoreCase) ||
            subject.Contains("failure notice", StringComparison.OrdinalIgnoreCase) ||
            subject.Contains("returned to sender", StringComparison.OrdinalIgnoreCase) ||
            subject.Contains("address not found", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Builds an unstructured-fallback <see cref="ParsedBounce"/> once a
    /// pending recipient has been located in the message text (see
    /// <see cref="FindAddressInText"/>) — best-effort codes pulled from
    /// whatever plain text the message body actually contains.
    /// </summary>
    public static ParsedBounce BuildHeuristicBounce(string recipient, string? subjectReason, MimeMessage message)
    {
        var text = SafeText(message);
        var (smtpCode, dsnCode) = ExtractCodes(text, null);

        return new ParsedBounce(
            recipient, Action: "failed", Status: dsnCode, DiagnosticCode: null, RemoteMta: null,
            Reason: subjectReason, SmtpCode: smtpCode, DsnCode: dsnCode, IsStructured: false);
    }

    /// <summary>
    /// Scans a bounce-looking message's plain-text body for any address still
    /// awaiting a result. Last resort, used only when the message carries no
    /// structured recipient field at all.
    /// </summary>
    public static string? FindAddressInText(MimeMessage message, IEnumerable<string> pendingAddresses)
    {
        var text = SafeText(message);
        if (text.Length == 0) return null;

        foreach (var address in pendingAddresses)
        {
            if (text.Contains(address, StringComparison.OrdinalIgnoreCase)) return address;
        }

        return null;
    }

    /// <summary>
    /// Looks for the original outbound message attached as <c>message/rfc822</c>
    /// (how Gmail and most providers include "what you sent" in a bounce) and
    /// returns its <c>Message-Id</c>, if any — a far more precise way to match
    /// a bounce back to one exact send than recipient address plus a time
    /// window, when it's available.
    /// </summary>
    public static string? TryFindOriginalMessageId(MimeMessage message)
    {
        IEnumerable<MimeEntity> bodyParts;
        try
        {
            bodyParts = message.BodyParts;
        }
        catch
        {
            return null;
        }

        foreach (var part in bodyParts)
        {
            if (part is not MessagePart embedded) continue;

            try
            {
                var id = embedded.Message?.MessageId;
                if (!string.IsNullOrWhiteSpace(id)) return id;
            }
            catch
            {
                // A malformed embedded message is still just "no Message-Id found here".
            }
        }

        return null;
    }

    private static string SafeText(MimeMessage message)
    {
        try
        {
            return message.TextBody ?? message.HtmlBody ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static (int? SmtpCode, string? DsnCode) ExtractCodes(string? primary, string? secondary)
    {
        string? dsnCode = null;
        if (SmtpResponseClassifier.TryExtractDsnCode(primary, out var fromPrimary)) dsnCode = fromPrimary;
        else if (SmtpResponseClassifier.TryExtractDsnCode(secondary, out var fromSecondary)) dsnCode = fromSecondary;

        int? smtpCode = null;
        if (SmtpResponseClassifier.TryExtractSmtpCode(primary, out var codeFromPrimary)) smtpCode = codeFromPrimary;
        else if (SmtpResponseClassifier.TryExtractSmtpCode(secondary, out var codeFromSecondary)) smtpCode = codeFromSecondary;

        return (smtpCode, dsnCode);
    }

    private static string? CleanAddress(string raw)
    {
        var value = raw.Trim();
        var prefixIndex = value.IndexOf(';');
        if (prefixIndex >= 0) value = value[(prefixIndex + 1)..].Trim();

        try
        {
            return MailboxAddress.Parse(value).Address;
        }
        catch
        {
            return value.Length > 0 ? value : null;
        }
    }
}
