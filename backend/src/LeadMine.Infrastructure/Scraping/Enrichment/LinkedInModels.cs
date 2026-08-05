namespace LeadMine.Infrastructure.Scraping.Enrichment;

/// <summary>
/// LinkedIn company data — industry, employee count, headquarters, description,
/// the company's own LinkedIn URL — as returned by whichever LinkedIn company
/// data source is configured (<see cref="PlaywrightLinkedInCompanyService"/>).
/// </summary>
public sealed record LinkedInCompanyResult(
    string? Industry,
    int? EmployeeCount,
    string? Description,
    string? LinkedInUrl,
    string? Website,
    string? HeadquartersCity);

/// <summary>
/// One person found at a company via LinkedIn people search
/// (<see cref="PlaywrightLinkedInPeopleService"/>), with a heuristic flag for
/// whether their title reads as ownership/leadership.
/// </summary>
public sealed record LinkedInPersonResult(
    string FullName,
    string JobTitle,
    string Headline,
    string LinkedInUrl,
    string Location,
    string? ExperienceJson,
    string? EducationJson,
    string? SkillsJson,
    bool IsDecisionMaker,
    string DecisionMakerRole);
