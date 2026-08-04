using System.ComponentModel.DataAnnotations;

namespace LeadMine.Application.DTOs;

/// <summary>
/// Enriches a bounded batch of already-saved leads with LinkedIn data —
/// company detail plus a decision-maker search — outside of a search run.
/// Runs synchronously (with bounded concurrency) rather than as a queued job:
/// the batch size is capped specifically so this stays within a normal HTTP
/// request's timeout, since each lead is one or two slow Apify actor runs.
/// </summary>
public sealed class EnrichLeadsWithLinkedInRequest
{
    [Required, MinLength(1), MaxLength(25)]
    public List<Guid> BusinessIds { get; set; } = new();

    [Range(1, 50)]
    public int MaxDecisionMakersPerCompany { get; set; } = 3;
}

public sealed class LinkedInEnrichmentOutcome
{
    public Guid BusinessId { get; set; }
    public string BusinessName { get; set; } = string.Empty;
    public bool CompanyEnriched { get; set; }
    public int PeopleFound { get; set; }
    public string? Error { get; set; }
}

public sealed class EnrichLeadsWithLinkedInResultDto
{
    public int Requested { get; set; }
    public int Enriched { get; set; }
    public int PeopleFound { get; set; }
    public int Failed { get; set; }
    public List<LinkedInEnrichmentOutcome> Outcomes { get; set; } = new();
}
