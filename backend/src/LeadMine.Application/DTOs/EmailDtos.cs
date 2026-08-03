using System.ComponentModel.DataAnnotations;
using LeadMine.Domain.Entities;

namespace LeadMine.Application.DTOs;

// --- templates (presets) ----------------------------------------------------

public sealed class EmailTemplateDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string BodyHtml { get; set; } = string.Empty;
    public string BodyText { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public Guid? SignatureId { get; set; }
    public string? SignatureName { get; set; }
    public int TimesSent { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class CreateEmailTemplateRequest
{
    [Required, MaxLength(128)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(512)]
    public string Description { get; set; } = string.Empty;

    [Required, MaxLength(256)]
    public string Subject { get; set; } = string.Empty;

    [Required]
    public string BodyHtml { get; set; } = string.Empty;

    public string BodyText { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public Guid? SignatureId { get; set; }
}

public sealed class UpdateEmailTemplateRequest
{
    [MaxLength(128)] public string? Name { get; set; }
    [MaxLength(512)] public string? Description { get; set; }
    [MaxLength(256)] public string? Subject { get; set; }
    public string? BodyHtml { get; set; }
    public string? BodyText { get; set; }
    public bool? IsActive { get; set; }
    public Guid? SignatureId { get; set; }
}

// --- signatures -------------------------------------------------------------

public sealed class EmailSignatureDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string BodyHtml { get; set; } = string.Empty;
    public string BodyText { get; set; } = string.Empty;
    public Guid? OwnerUserId { get; set; }
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; }
}

public sealed class SaveEmailSignatureRequest
{
    [Required, MaxLength(128)]
    public string Name { get; set; } = string.Empty;

    [Required]
    public string BodyHtml { get; set; } = string.Empty;

    public string BodyText { get; set; } = string.Empty;

    /// <summary>Leave null for a shared signature any sender may use.</summary>
    public Guid? OwnerUserId { get; set; }

    public bool IsDefault { get; set; }

    public bool IsActive { get; set; } = true;
}

// --- sending ----------------------------------------------------------------

public sealed class SendEmailRequest
{
    /// <summary>The preset to send. Free composition is intentionally not offered.</summary>
    [Required]
    public Guid TemplateId { get; set; }

    /// <summary>Leads to contact. Their email addresses are resolved server-side.</summary>
    [Required, MinLength(1)]
    public List<Guid> BusinessIds { get; set; } = new();

    /// <summary>Overrides the template's signature for this send.</summary>
    public Guid? SignatureId { get; set; }

    [MaxLength(512)] public string? Cc { get; set; }
    [MaxLength(512)] public string? Bcc { get; set; }

    /// <summary>Render and log without delivering, for previewing a batch.</summary>
    public bool DryRun { get; set; }
}

public sealed class SendEmailResultDto
{
    public int Requested { get; set; }
    public int Sent { get; set; }
    public int Failed { get; set; }
    public int Skipped { get; set; }
    public List<SendEmailOutcome> Outcomes { get; set; } = new();
}

public sealed record SendEmailOutcome(Guid BusinessId, string ToEmail, string Status, string? Error);

public sealed class EmailPreviewRequest
{
    [Required] public Guid TemplateId { get; set; }
    public Guid? BusinessId { get; set; }
    public Guid? SignatureId { get; set; }
}

public sealed class EmailPreviewDto
{
    public string Subject { get; set; } = string.Empty;
    public string BodyHtml { get; set; } = string.Empty;
    public string ToEmail { get; set; } = string.Empty;
}

// --- audit log --------------------------------------------------------------

public sealed class EmailLogDto
{
    public long Id { get; set; }
    public Guid? SenderUserId { get; set; }
    public string SenderEmail { get; set; } = string.Empty;
    public string SenderName { get; set; } = string.Empty;
    public string ToEmail { get; set; } = string.Empty;
    public string ToName { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string BodyHtml { get; set; } = string.Empty;
    public string TemplateName { get; set; } = string.Empty;
    public Guid? BusinessId { get; set; }
    public EmailSendStatus Status { get; set; }
    public string? Error { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? SentAt { get; set; }
}

/// <summary>
/// Filters for the sent-email history. Date bounds are the point of this screen,
/// so both ends are supported and applied inclusively.
/// </summary>
public sealed class EmailLogQueryRequest
{
    /// <summary>Admin only; ignored for callers without `email.view-all-logs`.</summary>
    public Guid? SenderUserId { get; set; }

    public string? Search { get; set; }

    public Guid? TemplateId { get; set; }

    public EmailSendStatus? Status { get; set; }

    public DateTimeOffset? From { get; set; }

    public DateTimeOffset? To { get; set; }

    [Range(1, int.MaxValue)] public int Page { get; set; } = 1;

    [Range(1, 500)] public int PageSize { get; set; } = 50;
}

public sealed class EmailStatsDto
{
    public int TotalSent { get; set; }
    public int TotalFailed { get; set; }
    public int SentToday { get; set; }
    public int SentLast7Days { get; set; }
    public IReadOnlyList<CountByLabel> BySender { get; set; } = Array.Empty<CountByLabel>();
    public IReadOnlyList<CountByLabel> ByTemplate { get; set; } = Array.Empty<CountByLabel>();
}
