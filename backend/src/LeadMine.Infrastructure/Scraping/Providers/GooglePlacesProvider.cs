using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using LeadMine.Application.DTOs;
using LeadMine.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace LeadMine.Infrastructure.Scraping.Providers;

/// <summary>
/// Google Places API (New) — Text Search, plus the Geocoding API for locations.
/// <para>
/// Richer than OpenStreetMap (ratings, review counts, phone numbers) but billed
/// per request and gated behind a key, so it is opt-in. The field mask is
/// explicit because Google prices responses by the fields requested: asking for
/// everything would multiply the cost of every run.
/// </para>
/// </summary>
public sealed class GooglePlacesProvider(
    IHttpClientFactory httpClientFactory,
    ILogger<GooglePlacesProvider> logger) : IPlaceProvider, IGeocoder
{
    private const string SearchTextUrl = "https://places.googleapis.com/v1/places:searchText";
    private const string GeocodeUrl = "https://maps.googleapis.com/maps/api/geocode/json";

    /// <summary>Google caps a Text Search page at 20 and paginates up to 3 pages.</summary>
    private const int PageSize = 20;
    private const int MaxPages = 3;

    private const string FieldMask =
        "places.id,places.displayName,places.formattedAddress,places.addressComponents," +
        "places.location,places.rating,places.userRatingCount,places.nationalPhoneNumber," +
        "places.internationalPhoneNumber,places.websiteUri,places.googleMapsUri," +
        "places.primaryTypeDisplayName,places.businessStatus,nextPageToken";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public BusinessSource Source => BusinessSource.GooglePlaces;

    public string Label => "Google Places";

    public string? Readiness(ProviderContext context) =>
        string.IsNullOrWhiteSpace(context.GoogleApiKey)
            ? "Google Places needs an API key. Add one in Settings or set Scraper:GoogleApiKey."
            : null;

    public async Task<IReadOnlyList<ProviderBusiness>> SearchAsync(
        ProviderQuery query,
        ProviderContext context,
        CancellationToken ct = default)
    {
        var notReady = Readiness(context);
        if (notReady is not null) throw new ProviderException(notReady, ProviderFailure.MissingApiKey);

        var client = httpClientFactory.CreateClient(ScraperHttpClients.Provider);
        var results = new List<ProviderBusiness>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        string? pageToken = null;

        for (var page = 0; page < MaxPages && results.Count < query.MaxResults; page++)
        {
            ct.ThrowIfCancellationRequested();
            await context.RateLimiter.WaitAsync(ct);

            var body = new Dictionary<string, object?>
            {
                ["textQuery"] = $"{query.CategoryLabel} in {query.Location.City}, {query.Location.Country}",
                ["maxResultCount"] = Math.Min(PageSize, query.MaxResults - results.Count),
                ["locationBias"] = new
                {
                    circle = new
                    {
                        center = new
                        {
                            latitude = query.Location.Latitude,
                            longitude = query.Location.Longitude,
                        },
                        radius = (double)Math.Clamp(query.RadiusMeters, 1, 50_000),
                    },
                },
            };

            if (pageToken is not null) body["pageToken"] = pageToken;

            // Google is fast; it must not inherit the Overpass-sized ceiling the
            // shared provider client carries.
            using var deadline = ScraperHttpClients.Deadline(context.Options.RequestTimeoutMs, ct);

            using var response = await ScraperRetry.ExecuteAsync(
                async _ =>
                {
                    using var request = new HttpRequestMessage(HttpMethod.Post, SearchTextUrl)
                    {
                        Content = JsonContent.Create(body, options: JsonOptions),
                    };

                    request.Headers.TryAddWithoutValidation("X-Goog-Api-Key", context.GoogleApiKey);
                    request.Headers.TryAddWithoutValidation("X-Goog-FieldMask", FieldMask);

                    var result = await client.SendAsync(request, deadline.Token);

                    // Surface retryable statuses as exceptions so the retry helper
                    // sees them; anything else is decided below.
                    if (ScraperRetry.IsRetryableStatus(result.StatusCode))
                    {
                        result.Dispose();
                        throw new HttpRequestException(
                            $"Google Places returned {(int)result.StatusCode}", null, result.StatusCode);
                    }

                    return result;
                },
                context.Options.RetryAttempts,
                logger,
                ct);

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                throw new ProviderException(
                    "Google Places rejected the API key. Check that the key is valid and the Places API (New) is enabled.",
                    ProviderFailure.InvalidApiKey);
            }

            var payload = await ReadPayloadAsync(response, ct);

            if (!response.IsSuccessStatusCode)
            {
                var message = payload?.Error?.Message ?? $"HTTP {(int)response.StatusCode}";
                throw new ProviderException($"Google Places error: {message}");
            }

            var places = payload?.Places ?? [];

            foreach (var place in places)
            {
                if (results.Count >= query.MaxResults) break;

                // `businessStatus` marks permanently closed venues; they're not leads.
                if (!string.IsNullOrEmpty(place.BusinessStatus) &&
                    !string.Equals(place.BusinessStatus, "OPERATIONAL", StringComparison.Ordinal))
                {
                    continue;
                }

                var externalId = place.Id ?? string.Empty;
                if (externalId.Length > 0 && !seen.Add(externalId)) continue;

                results.Add(ToBusiness(place, query));
            }

            pageToken = payload?.NextPageToken;
            if (string.IsNullOrEmpty(pageToken) || places.Count == 0) break;
        }

        return results;
    }

    private static async Task<SearchTextResponse?> ReadPayloadAsync(
        HttpResponseMessage response,
        CancellationToken ct)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<SearchTextResponse>(JsonOptions, ct);
        }
        catch (JsonException)
        {
            // An error page instead of JSON: the status code is the real signal.
            return null;
        }
    }

    private static ProviderBusiness ToBusiness(GooglePlace place, ProviderQuery query)
    {
        var components = place.AddressComponents;

        var city = Component(components, "locality")
            ?? Component(components, "postal_town")
            ?? Component(components, "administrative_area_level_2")
            ?? query.Location.City;

        var name = place.DisplayName?.Text?.Trim() ?? string.Empty;
        var address = place.FormattedAddress?.Trim() ?? string.Empty;

        return new ProviderBusiness
        {
            ExternalId = place.Id ?? string.Empty,
            Name = name,
            Category = query.CategoryLabel,
            Address = address,
            Country = Component(components, "country", useShort: true) ?? query.Location.Country,
            State = Component(components, "administrative_area_level_1") ?? query.Location.State,
            City = city,
            Phone = (place.InternationalPhoneNumber ?? place.NationalPhoneNumber ?? string.Empty).Trim(),
            Website = ProviderHelpers.NormalizeUrl(place.WebsiteUri),
            Latitude = place.Location?.Latitude,
            Longitude = place.Location?.Longitude,
            Rating = place.Rating,
            ReviewCount = place.UserRatingCount,
            MapsUrl = string.IsNullOrWhiteSpace(place.GoogleMapsUri)
                ? ProviderHelpers.BuildMapsSearchUrl(name, address)
                : place.GoogleMapsUri,
            Source = BusinessSource.GooglePlaces,
        };
    }

    /// <summary>Null when absent, so the caller's <c>??</c> fallback chain works.</summary>
    private static string? Component(
        List<GoogleAddressComponent>? components,
        string type,
        bool useShort = false)
    {
        var match = components?.FirstOrDefault(c => c.Types?.Contains(type) == true);
        if (match is null) return null;

        var value = useShort ? match.ShortText ?? match.LongText : match.LongText;
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    public async Task<ResolvedLocation?> ResolveAsync(
        string country,
        string? state,
        string city,
        ProviderContext context,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(context.GoogleApiKey)) return null;

        var client = httpClientFactory.CreateClient(ScraperHttpClients.Provider);
        var address = string.Join(", ", new[] { city, state, country }.Where(s => !string.IsNullOrWhiteSpace(s)));

        await context.RateLimiter.WaitAsync(ct);

        var url = $"{GeocodeUrl}?address={Uri.EscapeDataString(address)}" +
                  $"&key={Uri.EscapeDataString(context.GoogleApiKey)}";

        using var deadline = ScraperHttpClients.Deadline(context.Options.RequestTimeoutMs, ct);

        using var response = await ScraperRetry.ExecuteAsync(
            async _ => await client.GetAsync(url, deadline.Token),
            context.Options.RetryAttempts,
            logger,
            ct);

        if (!response.IsSuccessStatusCode) return null;

        var payload = await response.Content.ReadFromJsonAsync<GeocodeResponse>(JsonOptions, deadline.Token);

        // The Geocoding API answers HTTP 200 with a `status` field, so a
        // configuration error (the API not enabled on the project, a key
        // restricted to the wrong referrer) looks like a successful request that
        // simply found nothing. Returning null would silently push every lookup
        // onto Nominatim's 1-request-per-second budget with no explanation.
        if (payload?.Status is { } status && status is not ("OK" or "ZERO_RESULTS"))
        {
            logger.LogWarning(
                "Google Geocoding returned {Status} for \"{Address}\" ({Message}). Falling back to Nominatim, which is slower and rate-limited.",
                status, address, payload.ErrorMessage ?? "no detail");

            return null;
        }

        var first = payload?.Results?.FirstOrDefault();
        var location = first?.Geometry?.Location;

        if (first is null || location?.Lat is not { } lat || location.Lng is not { } lng) return null;

        var components = first.AddressComponents;

        return new ResolvedLocation(
            first.FormattedAddress ?? address,
            GeoComponent(components, "country", useShort: true) ?? country,
            GeoComponent(components, "administrative_area_level_1") ?? state ?? string.Empty,
            GeoComponent(components, "locality") ?? GeoComponent(components, "postal_town") ?? city,
            lat,
            lng);
    }

    private static string? GeoComponent(
        List<GeocodeAddressComponent>? components,
        string type,
        bool useShort = false)
    {
        var match = components?.FirstOrDefault(c => c.Types?.Contains(type) == true);
        if (match is null) return null;

        var value = useShort ? match.ShortName ?? match.LongName : match.LongName;
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    // --- wire formats -------------------------------------------------------

    private sealed class SearchTextResponse
    {
        [JsonPropertyName("places")] public List<GooglePlace>? Places { get; set; }
        [JsonPropertyName("nextPageToken")] public string? NextPageToken { get; set; }
        [JsonPropertyName("error")] public GoogleError? Error { get; set; }
    }

    private sealed class GoogleError
    {
        [JsonPropertyName("message")] public string? Message { get; set; }
        [JsonPropertyName("status")] public string? Status { get; set; }
    }

    private sealed class GooglePlace
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("displayName")] public GoogleText? DisplayName { get; set; }
        [JsonPropertyName("formattedAddress")] public string? FormattedAddress { get; set; }
        [JsonPropertyName("addressComponents")] public List<GoogleAddressComponent>? AddressComponents { get; set; }
        [JsonPropertyName("location")] public GoogleLatLng? Location { get; set; }
        [JsonPropertyName("rating")] public double? Rating { get; set; }
        [JsonPropertyName("userRatingCount")] public int? UserRatingCount { get; set; }
        [JsonPropertyName("nationalPhoneNumber")] public string? NationalPhoneNumber { get; set; }
        [JsonPropertyName("internationalPhoneNumber")] public string? InternationalPhoneNumber { get; set; }
        [JsonPropertyName("websiteUri")] public string? WebsiteUri { get; set; }
        [JsonPropertyName("googleMapsUri")] public string? GoogleMapsUri { get; set; }
        [JsonPropertyName("primaryTypeDisplayName")] public GoogleText? PrimaryTypeDisplayName { get; set; }
        [JsonPropertyName("businessStatus")] public string? BusinessStatus { get; set; }
    }

    private sealed class GoogleText
    {
        [JsonPropertyName("text")] public string? Text { get; set; }
    }

    private sealed class GoogleLatLng
    {
        [JsonPropertyName("latitude")] public double? Latitude { get; set; }
        [JsonPropertyName("longitude")] public double? Longitude { get; set; }
    }

    private sealed class GoogleAddressComponent
    {
        [JsonPropertyName("longText")] public string? LongText { get; set; }
        [JsonPropertyName("shortText")] public string? ShortText { get; set; }
        [JsonPropertyName("types")] public List<string>? Types { get; set; }
    }

    private sealed class GeocodeResponse
    {
        [JsonPropertyName("status")] public string? Status { get; set; }
        [JsonPropertyName("error_message")] public string? ErrorMessage { get; set; }
        [JsonPropertyName("results")] public List<GeocodeResult>? Results { get; set; }
    }

    private sealed class GeocodeResult
    {
        [JsonPropertyName("formatted_address")] public string? FormattedAddress { get; set; }
        [JsonPropertyName("geometry")] public GeocodeGeometry? Geometry { get; set; }
        [JsonPropertyName("address_components")] public List<GeocodeAddressComponent>? AddressComponents { get; set; }
    }

    private sealed class GeocodeGeometry
    {
        [JsonPropertyName("location")] public GeocodeLatLng? Location { get; set; }
    }

    private sealed class GeocodeLatLng
    {
        [JsonPropertyName("lat")] public double? Lat { get; set; }
        [JsonPropertyName("lng")] public double? Lng { get; set; }
    }

    private sealed class GeocodeAddressComponent
    {
        [JsonPropertyName("long_name")] public string? LongName { get; set; }
        [JsonPropertyName("short_name")] public string? ShortName { get; set; }
        [JsonPropertyName("types")] public List<string>? Types { get; set; }
    }
}
