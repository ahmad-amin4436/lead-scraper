using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using LeadMine.Application.DTOs;
using LeadMine.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace LeadMine.Infrastructure.Scraping.Providers;

/// <summary>
/// OpenStreetMap via Overpass, with Nominatim for geocoding.
/// <para>
/// The default source: it needs no API key, so the app is useful before anyone
/// configures anything. It carries no ratings or review counts, which is why
/// those filters are documented as Google-only.
/// </para>
/// </summary>
public sealed class OpenStreetMapProvider(
    IHttpClientFactory httpClientFactory,
    ILogger<OpenStreetMapProvider> logger) : IPlaceProvider, IGeocoder
{
    /// <summary>
    /// Community mirrors, tried in order.
    /// <para>
    /// This list <em>is</em> the retry strategy, which is why each endpoint gets
    /// a single attempt below. An Overpass query carries a 60-second server-side
    /// budget, so retrying one three times can burn three minutes before trying
    /// the mirror that would have answered in two seconds.
    /// </para>
    /// </summary>
    private static readonly string[] OverpassEndpoints =
    [
        "https://overpass-api.de/api/interpreter",
        "https://overpass.kumi.systems/api/interpreter",
    ];

    private const string NominatimUrl = "https://nominatim.openstreetmap.org/search";

    /// <summary>
    /// Nominatim's usage policy allows one request per second regardless of our
    /// own rate limit, so it gets its own process-wide gate.
    /// </summary>
    private static readonly RateLimiter NominatimLimiter = new(55);

    public BusinessSource Source => BusinessSource.OpenStreetMap;

    public string Label => "OpenStreetMap";

    public string? Readiness(ProviderContext context) => null;

    public async Task<IReadOnlyList<ProviderBusiness>> SearchAsync(
        ProviderQuery query,
        ProviderContext context,
        CancellationToken ct = default)
    {
        var category = CategoryCatalog.Resolve(query.CategoryId);

        if (category.OsmFilters.Length == 0)
        {
            // Reported rather than returned as "no results", because the two mean
            // very different things to whoever reads the run's log.
            throw new ProviderException(
                $"No OpenStreetMap mapping for category \"{query.CategoryId}\". Use Google Places for this category.",
                ProviderFailure.UnsupportedCategory);
        }

        var overpassQuery = BuildOverpassQuery(category.OsmFilters, query);
        var client = httpClientFactory.CreateClient(ScraperHttpClients.Provider);

        Exception? lastError = null;

        foreach (var endpoint in OverpassEndpoints)
        {
            ct.ThrowIfCancellationRequested();
            await context.RateLimiter.WaitAsync(ct);

            try
            {
                using var content = new FormUrlEncodedContent(
                    new Dictionary<string, string> { ["data"] = overpassQuery });

                using (var response = await client.PostAsync(endpoint, content, ct))
                {
                    // Any failure is worth trying the next mirror for, not
                    // failing the task on — they differ mostly in how loaded
                    // they are at that moment.
                    if (!response.IsSuccessStatusCode)
                    {
                        lastError = new HttpRequestException($"Overpass returned {(int)response.StatusCode}");
                        continue;
                    }

                    var payload = await response.Content.ReadFromJsonAsync<OverpassResponse>(cancellationToken: ct);
                    return MapElements(payload?.Elements ?? [], query);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastError = ex;
                logger.LogDebug(ex, "Overpass endpoint {Endpoint} failed", endpoint);
            }
        }

        throw new ProviderException(
            $"OpenStreetMap search failed: {lastError?.Message}. Community Overpass servers throttle heavy use — retry shortly or switch to Google Places.",
            ProviderFailure.Transient,
            lastError);
    }

    private static string BuildOverpassQuery(string[] filters, ProviderQuery query)
    {
        var radius = Math.Clamp(query.RadiusMeters, 1, 50_000);
        var lat = query.Location.Latitude.ToString("F6", CultureInfo.InvariantCulture);
        var lon = query.Location.Longitude.ToString("F6", CultureInfo.InvariantCulture);

        // `nwr` matches nodes, ways and relations in one pass.
        var clauses = string.Join("\n", filters.Select(filter =>
        {
            var parts = filter.Split('=', 2);
            return $"  nwr[\"{parts[0]}\"=\"{parts[1]}\"](around:{radius},{lat},{lon});";
        }));

        // Over-fetch, because unnamed POIs are discarded below.
        var limit = Math.Clamp(query.MaxResults * 3, 50, 500);

        // The server-side budget deliberately sits below the HTTP client's, so a
        // slow mirror returns an Overpass error we can read rather than being
        // cut off mid-answer.
        return $"[out:json][timeout:{ScraperHttpClients.OverpassQuerySeconds}];\n(\n{clauses}\n);\nout center tags {limit};";
    }

    private List<ProviderBusiness> MapElements(
        IReadOnlyList<OverpassElement> elements,
        ProviderQuery query)
    {
        var results = new List<ProviderBusiness>();

        foreach (var element in elements)
        {
            if (results.Count >= query.MaxResults) break;

            var tags = element.Tags ?? new Dictionary<string, string>();
            var name = Tag(tags, "name", "brand", "operator");

            // An unnamed POI is not a usable lead.
            if (string.IsNullOrWhiteSpace(name)) continue;

            var address = BuildAddress(tags);

            results.Add(new ProviderBusiness
            {
                ExternalId = $"{element.Type}/{element.Id}",
                Name = name,
                Category = query.CategoryLabel,
                Address = string.IsNullOrWhiteSpace(address) ? query.Location.Label : address,
                Country = Tag(tags, "addr:country") is { Length: > 0 } c ? c : query.Location.Country,
                State = Tag(tags, "addr:state", "addr:province") is { Length: > 0 } s ? s : query.Location.State,
                City = Tag(tags, "addr:city", "addr:town", "addr:village") is { Length: > 0 } t ? t : query.Location.City,
                Phone = Tag(tags, "phone", "contact:phone", "contact:mobile"),
                Website = ProviderHelpers.NormalizeUrl(Tag(tags, "website", "contact:website", "url")),
                Latitude = element.Lat ?? element.Center?.Lat,
                Longitude = element.Lon ?? element.Center?.Lon,
                // OSM has no ratings; the columns stay empty rather than faked.
                Rating = null,
                ReviewCount = null,
                MapsUrl = ProviderHelpers.BuildMapsSearchUrl(name, address),
                Source = BusinessSource.OpenStreetMap,
            });
        }

        return results;
    }

    private static string Tag(IReadOnlyDictionary<string, string> tags, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (tags.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return string.Empty;
    }

    private static string BuildAddress(IReadOnlyDictionary<string, string> tags)
    {
        var street = string.Join(' ', new[]
        {
            Tag(tags, "addr:housenumber"),
            Tag(tags, "addr:street"),
        }.Where(part => part.Length > 0));

        return string.Join(", ", new[]
        {
            street,
            Tag(tags, "addr:suburb", "addr:neighbourhood"),
            Tag(tags, "addr:city", "addr:town", "addr:village"),
            Tag(tags, "addr:postcode"),
        }.Where(part => part.Length > 0));
    }

    public async Task<ResolvedLocation?> ResolveAsync(
        string country,
        string? state,
        string city,
        ProviderContext context,
        CancellationToken ct = default)
    {
        await NominatimLimiter.WaitAsync(ct);

        var client = httpClientFactory.CreateClient(ScraperHttpClients.Provider);

        // Nominatim's structured query wants a country name, not an ISO code.
        var countryName = LocationNames.CountryName(country);

        var url = $"{NominatimUrl}?city={Uri.EscapeDataString(city)}" +
                  (string.IsNullOrWhiteSpace(state) ? string.Empty : $"&state={Uri.EscapeDataString(state)}") +
                  $"&country={Uri.EscapeDataString(countryName)}&format=jsonv2&limit=1&addressdetails=1";

        // Nominatim answers in well under a second when healthy; it must not
        // inherit the Overpass-sized ceiling on the shared client.
        using var deadline = ScraperHttpClients.Deadline(context.Options.RequestTimeoutMs, ct);

        var response = await ScraperRetry.ExecuteAsync(
            async _ => await client.GetAsync(url, deadline.Token),
            context.Options.RetryAttempts,
            logger,
            ct);

        using (response)
        {
            if (!response.IsSuccessStatusCode) return null;

            var results = await response.Content.ReadFromJsonAsync<List<NominatimResult>>(
                cancellationToken: deadline.Token);
            var first = results?.FirstOrDefault();

            if (first is null ||
                !double.TryParse(first.Lat, NumberStyles.Float, CultureInfo.InvariantCulture, out var lat) ||
                !double.TryParse(first.Lon, NumberStyles.Float, CultureInfo.InvariantCulture, out var lon))
            {
                return null;
            }

            var address = first.Address ?? new Dictionary<string, string>();

            return new ResolvedLocation(
                first.DisplayName ?? $"{city}, {countryName}",
                // The ISO code, not the display name: it is what gets stored on
                // the lead and what the front end filters by.
                country,
                address.GetValueOrDefault("state")
                    ?? address.GetValueOrDefault("region")
                    ?? state
                    ?? string.Empty,
                address.GetValueOrDefault("city")
                    ?? address.GetValueOrDefault("town")
                    ?? address.GetValueOrDefault("village")
                    ?? city,
                lat,
                lon);
        }
    }

    // --- wire formats -------------------------------------------------------

    private sealed class OverpassResponse
    {
        [JsonPropertyName("elements")]
        public List<OverpassElement>? Elements { get; set; }
    }

    private sealed class OverpassElement
    {
        [JsonPropertyName("type")] public string Type { get; set; } = string.Empty;
        [JsonPropertyName("id")] public long Id { get; set; }
        [JsonPropertyName("lat")] public double? Lat { get; set; }
        [JsonPropertyName("lon")] public double? Lon { get; set; }
        [JsonPropertyName("center")] public OverpassCenter? Center { get; set; }
        [JsonPropertyName("tags")] public Dictionary<string, string>? Tags { get; set; }
    }

    private sealed class OverpassCenter
    {
        [JsonPropertyName("lat")] public double Lat { get; set; }
        [JsonPropertyName("lon")] public double Lon { get; set; }
    }

    private sealed class NominatimResult
    {
        [JsonPropertyName("lat")] public string? Lat { get; set; }
        [JsonPropertyName("lon")] public string? Lon { get; set; }
        [JsonPropertyName("display_name")] public string? DisplayName { get; set; }
        [JsonPropertyName("address")] public Dictionary<string, string>? Address { get; set; }
    }
}
