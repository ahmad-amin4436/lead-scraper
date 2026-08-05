using System.ComponentModel.DataAnnotations;

namespace LeadMine.Application.DTOs;

/// <summary>
/// Searches one named company's LinkedIn People tab and saves every match as a
/// standalone <see cref="Domain.Entities.Person"/> — not tied to a pre-existing
/// saved lead, unlike <see cref="EnrichLeadsWithLinkedInRequest"/>. Stored
/// verbatim as a queued job's <c>RequestJson</c> and read back by
/// <c>LinkedInPeopleSearchRunner</c>.
/// </summary>
public sealed class LinkedInPeopleSearchRequest
{
    /// <summary>The company to search within — resolved to a LinkedIn company page by name.</summary>
    [Required, MaxLength(256)]
    public string CompanyName { get; set; } = string.Empty;

    /// <summary>Title/keyword filter, matching LinkedIn's own "Search employees by title, keyword or school" box on the People tab. Empty means every employee.</summary>
    [MaxLength(256)]
    public string? Keywords { get; set; }

    /// <summary>
    /// Best-effort location filter, matched against each result's own displayed
    /// location text client-side — LinkedIn's own location facets on this page
    /// use internal geo ids this app has no way to resolve without further
    /// per-location reconnaissance, so this is a plain substring match, not a
    /// structured filter.
    /// </summary>
    [MaxLength(128)]
    public string? Location { get; set; }

    [Range(1, 100)]
    public int MaxResults { get; set; } = 25;
}

/// <summary>One person found, appended to a people-search job's live-progress list.</summary>
public sealed class LinkedInPersonFoundOutcome
{
    public string FullName { get; set; } = string.Empty;
    public string JobTitle { get; set; } = string.Empty;
    public string LinkedInUrl { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public bool IsDecisionMaker { get; set; }
}
