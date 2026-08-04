using System.Text.Json;
using System.Text.Json.Serialization;
using LeadMine.Domain.Entities;
using LeadMine.Infrastructure.Scraping.Providers;

namespace LeadMine.Infrastructure.Scraping.Enrichment;

/// <summary>
/// Enriches a business with publicly available LinkedIn company data — industry,
/// employee count, headquarters, description, the company's own LinkedIn URL —
/// via Apify's <c>harvestapi/linkedin-company</c> actor.
/// </summary>
public sealed record LinkedInCompanyResult(
    string? Industry,
    int? EmployeeCount,
    string? Description,
    string? LinkedInUrl,
    string? Website,
    string? HeadquartersCity);

public sealed class ApifyLinkedInCompanyService(ApifyClient apify)
{
    private const string DefaultActorId = "harvestapi~linkedin-company";

    public string? Readiness(ScraperOptions options) =>
        ApifyClient.Readiness(options, "LinkedIn company enrichment (Apify)");

    /// <summary>
    /// Null when nothing on LinkedIn matched. Takes plain fields rather than a
    /// <see cref="Business"/> entity because the caller (<c>SearchRunner</c>)
    /// runs this before a discovered lead has been saved.
    /// </summary>
    public async Task<LinkedInCompanyResult?> EnrichAsync(
        string name,
        string? existingLinkedInUrl,
        ProviderContext context,
        CancellationToken ct)
    {
        var notReady = Readiness(context.Options);
        if (notReady is not null) throw new ProviderException(notReady, ProviderFailure.MissingApiKey);

        var actorId = string.IsNullOrWhiteSpace(context.Options.ApifyLinkedInCompanyActorId)
            ? DefaultActorId
            : context.Options.ApifyLinkedInCompanyActorId;

        var input = new Dictionary<string, object?>();

        // A lead's LinkedIn field may already hold a company page URL (found
        // by the website crawler) — a direct URL beats a name search.
        if (!string.IsNullOrWhiteSpace(existingLinkedInUrl) &&
            existingLinkedInUrl.Contains("linkedin.com/company", StringComparison.OrdinalIgnoreCase))
        {
            input["companies"] = new[] { existingLinkedInUrl };
        }
        else
        {
            input["searches"] = new[] { name };
        }

        var items = await apify.RunAsync(actorId, input, 1, context, ct);
        if (items.Count == 0) return null;

        var company = items[0].Deserialize<ApifyLinkedInCompany>(ApifyClient.JsonOptions);
        if (company is null) return null;

        var headquarters = company.Locations?.FirstOrDefault(l => l.Headquarter == true) ?? company.Locations?.FirstOrDefault();

        return new LinkedInCompanyResult(
            Industry: company.Industries?.FirstOrDefault()?.Name,
            EmployeeCount: company.EmployeeCount,
            Description: string.IsNullOrWhiteSpace(company.Description) ? company.Tagline : company.Description,
            LinkedInUrl: company.LinkedinUrl,
            Website: company.Website,
            HeadquartersCity: headquarters?.City);
    }

    // --- wire formats -------------------------------------------------------

    private sealed class ApifyLinkedInCompany
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("linkedinUrl")] public string? LinkedinUrl { get; set; }
        [JsonPropertyName("tagline")] public string? Tagline { get; set; }
        [JsonPropertyName("description")] public string? Description { get; set; }
        [JsonPropertyName("website")] public string? Website { get; set; }
        [JsonPropertyName("employeeCount")] public int? EmployeeCount { get; set; }
        [JsonPropertyName("industries")] public List<ApifyLinkedInIndustry>? Industries { get; set; }
        [JsonPropertyName("locations")] public List<ApifyLinkedInLocation>? Locations { get; set; }
    }

    private sealed class ApifyLinkedInIndustry
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
    }

    private sealed class ApifyLinkedInLocation
    {
        [JsonPropertyName("city")] public string? City { get; set; }
        [JsonPropertyName("headquarter")] public bool? Headquarter { get; set; }
    }
}
