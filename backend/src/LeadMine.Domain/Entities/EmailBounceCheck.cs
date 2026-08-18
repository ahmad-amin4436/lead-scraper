using LeadMine.Domain.Enums;

namespace LeadMine.Domain.Entities;

/// <summary>
/// One send-and-watch-for-bounce attempt for a lead's email address —
/// <c>EmailBounceCheckWorkerService</c>'s durable record of "we sent a probe,
/// here's what we're waiting to find out". Plain entity, not
/// <see cref="Common.AuditableEntity"/>: this is operational tracking state for
/// a background process, not user-authored data worth a soft-delete/audit trail.
/// </summary>
public class EmailBounceCheck
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid BusinessId { get; set; }

    public Business? Business { get; set; }

    /// <summary>
    /// The address as it was when the probe was sent. Kept separate from
    /// <c>Business.Email</c> so a later edit to the lead's email doesn't orphan
    /// an in-flight check against the old address.
    /// </summary>
    public string Email { get; set; } = string.Empty;

    public EmailBounceCheckStatus Status { get; set; } = EmailBounceCheckStatus.Pending;

    public DateTimeOffset SentAt { get; set; }

    /// <summary>
    /// The <c>Message-Id</c> the probe was sent with, so an incoming DSN/NDR
    /// that references it (many mail servers include the original headers) can
    /// be matched precisely rather than only by recipient-address text search.
    /// </summary>
    public string MessageId { get; set; } = string.Empty;

    public DateTimeOffset? ResolvedAt { get; set; }

    /// <summary>Set when <see cref="Status"/> is <see cref="EmailBounceCheckStatus.Bounced"/> — the bounce message's own reason line, if one was present.</summary>
    public string? BounceReason { get; set; }
}
