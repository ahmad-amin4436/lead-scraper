using LeadMine.Domain.Common;

namespace LeadMine.Domain.Entities;

/// <summary>
/// An admin-authored WhatsApp message preset.
/// <para>
/// Mirrors <see cref="EmailTemplate"/>'s role — users pick from these rather
/// than composing freely — but simpler: WhatsApp click-to-chat messages are
/// plain text with no subject line and no separate signature block, so this
/// is one field instead of three. <c>{{placeholders}}</c> are substituted from
/// the recipient lead the same way email presets are.
/// </para>
/// </summary>
public class WhatsAppTemplate : AuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    /// <summary>Plain text. Placeholders are replaced before the link is built.</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>Inactive presets stay for audit history but cannot be used.</summary>
    public bool IsActive { get; set; } = true;

    public ICollection<WhatsAppContactLog> Contacts { get; set; } = new List<WhatsAppContactLog>();
}

/// <summary>
/// One click-to-chat link opened for a lead. Append-only, mirroring
/// <see cref="EmailLog"/> — but there is no delivery status to record here: a
/// click-to-chat link only proves the sender opened WhatsApp with the message
/// ready to go, not that they pressed send inside it. Read this as "contact was
/// initiated", not "message was delivered".
/// </summary>
public class WhatsAppContactLog
{
    public long Id { get; set; }

    public Guid? SenderUserId { get; set; }

    /// <summary>Denormalised so the log survives the user being deleted.</summary>
    public string SenderEmail { get; set; } = string.Empty;

    public Guid BusinessId { get; set; }

    public string ToName { get; set; } = string.Empty;

    public string ToPhone { get; set; } = string.Empty;

    public Guid? TemplateId { get; set; }

    public WhatsAppTemplate? Template { get; set; }

    /// <summary>Denormalised: the preset may be renamed or deactivated later.</summary>
    public string TemplateName { get; set; } = string.Empty;

    /// <summary>The rendered text exactly as placed in the click-to-chat link.</summary>
    public string Message { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
