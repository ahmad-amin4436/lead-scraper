using System.Text.Json;
using System.Text.Json.Serialization;
using LeadMine.Infrastructure.Scraping.Providers;

namespace LeadMine.Infrastructure.Scraping.Enrichment;

/// <summary>
/// Finds individuals at a company via Apify's
/// <c>harvestapi/linkedin-company-employees</c> actor, and flags which of them
/// look like decision-makers from their title.
/// <para>
/// Needs the company's own LinkedIn URL as input — run
/// <see cref="ApifyLinkedInCompanyService"/> first in the same enrichment pass
/// so a business discovered today (with no LinkedIn URL yet) can still be
/// searched. A business with no LinkedIn URL at all — company enrichment found
/// nothing either — has nothing to search and returns no results.
/// </para>
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

public sealed class ApifyLinkedInPeopleService(ApifyClient apify)
{
    private const string DefaultActorId = "harvestapi~linkedin-company-employees";

    /// <summary>
    /// Title fragments that read as ownership/leadership. A substring match
    /// against the job title (and headline as a fallback), not an exhaustive
    /// enum — real-world titles vary too much for a fixed list to cover
    /// exactly, and a false positive here just means the record shows a
    /// decision-maker badge someone can ignore, not a wrong contact.
    /// </summary>
    private static readonly string[] DecisionMakerTitles =
    [
        "owner", "founder", "co-founder", "ceo", "chief executive", "president",
        "managing director", "managing partner", "director", "vice president",
        " vp ", "vp,", "vp.", "chief", "general manager", "gm,", "principal",
        "partner", "head of",
    ];

    public string? Readiness(ScraperOptions options) =>
        ApifyClient.Readiness(options, "LinkedIn people search (Apify)");

    public async Task<IReadOnlyList<LinkedInPersonResult>> SearchAsync(
        string companyLinkedInUrl,
        int maxResults,
        ProviderContext context,
        CancellationToken ct)
    {
        var notReady = Readiness(context.Options);
        if (notReady is not null) throw new ProviderException(notReady, ProviderFailure.MissingApiKey);
        if (string.IsNullOrWhiteSpace(companyLinkedInUrl)) return [];

        var actorId = string.IsNullOrWhiteSpace(context.Options.ApifyLinkedInPeopleActorId)
            ? DefaultActorId
            : context.Options.ApifyLinkedInPeopleActorId;

        var input = new Dictionary<string, object?>
        {
            ["companies"] = new[] { companyLinkedInUrl },
            // Filtering server-side to decision-maker-shaped titles keeps the
            // billed result count down to who this feature actually exists to
            // find, rather than every employee at the company.
            ["jobTitles"] = new[]
            {
                "Owner", "Founder", "Co-Founder", "CEO", "President", "Managing Director",
                "Managing Partner", "Director", "Vice President", "VP", "General Manager", "Partner",
            },
            // The actor validates this against its exact display string, not a
            // short code — confirmed live; "Full" alone is rejected as invalid input.
            ["profileScraperMode"] = "Full ($8 per 1k)",
            ["maxItems"] = Math.Clamp(maxResults, 1, 50),
        };

        var items = await apify.RunAsync(actorId, input, maxResults, context, ct);
        var results = new List<LinkedInPersonResult>(items.Count);

        foreach (var item in items)
        {
            var person = item.Deserialize<ApifyLinkedInPerson>(ApifyClient.JsonOptions);
            if (person is null) continue;

            var fullName = $"{person.FirstName} {person.LastName}".Trim();
            if (fullName.Length == 0) continue;

            var title = person.CurrentPosition?.Title ?? string.Empty;
            var (isDecisionMaker, role) = ClassifyDecisionMaker(title, person.Headline);

            var skills = person.Skills is { Count: > 0 } ? person.Skills : person.TopSkills;

            results.Add(new LinkedInPersonResult(
                FullName: fullName,
                JobTitle: title,
                Headline: person.Headline ?? string.Empty,
                LinkedInUrl: person.LinkedinUrl ?? string.Empty,
                Location: person.Location ?? string.Empty,
                ExperienceJson: person.Experience is { Count: > 0 }
                    ? JsonSerializer.Serialize(person.Experience, ApifyClient.JsonOptions)
                    : null,
                EducationJson: person.Education is { Count: > 0 }
                    ? JsonSerializer.Serialize(person.Education, ApifyClient.JsonOptions)
                    : null,
                SkillsJson: skills is { Count: > 0 } ? JsonSerializer.Serialize(skills, ApifyClient.JsonOptions) : null,
                IsDecisionMaker: isDecisionMaker,
                DecisionMakerRole: role));
        }

        return results;
    }

    private static (bool IsMatch, string Role) ClassifyDecisionMaker(string? title, string? headline)
    {
        var text = $" {title} {headline} ".ToLowerInvariant();

        foreach (var keyword in DecisionMakerTitles)
        {
            if (text.Contains(keyword, StringComparison.Ordinal))
            {
                return (true, !string.IsNullOrWhiteSpace(title) ? title : headline ?? keyword);
            }
        }

        return (false, string.Empty);
    }

    // --- wire formats -------------------------------------------------------

    private sealed class ApifyLinkedInPerson
    {
        [JsonPropertyName("firstName")] public string? FirstName { get; set; }
        [JsonPropertyName("lastName")] public string? LastName { get; set; }
        [JsonPropertyName("headline")] public string? Headline { get; set; }
        [JsonPropertyName("linkedinUrl")] public string? LinkedinUrl { get; set; }
        [JsonPropertyName("location")] public string? Location { get; set; }
        [JsonPropertyName("currentPosition")] public ApifyLinkedInPosition? CurrentPosition { get; set; }
        [JsonPropertyName("topSkills")] public List<string>? TopSkills { get; set; }
        [JsonPropertyName("skills")] public List<string>? Skills { get; set; }
        [JsonPropertyName("experience")] public List<ApifyLinkedInExperience>? Experience { get; set; }
        [JsonPropertyName("education")] public List<ApifyLinkedInEducation>? Education { get; set; }
    }

    private sealed class ApifyLinkedInPosition
    {
        [JsonPropertyName("companyName")] public string? CompanyName { get; set; }
        [JsonPropertyName("title")] public string? Title { get; set; }
    }

    private sealed class ApifyLinkedInExperience
    {
        [JsonPropertyName("company")] public string? Company { get; set; }
        [JsonPropertyName("title")] public string? Title { get; set; }
        [JsonPropertyName("dateRange")] public string? DateRange { get; set; }
        [JsonPropertyName("description")] public string? Description { get; set; }
    }

    private sealed class ApifyLinkedInEducation
    {
        [JsonPropertyName("school")] public string? School { get; set; }
        [JsonPropertyName("degree")] public string? Degree { get; set; }
        [JsonPropertyName("dateRange")] public string? DateRange { get; set; }
    }
}
