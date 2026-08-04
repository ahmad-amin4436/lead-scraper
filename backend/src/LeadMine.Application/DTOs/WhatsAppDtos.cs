using System.ComponentModel.DataAnnotations;

namespace LeadMine.Application.DTOs;

// --- templates (presets) ----------------------------------------------------

public sealed class WhatsAppTemplateDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public int TimesUsed { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class CreateWhatsAppTemplateRequest
{
    [Required, MaxLength(128)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(512)]
    public string Description { get; set; } = string.Empty;

    [Required, MaxLength(4096)]
    public string Message { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;
}

public sealed class UpdateWhatsAppTemplateRequest
{
    [MaxLength(128)] public string? Name { get; set; }
    [MaxLength(512)] public string? Description { get; set; }
    [MaxLength(4096)] public string? Message { get; set; }
    public bool? IsActive { get; set; }
}

// --- click-to-chat links ------------------------------------------------------

public sealed class GenerateWhatsAppLinksRequest
{
    /// <summary>The preset to render. Free composition is intentionally not offered.</summary>
    [Required]
    public Guid TemplateId { get; set; }

    /// <summary>Leads to build links for. Numbers are resolved server-side.</summary>
    [Required, MinLength(1)]
    public List<Guid> BusinessIds { get; set; } = new();
}

/// <summary>
/// One ready-to-click link. There is no bulk "send" — the caller opens each of
/// these themselves, one at a time, in their own WhatsApp.
/// </summary>
public sealed class WhatsAppLinkDto
{
    public Guid BusinessId { get; set; }
    public string BusinessName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;

    /// <summary>Null when the lead was skipped — see <see cref="SkipReason"/>.</summary>
    public string? SkipReason { get; set; }
}

public sealed class GenerateWhatsAppLinksResultDto
{
    public int Requested { get; set; }
    public List<WhatsAppLinkDto> Links { get; set; } = new();
}

public sealed class WhatsAppPreviewRequest
{
    [Required] public Guid TemplateId { get; set; }
    public Guid? BusinessId { get; set; }
}

public sealed class WhatsAppPreviewDto
{
    public string Message { get; set; } = string.Empty;
    public string ToPhone { get; set; } = string.Empty;
}

/// <summary>
/// Records that a click-to-chat link was opened. The frontend calls this at
/// the moment the user clicks — there is no server-side send to hang the
/// record off, so the click itself is the event.
/// </summary>
public sealed class MarkWhatsAppContactedRequest
{
    [Required] public Guid BusinessId { get; set; }
    public Guid? TemplateId { get; set; }

    /// <summary>The rendered message the link was built from, for the log.</summary>
    [MaxLength(4096)] public string? Message { get; set; }
}
