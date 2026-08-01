using LeadMine.Domain.Common;
using LeadMine.Domain.Enums;

namespace LeadMine.Domain.Entities;

/// <summary>A discovered business lead.</summary>
public class Business : AuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    public string Category { get; set; } = string.Empty;

    public string Country { get; set; } = string.Empty;

    public string State { get; set; } = string.Empty;

    public string City { get; set; } = string.Empty;

    public string Address { get; set; } = string.Empty;

    public string Phone { get; set; } = string.Empty;

    public string Website { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public EmailStatus EmailStatus { get; set; } = EmailStatus.Unverified;

    public string WhatsApp { get; set; } = string.Empty;

    public WhatsAppStatus WhatsAppStatus { get; set; } = WhatsAppStatus.Unverified;

    public string Facebook { get; set; } = string.Empty;

    public string Instagram { get; set; } = string.Empty;

    public string LinkedIn { get; set; } = string.Empty;

    public double? Latitude { get; set; }

    public double? Longitude { get; set; }

    public double? Rating { get; set; }

    public int? ReviewCount { get; set; }

    public string MapsUrl { get; set; } = string.Empty;

    public BusinessSource Source { get; set; } = BusinessSource.Manual;

    public BusinessStatus Status { get; set; } = BusinessStatus.New;

    public string Notes { get; set; } = string.Empty;

    /// <summary>The run that produced this lead, if it came from a search.</summary>
    public Guid? SearchJobId { get; set; }

    public SearchJob? SearchJob { get; set; }

    /// <summary>
    /// Normalised website host, phone and name+city keys used for duplicate
    /// detection. Persisted (not computed on read) so they can be indexed —
    /// scanning 100k rows per insert would not scale.
    /// </summary>
    public string? DedupeWebsiteKey { get; set; }

    public string? DedupePhoneKey { get; set; }

    public string? DedupeNameKey { get; set; }
}
