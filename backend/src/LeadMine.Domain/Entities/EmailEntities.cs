using LeadMine.Domain.Common;

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

    /// <summary>Message-ID assigned by the SMTP server, for tracing.</summary>
    public string? MessageId { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? SentAt { get; set; }

    public long? DurationMs { get; set; }
}
