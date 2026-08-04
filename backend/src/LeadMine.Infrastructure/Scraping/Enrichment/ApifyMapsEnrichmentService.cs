using System.Text.Json;
using LeadMine.Domain.Entities;
using LeadMine.Infrastructure.Scraping.Providers;

namespace LeadMine.Infrastructure.Scraping.Enrichment;

/// <summary>
/// Fills in the Google Maps detail Google Places' Text Search does not return
/// (opening hours, postal code, images, a fresher rating/review count) for a
/// business already discovered — via Apify, the same actor
/// <see cref="ApifyGoogleMapsProvider"/> uses for discovery, just pointed at
/// one specific listing instead of running a fresh search.
/// </summary>
public sealed record MapsEnrichmentResult(
    string? PostalCode,
    string? OpeningHoursJson,
    string? PlaceId,
    string? ImageUrlsJson,
    bool? PermanentlyClosed,
    double? Rating,
    int? ReviewCount,
    string? Description);

public sealed class ApifyMapsEnrichmentService(ApifyClient apify)
{
    private const string DefaultActorId = "compass~crawler-google-places";

    public string? Readiness(ScraperOptions options) =>
        ApifyClient.Readiness(options, "Google Maps enrichment (Apify)");

    /// <summary>
    /// Null when the actor found nothing to match. Takes plain fields rather
    /// than a <see cref="Business"/> entity because the caller
    /// (<c>SearchRunner</c>) runs this before a discovered lead has been saved
    /// — there is no tracked entity yet, just an in-memory candidate.
    /// </summary>
    public async Task<MapsEnrichmentResult?> EnrichAsync(
        string name,
        string city,
        string state,
        string country,
        string mapsUrl,
        ProviderContext context,
        CancellationToken ct)
    {
        var notReady = Readiness(context.Options);
        if (notReady is not null) throw new ProviderException(notReady, ProviderFailure.MissingApiKey);

        var actorId = string.IsNullOrWhiteSpace(context.Options.ApifyActorId)
            ? DefaultActorId
            : context.Options.ApifyActorId;

        var input = new Dictionary<string, object?>
        {
            ["language"] = "en",
            ["maxCrawledPlacesPerSearch"] = 1,
            // The whole point here is the detail Text Search omits.
            ["scrapePlaceDetailPage"] = true,
            ["scrapeContacts"] = false,
        };

        // A direct link to the exact listing beats re-searching by name: no
        // fuzzy matching, and it is the URL Google Places already gave us.
        if (!string.IsNullOrWhiteSpace(mapsUrl))
        {
            input["startUrls"] = new[] { new { url = mapsUrl } };
        }
        else
        {
            input["searchStringsArray"] = new[] { name };
            input["locationQuery"] = string.Join(
                ", ", new[] { city, state, country }.Where(s => !string.IsNullOrWhiteSpace(s)));
        }

        var items = await apify.RunAsync(actorId, input, 1, context, ct);
        if (items.Count == 0) return null;

        var place = items[0].Deserialize<ApifyPlace>(ApifyClient.JsonOptions);
        if (place is null) return null;

        return new MapsEnrichmentResult(
            PostalCode: string.IsNullOrWhiteSpace(place.PostalCode) ? null : place.PostalCode,
            OpeningHoursJson: place.OpeningHours is { Count: > 0 }
                ? JsonSerializer.Serialize(place.OpeningHours, ApifyClient.JsonOptions)
                : null,
            PlaceId: string.IsNullOrWhiteSpace(place.PlaceId) ? null : place.PlaceId,
            ImageUrlsJson: place.ImageUrls is { Count: > 0 }
                ? JsonSerializer.Serialize(place.ImageUrls, ApifyClient.JsonOptions)
                : null,
            PermanentlyClosed: place.PermanentlyClosed,
            Rating: place.TotalScore,
            ReviewCount: place.ReviewsCount,
            Description: string.IsNullOrWhiteSpace(place.Description) ? null : place.Description);
    }
}
