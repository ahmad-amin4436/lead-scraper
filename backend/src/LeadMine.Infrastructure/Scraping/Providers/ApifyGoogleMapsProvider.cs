using System.Text.Json;
using System.Text.Json.Serialization;
using LeadMine.Application.DTOs;
using LeadMine.Domain.Enums;

namespace LeadMine.Infrastructure.Scraping.Providers;

/// <summary>
/// Google Maps, scraped by an Apify actor rather than called directly.
/// <para>
/// Google Maps itself is a JS-rendered app with no public search API and active
/// bot defenses — scraping it directly would need a headless browser this host
/// cannot run, and would violate Google's terms regardless of where that
/// browser ran. Apify is a legitimate, paid third-party platform whose declared
/// product is exactly this: it operates its own scraping infrastructure and
/// actors (e.g. the Google Maps Scraper) as a service, so the detection-evasion
/// and maintenance burden is Apify's problem, not this codebase's.
/// </para>
/// <para>
/// Deliberately does not enable the actor's own <c>scrapeContacts</c> option
/// (which crawls each business's website for email/social links) — that is a
/// separate billed event per Apify's pricing, and this app already has a
/// robots.txt-respecting crawler (<see cref="Enrichment.EnrichmentService"/>)
/// that does the same job for free as part of the normal pipeline every other
/// provider's results already go through.
/// </para>
/// </summary>
public sealed class ApifyGoogleMapsProvider(ApifyClient apify) : IPlaceProvider
{
    private const string DefaultActorId = "compass~crawler-google-places";

    public BusinessSource Source => BusinessSource.Apify;

    public string Label => "Google Maps (Apify)";

    public string? Readiness(ProviderContext context) => ApifyClient.Readiness(context.Options, Label);

    public async Task<IReadOnlyList<ProviderBusiness>> SearchAsync(
        ProviderQuery query,
        ProviderContext context,
        CancellationToken ct = default)
    {
        var notReady = Readiness(context);
        if (notReady is not null) throw new ProviderException(notReady, ProviderFailure.MissingApiKey);

        var actorId = string.IsNullOrWhiteSpace(context.Options.ApifyActorId)
            ? DefaultActorId
            : context.Options.ApifyActorId;

        var input = new Dictionary<string, object?>
        {
            ["searchStringsArray"] = new[] { query.CategoryLabel },
            ["locationQuery"] = string.Join(
                ", ",
                new[] { query.Location.City, query.Location.State, query.Location.Country }
                    .Where(s => !string.IsNullOrWhiteSpace(s))),
            ["maxCrawledPlacesPerSearch"] = Math.Clamp(query.MaxResults, 1, 500),
            ["language"] = "en",
            // Detail pages and contact scraping are each separately billed
            // Apify events; the base search result already has everything this
            // app's ProviderBusiness shape uses, and enrichment covers contacts.
            ["scrapePlaceDetailPage"] = false,
            ["scrapeContacts"] = false,
        };

        var items = await apify.RunAsync(actorId, input, query.MaxResults, context, ct);

        var results = new List<ProviderBusiness>(items.Count);

        foreach (var item in items)
        {
            if (results.Count >= query.MaxResults) break;

            var place = item.Deserialize<ApifyPlace>(ApifyClient.JsonOptions);
            if (place is null) continue;

            // A closed listing is not a lead, same rule Google Places applies.
            if (place.PermanentlyClosed == true || place.TemporarilyClosed == true) continue;

            results.Add(ToBusiness(place, query));
        }

        return results;
    }

    private static ProviderBusiness ToBusiness(ApifyPlace place, ProviderQuery query)
    {
        var name = (place.Title ?? string.Empty).Trim();
        var address = (place.Address ?? string.Empty).Trim();

        return new ProviderBusiness
        {
            ExternalId = place.PlaceId ?? string.Empty,
            Name = name,
            Category = query.CategoryLabel,
            Address = address,
            Country = Fallback(place.CountryCode, query.Location.Country),
            State = Fallback(place.State, query.Location.State),
            City = Fallback(place.City, query.Location.City),
            Phone = (place.PhoneUnformatted ?? place.Phone ?? string.Empty).Trim(),
            Website = ProviderHelpers.NormalizeUrl(place.Website),
            Latitude = place.Location?.Lat,
            Longitude = place.Location?.Lng,
            Rating = place.TotalScore,
            ReviewCount = place.ReviewsCount,
            MapsUrl = string.IsNullOrWhiteSpace(place.Url)
                ? ProviderHelpers.BuildMapsSearchUrl(name, address)
                : place.Url,
            Source = BusinessSource.Apify,
        };
    }

    private static string Fallback(string? value, string alternative) =>
        string.IsNullOrWhiteSpace(value) ? alternative : value.Trim();
}

/// <summary>
/// One place from <c>compass/crawler-google-places</c>'s dataset output.
/// Shared between discovery (<see cref="ApifyGoogleMapsProvider"/>) and
/// enrichment (<see cref="ApifyMapsEnrichmentService"/>) — the same actor, so
/// the same response shape.
/// </summary>
internal sealed class ApifyPlace
{
    [JsonPropertyName("title")] public string? Title { get; set; }
    [JsonPropertyName("categoryName")] public string? CategoryName { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("address")] public string? Address { get; set; }
    [JsonPropertyName("city")] public string? City { get; set; }
    [JsonPropertyName("state")] public string? State { get; set; }
    [JsonPropertyName("postalCode")] public string? PostalCode { get; set; }
    [JsonPropertyName("countryCode")] public string? CountryCode { get; set; }
    [JsonPropertyName("phone")] public string? Phone { get; set; }
    [JsonPropertyName("phoneUnformatted")] public string? PhoneUnformatted { get; set; }
    [JsonPropertyName("website")] public string? Website { get; set; }
    [JsonPropertyName("url")] public string? Url { get; set; }
    [JsonPropertyName("placeId")] public string? PlaceId { get; set; }
    [JsonPropertyName("location")] public ApifyLatLng? Location { get; set; }
    [JsonPropertyName("totalScore")] public double? TotalScore { get; set; }
    [JsonPropertyName("reviewsCount")] public int? ReviewsCount { get; set; }
    [JsonPropertyName("permanentlyClosed")] public bool? PermanentlyClosed { get; set; }
    [JsonPropertyName("temporarilyClosed")] public bool? TemporarilyClosed { get; set; }
    [JsonPropertyName("openingHours")] public List<ApifyOpeningHours>? OpeningHours { get; set; }
    [JsonPropertyName("imageUrls")] public List<string>? ImageUrls { get; set; }
}

internal sealed class ApifyLatLng
{
    [JsonPropertyName("lat")] public double? Lat { get; set; }
    [JsonPropertyName("lng")] public double? Lng { get; set; }
}

internal sealed class ApifyOpeningHours
{
    [JsonPropertyName("day")] public string? Day { get; set; }
    [JsonPropertyName("hours")] public string? Hours { get; set; }
}
