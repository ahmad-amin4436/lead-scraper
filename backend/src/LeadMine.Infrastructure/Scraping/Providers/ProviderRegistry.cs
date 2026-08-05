using LeadMine.Application.DTOs;
using Microsoft.Extensions.Logging;

namespace LeadMine.Infrastructure.Scraping.Providers;

/// <summary>
/// Picks the search provider for a run.
/// <para>
/// OpenStreetMap is the default because it needs no key. Google Places is used
/// when a run asks for it, or automatically when a key is configured — a
/// configured key means someone accepted the billing and wants the better data.
/// </para>
/// </summary>
public sealed class ProviderRegistry(
    GooglePlacesProvider google,
    OpenStreetMapProvider openStreetMap,
    ApifyGoogleMapsProvider apify,
    PlaywrightGoogleMapsProvider browserMaps,
    ILoggerFactory loggerFactory,
    ILogger<ProviderRegistry> logger)
{
    public const string GoogleId = "google-places";
    public const string OpenStreetMapId = "openstreetmap";
    public const string ApifyId = "apify";

    /// <summary>Google Maps (Apify) and OpenStreetMap, swept at the same time and merged.</summary>
    public const string ApifyParallelId = "apify-parallel";

    /// <summary>
    /// Google Maps via a real browser instead of Apify's actor — Phase 1 of the
    /// Apify replacement. Additive alongside <see cref="ApifyId"/> rather than
    /// replacing it yet, so this can be exercised end-to-end (a real search run)
    /// before Phase 3 cuts the search form and <c>SearchRunner</c> fallback over
    /// to it and removes the Apify path.
    /// </summary>
    public const string BrowserId = "browser";

    public IPlaceProvider Select(string? requested, ProviderContext context)
    {
        if (string.Equals(requested, BrowserId, StringComparison.OrdinalIgnoreCase))
        {
            var notReady = browserMaps.Readiness(context);
            if (notReady is not null) throw new ProviderException(notReady, ProviderFailure.MissingApiKey);

            return browserMaps;
        }

        if (string.Equals(requested, OpenStreetMapId, StringComparison.OrdinalIgnoreCase))
        {
            return openStreetMap;
        }

        if (string.Equals(requested, GoogleId, StringComparison.OrdinalIgnoreCase))
        {
            var notReady = google.Readiness(context);

            if (notReady is not null)
            {
                // Falling back silently would bill the user zero and quietly
                // return worse data than they asked for; say so instead.
                throw new ProviderException(notReady, ProviderFailure.MissingApiKey);
            }

            return google;
        }

        if (string.Equals(requested, ApifyId, StringComparison.OrdinalIgnoreCase))
        {
            var notReady = apify.Readiness(context);
            if (notReady is not null) throw new ProviderException(notReady, ProviderFailure.MissingApiKey);

            return apify;
        }

        if (string.Equals(requested, ApifyParallelId, StringComparison.OrdinalIgnoreCase))
        {
            var notReady = apify.Readiness(context);
            if (notReady is not null) throw new ProviderException(notReady, ProviderFailure.MissingApiKey);

            // A fresh instance per selection, not a singleton: it carries no
            // state of its own beyond the two providers it wraps (which are
            // themselves stateless singletons), so this is cheap, and it keeps
            // "constructed once per run" an invariant nothing downstream needs
            // to reason about.
            return new ParallelMergedProvider(apify, openStreetMap, loggerFactory.CreateLogger<ParallelMergedProvider>());
        }

        // No explicit choice: use Google if it can run, otherwise OSM.
        if (google.Readiness(context) is null) return google;

        logger.LogDebug("No Google Places key configured; using OpenStreetMap");
        return openStreetMap;
    }
}

/// <summary>
/// Resolves cities to coordinates, preferring Google and falling back to
/// Nominatim.
/// <para>
/// Results are cached process-wide. Geocoding the same city on every run wastes
/// Google quota and, on Nominatim, burns the one-request-per-second budget the
/// rest of the crawl needs.
/// </para>
/// </summary>
public sealed class GeocodingService(
    GooglePlacesProvider google,
    OpenStreetMapProvider openStreetMap,
    ILogger<GeocodingService> logger)
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, ResolvedLocation> Cache =
        new(StringComparer.OrdinalIgnoreCase);

    public async Task<ResolvedLocation?> ResolveAsync(
        string country,
        string? state,
        string city,
        ProviderContext context,
        CancellationToken ct = default)
    {
        var key = $"{country}|{state}|{city}";

        if (Cache.TryGetValue(key, out var cached)) return cached;

        var geocoders = string.IsNullOrWhiteSpace(context.GoogleApiKey)
            ? new IGeocoder[] { openStreetMap }
            : [google, openStreetMap];

        Exception? lastError = null;

        foreach (var geocoder in geocoders)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var resolved = await geocoder.ResolveAsync(country, state, city, context, ct);

                if (resolved is not null)
                {
                    Cache[key] = resolved;
                    return resolved;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastError = ex;
                logger.LogDebug(ex, "Geocoding {City} failed on one provider", city);
            }
        }

        if (lastError is not null)
        {
            throw new ProviderException($"Could not locate \"{city}\": {lastError.Message}", ProviderFailure.Transient, lastError);
        }

        return null;
    }

    public static void ClearCache() => Cache.Clear();
}
