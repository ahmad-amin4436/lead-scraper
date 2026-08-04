using System.ComponentModel.DataAnnotations;

namespace LeadMine.Application.DTOs;

public sealed class PersonDto
{
    public Guid Id { get; set; }
    public Guid? BusinessId { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string JobTitle { get; set; } = string.Empty;
    public string Headline { get; set; } = string.Empty;
    public string LinkedInUrl { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;

    /// <summary>Raw JSON — the frontend already speaks JSON, no need to round-trip through typed C#.</summary>
    public string ExperienceJson { get; set; } = string.Empty;
    public string EducationJson { get; set; } = string.Empty;
    public string SkillsJson { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public bool IsDecisionMaker { get; set; }
    public string DecisionMakerRole { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class UpdatePersonRequest
{
    [MaxLength(256)] public string? FullName { get; set; }
    [MaxLength(256)] public string? JobTitle { get; set; }
    [MaxLength(256)] public string? Email { get; set; }
    [MaxLength(64)] public string? Phone { get; set; }
    public bool? IsDecisionMaker { get; set; }
}

/// <summary>Filters for the people list. Bound from the query string.</summary>
public sealed class PersonQueryRequest
{
    public string? Search { get; set; }
    public Guid? BusinessId { get; set; }
    public bool? IsDecisionMaker { get; set; }

    /// <summary>Admin-only, mirrors <see cref="BusinessQueryRequest.OwnerUserId"/>.</summary>
    public Guid? OwnerUserId { get; set; }

    [Range(1, int.MaxValue)] public int Page { get; set; } = 1;
    [Range(1, 500)] public int PageSize { get; set; } = 25;
}
