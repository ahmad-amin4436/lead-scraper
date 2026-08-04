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
    /// The user who scraped or created this lead.
    /// <para>
    /// Every lead query is filtered by this unless the caller holds
    /// <c>leads.view-all</c>, so one user's prospecting is never visible to
    /// another. Nullable only so leads imported before ownership existed still
    /// load; those are treated as unowned and visible to super admins only.
    /// </para>
    /// </summary>
    public Guid? OwnerUserId { get; set; }

    /// <summary>
    /// Normalised website host, phone and name+city keys used for duplicate
    /// detection. Persisted (not computed on read) so they can be indexed —
    /// scanning 100k rows per insert would not scale.
    /// </summary>
    public string? DedupeWebsiteKey { get; set; }

    public string? DedupePhoneKey { get; set; }

    public string? DedupeNameKey { get; set; }

    /// <summary>
    /// Set once the lead has been emailed, so the UI can avoid contacting the
    /// same business twice and the audit trail has a quick reference.
    /// </summary>
    public DateTimeOffset? LastContactedAt { get; set; }

    public int TimesContacted { get; set; }

    /// <summary>
    /// Set when the caller opens a click-to-chat WhatsApp link for this lead —
    /// there is no delivery confirmation the way SMTP gives one, so this means
    /// "a send was initiated", not "the message arrived". Kept separate from
    /// <see cref="LastContactedAt"/> because the two channels are contacted
    /// independently: emailing a lead should not hide it from WhatsApp outreach
    /// or vice versa.
    /// </summary>
    public DateTimeOffset? LastWhatsAppContactedAt { get; set; }

    public int WhatsAppTimesContacted { get; set; }

    // --- Google Maps enrichment (Apify) --------------------------------------
    // Filled in after discovery, not by it: Google Places already supplies
    // Rating/ReviewCount/MapsUrl at discovery time, and this step refreshes
    // those plus the detail Places' Text Search doesn't return.

    public string PostalCode { get; set; } = string.Empty;

    /// <summary>Serialized <c>[{"day":"Monday","hours":"9 AM to 5 PM"}, ...]</c>.</summary>
    public string OpeningHoursJson { get; set; } = string.Empty;

    /// <summary>The Maps place id, as Apify's actor returns it (not Google's own Place ID format).</summary>
    public string PlaceId { get; set; } = string.Empty;

    /// <summary>Serialized array of image URLs. No blobs stored — links only.</summary>
    public string ImageUrlsJson { get; set; } = string.Empty;

    public bool? PermanentlyClosed { get; set; }

    public DateTimeOffset? LastMapsEnrichedAt { get; set; }

    // --- LinkedIn company enrichment (Apify) ---------------------------------

    public string Industry { get; set; } = string.Empty;

    public int? EmployeeCount { get; set; }

    /// <summary>LinkedIn's own company description, distinct from the user-authored <see cref="Notes"/>.</summary>
    public string CompanyDescription { get; set; } = string.Empty;

    public DateTimeOffset? LastLinkedInEnrichedAt { get; set; }

    /// <summary>
    /// Set once LinkedIn people search has run for this company. Distinct from
    /// <see cref="LastLinkedInEnrichedAt"/> (company-level detail, filled in
    /// before the lead is even saved) because people search runs in a separate
    /// phase afterward — against the business's real, saved id — and this is
    /// what stops it re-running for the same company if a run is resumed.
    /// </summary>
    public DateTimeOffset? LastPeopleSearchedAt { get; set; }

    /// <summary>Decision-makers found at this company via LinkedIn people search.</summary>
    public ICollection<Person> People { get; set; } = new List<Person>();
}
