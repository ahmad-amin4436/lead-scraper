using System.ComponentModel.DataAnnotations;

namespace LeadMine.Application.DTOs;

/// <summary>
/// Enriches a bounded batch of already-saved leads with LinkedIn data —
/// company detail plus a decision-maker search — outside of a search run.
/// Stored verbatim as a queued job's <c>RequestJson</c> and read back by
/// <c>LinkedInEnrichmentRunner</c>; the batch is still capped, now because a
/// very long-running lease is harder to reason about, not because of an HTTP
/// timeout.
/// </summary>
public sealed class EnrichLeadsWithLinkedInRequest
{
    [Required, MinLength(1), MaxLength(25)]
    public List<Guid> BusinessIds { get; set; } = new();

    [Range(1, 50)]
    public int MaxDecisionMakersPerCompany { get; set; } = 3;
}

/// <summary>One lead's result, appended to a job's live-progress list as the run proceeds.</summary>
public sealed class LinkedInEnrichmentOutcome
{
    public Guid BusinessId { get; set; }
    public string BusinessName { get; set; } = string.Empty;
    public bool CompanyEnriched { get; set; }
    public int PeopleFound { get; set; }
    public string? Error { get; set; }
}
