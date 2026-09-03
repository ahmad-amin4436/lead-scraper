using LeadMine.Domain.Common;
using LeadMine.Domain.Enums;

namespace LeadMine.Domain.Entities;

/// <summary>
/// An admin-authored email preset.
/// <para>
/// Users send from these rather than composing freely, so outbound messaging
/// stays on-message and auditable. Bodies support <c>{{placeholders}}</c> that
/// are substituted from the recipient lead at send time.
/// </para>
/// </summary>
public class EmailTemplate : AuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string Subject { get; set; } = string.Empty;

    /// <summary>HTML body. Placeholders are replaced before sending.</summary>
    public string BodyHtml { get; set; } = string.Empty;

    /// <summary>
    /// Plain-text alternative. Generated from the HTML when left blank — a
    /// message without one is far more likely to be treated as spam.
    /// </summary>
    public string BodyText { get; set; } = string.Empty;

    /// <summary>Inactive presets stay for audit history but cannot be sent.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Signature appended to every send using this preset.</summary>
    public Guid? SignatureId { get; set; }

    public EmailSignature? Signature { get; set; }

    public ICollection<EmailLog> Sends { get; set; } = new List<EmailLog>();
}

/// <summary>
/// A reusable sign-off block. Kept separate from templates so a user's own
/// signature can travel across every preset they send.
/// </summary>
public class EmailSignature : AuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    public string BodyHtml { get; set; } = string.Empty;

    public string BodyText { get; set; } = string.Empty;

    /// <summary>
    /// When set, this signature belongs to one user. Null means it is a shared
    /// organisation signature any sender may use.
    /// </summary>
    public Guid? OwnerUserId { get; set; }

    /// <summary>The fallback when a template names no signature.</summary>
    public bool IsDefault { get; set; }

    public bool IsActive { get; set; } = true;
}

public enum EmailSendStatus
{
    Queued = 0,
    Sent = 1,
    Failed = 2,

    /// <summary>
    /// The send itself succeeded (SMTP accepted it), but a delivery-failure
    /// notice was later matched back to this message by
    /// <c>EmailBounceProcessorService</c>. Distinct from <see cref="Failed"/>,
    /// which means the SMTP send attempt itself was rejected synchronously —
    /// this is an asynchronous verdict that can arrive minutes to days later.
    /// </summary>
    Bounced = 3,
}

/// <summary>
/// One outbound email. Append-only.
/// <para>
/// This is the record that answers "how many, what, and to whom" for any user
/// over any date range, so it stores the rendered subject and body actually
/// delivered rather than a pointer to a template that may later change.
/// </para>
/// </summary>
public class EmailLog
{
    public long Id { get; set; }

    public Guid? SenderUserId { get; set; }

    /// <summary>Denormalised so the log survives the user being deleted.</summary>
    public string SenderEmail { get; set; } = string.Empty;

    public string SenderName { get; set; } = string.Empty;

    public string ToEmail { get; set; } = string.Empty;

    public string ToName { get; set; } = string.Empty;

    public string? Cc { get; set; }

    public string? Bcc { get; set; }

    public string Subject { get; set; } = string.Empty;

    /// <summary>The rendered HTML exactly as delivered.</summary>
    public string BodyHtml { get; set; } = string.Empty;

    public Guid? TemplateId { get; set; }

    public EmailTemplate? Template { get; set; }

    /// <summary>Denormalised: the preset may be renamed or deactivated later.</summary>
    public string TemplateName { get; set; } = string.Empty;

    /// <summary>The lead this was sent to, when it came from the database.</summary>
    public Guid? BusinessId { get; set; }

    public EmailSendStatus Status { get; set; } = EmailSendStatus.Queued;

    public string? Error { get; set; }

    /// <summary>
    /// The <c>Message-Id</c> header this message was sent with (assigned
    /// before sending, not read back from the server) — how
    /// <c>EmailBounceProcessorService</c> can match a later DSN to this exact
    /// send when the bounce carries the original message as a
    /// <c>message/rfc822</c> attachment, which is more precise than matching
    /// on recipient address and a time window alone.
    /// </summary>
    public string? MessageId { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? SentAt { get; set; }

    public long? DurationMs { get; set; }

    // --- Post-send bounce processing -----------------------------------------
    // Set only when Status transitions to Bounced. See EmailBounceProcessorService
    // and Business's own EmailBounceType/EmailBounceStatus for the lead-level
    // rollup this feeds into.

    public EmailBounceType? BounceType { get; set; }

    /// <summary>The DSN's own reason line — <c>Diagnostic-Code</c> if present, else <c>Status</c>, else a heuristic subject.</summary>
    public string? BounceReason { get; set; }

    /// <summary>The raw SMTP reply code from the DSN (e.g. 550), when one could be parsed out of it.</summary>
    public int? BounceSmtpCode { get; set; }

    /// <summary>The RFC 3463 extended status code (e.g. "5.1.1"), when the DSN carried one.</summary>
    public string? BounceDsnCode { get; set; }

    public DateTimeOffset? BounceDetectedAt { get; set; }
}
