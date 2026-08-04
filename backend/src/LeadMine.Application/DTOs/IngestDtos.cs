using System.ComponentModel.DataAnnotations;
using LeadMine.Application.Validation;
using LeadMine.Domain.Enums;

namespace LeadMine.Application.DTOs;

/// <summary>
/// A batch of scraped leads handed over by the scraper worker.
/// <para>
/// The worker runs outside any user session, so it authenticates with a service
/// key and states the owner explicitly. That is why this is a separate endpoint
/// from the user-facing create: it is the one place a caller may set
/// <see cref="OwnerUserId"/>, and it is reachable only with the service key.
/// </para>
/// </summary>
public sealed class IngestLeadsRequest
{
    /// <summary>The user whose pipeline these leads belong to.</summary>
    [Required]
    public Guid OwnerUserId { get; set; }

    /// <summary>The run that produced them, for traceability.</summary>
    public Guid? SearchJobId { get; set; }

    /// <summary>Drop leads that already exist for this owner.</summary>
    public bool SkipDuplicates { get; set; } = true;

    [Required, MinLength(1)]
    public List<IngestLead> Leads { get; set; } = new();
}

public sealed class IngestLead
{
    [Required, MaxLength(256)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(128)] public string Category { get; set; } = string.Empty;
    [MaxLength(128)] public string Country { get; set; } = string.Empty;
    [MaxLength(128)] public string State { get; set; } = string.Empty;
    [MaxLength(128)] public string City { get; set; } = string.Empty;
    [MaxLength(512)] public string Address { get; set; } = string.Empty;
    [MaxLength(64)] public string Phone { get; set; } = string.Empty;
    [MaxLength(512)] public string Website { get; set; } = string.Empty;

    [OptionalEmailAddress, MaxLength(256)]
    public string Email { get; set; } = string.Empty;

    public EmailStatus EmailStatus { get; set; } = EmailStatus.Unverified;

    [MaxLength(512)] public string WhatsApp { get; set; } = string.Empty;

    public WhatsAppStatus WhatsAppStatus { get; set; } = WhatsAppStatus.Unverified;

    [MaxLength(512)] public string Facebook { get; set; } = string.Empty;
    [MaxLength(512)] public string Instagram { get; set; } = string.Empty;
    [MaxLength(512)] public string LinkedIn { get; set; } = string.Empty;

    public double? Latitude { get; set; }
    public double? Longitude { get; set; }

    [Range(0, 5)] public double? Rating { get; set; }
    [Range(0, int.MaxValue)] public int? ReviewCount { get; set; }

    [MaxLength(1024)] public string MapsUrl { get; set; } = string.Empty;
    [MaxLength(2000)] public string Notes { get; set; } = string.Empty;

    public BusinessSource Source { get; set; } = BusinessSource.Manual;
    public BusinessStatus Status { get; set; } = BusinessStatus.New;

    // --- Google Maps enrichment (Apify), populated only when requested ------

    [MaxLength(32)] public string PostalCode { get; set; } = string.Empty;
    public string OpeningHoursJson { get; set; } = string.Empty;
    [MaxLength(256)] public string PlaceId { get; set; } = string.Empty;
    public string ImageUrlsJson { get; set; } = string.Empty;
    public bool? PermanentlyClosed { get; set; }

    /// <summary>
    /// Set by the worker when the Maps enrichment step was attempted for this
    /// lead, regardless of whether it found anything — distinguishes "ran and
    /// found nothing" from "never ran".
    /// </summary>
    public DateTimeOffset? MapsEnrichedAt { get; set; }

    // --- LinkedIn company enrichment (Apify), populated only when requested -

    [MaxLength(256)] public string Industry { get; set; } = string.Empty;
    public int? EmployeeCount { get; set; }
    public string CompanyDescription { get; set; } = string.Empty;

    /// <summary>Same "attempted, not just found" meaning as <see cref="MapsEnrichedAt"/>.</summary>
    public DateTimeOffset? LinkedInEnrichedAt { get; set; }
}

public sealed class IngestResultDto
{
    public int Received { get; set; }
    public int Saved { get; set; }
    public int Duplicates { get; set; }
    public int Rejected { get; set; }
}
