using System.Globalization;
using LeadMine.Application.DTOs;
using LeadMine.Domain.Enums;
using LeadMine.Infrastructure.Scraping.Browser;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace LeadMine.Infrastructure.Scraping.Providers;

/// <summary>
/// Google Maps, driven directly with a real browser instead of Apify's actor
/// or the Google Places API.
/// <para>
/// Field extraction lives in <see cref="GoogleMapsPageExtractor"/>, shared with
/// <see cref="PlaywrightMapsEnrichmentService"/> since both parse the same
/// rendered page for a different subset of fields.
/// </para>
/// <para>
/// The requested radius is honoured by tiling: <see cref="GeoGrid"/> turns the
/// city center and radius into one or more viewport-biased search points, each
/// searched in turn and merged here, since the Maps website itself has no
/// radius parameter to hand it.
/// </para>
/// </summary>
public sealed class PlaywrightGoogleMapsProvider(
    PlaywrightBrowserManager browser,
    ILogger<PlaywrightGoogleMapsProvider> logger) : IPlaceProvider
{
    /// <summary>Consecutive scrolls that add no new result before giving up on finding more.</summary>
    private const int MaxNoChangeScrolls = 3;

    public BusinessSource Source => BusinessSource.GoogleMapsBrowser;

    public string Label => "Google Maps (browser)";

    public string? Readiness(ProviderContext context) => browser.Readiness();

    public async Task<IReadOnlyList<ProviderBusiness>> SearchAsync(
        ProviderQuery query,
        ProviderContext context,
        CancellationToken ct = default)
    {
        var notReady = Readiness(context);
        if (notReady is not null) throw new ProviderException(notReady, ProviderFailure.MissingApiKey);

        var cells = GeoGrid.BuildCoverage(query.Location.Latitude, query.Location.Longitude, query.RadiusMeters);

        await using var lease = await browser.AcquireContextAsync(
            new BrowserNewContextOptions
            {
                Locale = "en-US",
                ViewportSize = new ViewportSize { Width = 1366, Height = 900 },
            },
            ct);

        var results = new List<ProviderBusiness>();

        // Shared across every cell, keyed by place token rather than raw URL:
        // overlapping tiles routinely rediscover the same business, and this
        // is what stops it being opened twice.
        var seenTokens = new HashSet<string>(StringComparer.Ordinal);

        foreach (var cell in cells)
        {
            if (results.Count >= query.MaxResults) break;
            ct.ThrowIfCancellationRequested();

            // A cell's own feed-scrolling can already take a while before any
            // business is even opened; renew the lease going in rather than
            // only after the whole cell (which may itself run long) returns.
            if (context.Heartbeat is not null) await context.Heartbeat(ct);

            var blocked = await SearchCellAsync(lease.Context, query, cell, seenTokens, results, context, ct);

            // Once Maps has flagged this session, further cells would just
            // hit the same wall — stop expanding coverage and hand back
            // whatever was already found rather than hammering a blocked
            // session for the rest of the sweep.
            if (blocked) break;
        }

        return results;
    }

    /// <summary>
    /// Searches from one grid cell, appending newly-discovered businesses to
    /// <paramref name="results"/>. Returns true when Maps served a
    /// verification challenge for this search, so the caller stops trying
    /// further cells instead of attempting to bypass it.
    /// </summary>
    private async Task<bool> SearchCellAsync(
        IBrowserContext browserContext,
        ProviderQuery query,
        GeoGrid.Cell cell,
        HashSet<string> seenTokens,
        List<ProviderBusiness> results,
        ProviderContext context,
        CancellationToken ct)
    {
        var searchText = string.Join(
            " ",
            new[] { query.CategoryLabel, query.Location.City, query.Location.State, query.Location.Country }
                .Where(s => !string.IsNullOrWhiteSpace(s)));

        var lat = cell.Latitude.ToString("F6", CultureInfo.InvariantCulture);
        var lng = cell.Longitude.ToString("F6", CultureInfo.InvariantCulture);
        var searchUrl =
            $"https://www.google.com/maps/search/{Uri.EscapeDataString(searchText)}/@{lat},{lng},{cell.Zoom}z/?hl=en";

        string? singlePlaceUrl = null;
        List<string> links;

        var searchPage = await browserContext.NewPageAsync();

        try
        {
            await context.RateLimiter.WaitAsync(ct);
            await searchPage.GotoAsync(searchUrl, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout = context.Options.RequestTimeoutMs,
            });

            await GoogleMapsPageExtractor.AcceptConsentIfPresentAsync(searchPage);

            if (await GoogleMapsPageExtractor.IsBlockedAsync(searchPage))
            {
                await ScrapeFailureLogger.CaptureAsync(
                    searchPage,
                    "google-maps-search-blocked",
                    new ProviderException("Google Maps presented a verification challenge for this search."),
                    context.Options,
                    logger,
                    ct);

                logger.LogWarning(
                    "Google Maps (browser) was blocked searching for {Category} near {Lat},{Lng}; " +
                    "stopping this task's remaining coverage rather than trying to bypass it.",
                    query.CategoryLabel, lat, lng);

                return true;
            }

            // A search with one dominant match sometimes lands directly on a
            // place page rather than a results list — treat the page itself
            // as the sole result rather than waiting forever for a feed that
            // will not appear.
            if (searchPage.Url.Contains("/maps/place/"))
            {
                singlePlaceUrl = searchPage.Url;
                links = [];
            }
            else
            {
                var remaining = query.MaxResults - results.Count;
                links = await CollectPlaceLinksAsync(searchPage, remaining, seenTokens, context, ct);
            }
        }
        finally
        {
            // Done reading from the results page itself; detail extraction
            // below opens its own pages, so this one need not sit open (and
            // consuming memory) for the rest of the cell.
            await searchPage.CloseAsync();
        }

        if (singlePlaceUrl is not null)
        {
            var token = GoogleMapsPageExtractor.ParsePlaceToken(singlePlaceUrl) ?? singlePlaceUrl;

            if (seenTokens.Add(token) && results.Count < query.MaxResults)
            {
                var single = await ExtractPlaceAsync(browserContext, singlePlaceUrl, query, context, ct);
                if (single is not null) results.Add(single);
            }

            return false;
        }

        // This loop is what actually risks outrunning the job's lease: up to
        // MaxResults page loads, each with a deliberate anti-detection delay on
        // top. Sequential, so a plain timestamp is enough — no concurrent
        // callers to race like the engine's own Parallel.ForEach heartbeats do.
        var lastHeartbeat = DateTimeOffset.UtcNow;

        foreach (var link in links)
        {
            if (results.Count >= query.MaxResults) break;
            ct.ThrowIfCancellationRequested();

            try
            {
                var business = await ExtractPlaceAsync(browserContext, link, query, context, ct);
                if (business is not null) results.Add(business);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Failed to extract Google Maps place {Link}", link);
            }

            if (context.Heartbeat is not null && DateTimeOffset.UtcNow - lastHeartbeat > TimeSpan.FromSeconds(15))
            {
                await context.Heartbeat(ct);
                lastHeartbeat = DateTimeOffset.UtcNow;
            }

            await PlaywrightBrowserManager.RandomDelayAsync(
                context.Options.PlaywrightMinDelayMs, context.Options.PlaywrightMaxDelayMs, ct);
        }

        return false;
    }

    /// <summary>
    /// Scrolls the results feed, collecting place links as they load, until
    /// <paramref name="maxNew"/> new (not already in <paramref name="seenTokens"/>)
    /// links are found or a few consecutive scrolls add nothing new (end of the
    /// list). Confirmed live: the feed lazy-loads more results on
    /// <c>scrollTop</c> manipulation, going from 7 to 34 links over six scrolls
    /// on a real search.
    /// </summary>
    private static async Task<List<string>> CollectPlaceLinksAsync(
        IPage page, int maxNew, HashSet<string> seenTokens, ProviderContext context, CancellationToken ct)
    {
        var feed = page.Locator("[role='feed']").First;

        try
        {
            await feed.WaitForAsync(new LocatorWaitForOptions { Timeout = context.Options.RequestTimeoutMs });
        }
        catch (TimeoutException)
        {
            // No results feed at all — a genuinely empty search.
            return [];
        }

        var newLinks = new List<string>();
        var noChangeStreak = 0;

        while (newLinks.Count < maxNew && noChangeStreak < MaxNoChangeScrolls)
        {
            ct.ThrowIfCancellationRequested();

            var hrefs = await page.EvalOnSelectorAllAsync<string[]>(
                "a[href*='/maps/place/']", "els => els.map(e => e.href)");

            var addedThisPass = 0;

            foreach (var href in hrefs)
            {
                var token = GoogleMapsPageExtractor.ParsePlaceToken(href) ?? href;
                if (!seenTokens.Add(token)) continue;

                newLinks.Add(href);
                addedThisPass++;
            }

            noChangeStreak = addedThisPass == 0 ? noChangeStreak + 1 : 0;
            if (newLinks.Count >= maxNew) break;

            await feed.EvaluateAsync("el => el.scrollTop = el.scrollHeight");
            await PlaywrightBrowserManager.RandomDelayAsync(
                context.Options.PlaywrightMinDelayMs, context.Options.PlaywrightMaxDelayMs, ct);
        }

        return newLinks.Take(maxNew).ToList();
    }

    private async Task<ProviderBusiness?> ExtractPlaceAsync(
        IBrowserContext browserContext, string url, ProviderQuery query, ProviderContext context, CancellationToken ct)
    {
        var page = await browserContext.NewPageAsync();

        try
        {
            await context.RateLimiter.WaitAsync(ct);
            await page.GotoAsync(url, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout = context.Options.RequestTimeoutMs,
            });

            var heading = page.Locator("h1").First;

            try
            {
                await heading.WaitForAsync(new LocatorWaitForOptions { Timeout = context.Options.RequestTimeoutMs });
            }
            catch (TimeoutException)
            {
                return null;
            }

            var name = (await heading.InnerTextAsync()).Trim();
            if (name.Length == 0) return null;

            // A closed listing is not a lead, same rule every other provider applies.
            if (await GoogleMapsPageExtractor.IsClosedAsync(page)) return null;

            var address = await GoogleMapsPageExtractor.ReadDataItemLabelAsync(page, "address", "Address:");
            var website = await GoogleMapsPageExtractor.ReadDataItemHrefAsync(page, "authority");
            var phone = await GoogleMapsPageExtractor.ReadPhoneAsync(page);
            var (rating, reviewCount) = await GoogleMapsPageExtractor.ReadRatingAsync(page);
            var (latitude, longitude) = GoogleMapsPageExtractor.ParseCoordinates(page.Url);

            return new ProviderBusiness
            {
                ExternalId = GoogleMapsPageExtractor.ParsePlaceToken(page.Url) ?? string.Empty,
                Name = name,
                // Maps' UI does not expose a place's category as cleanly as
                // Apify's actor did; the search category is what every other
                // provider in this app uses for this field anyway.
                Category = query.CategoryLabel,
                Address = address ?? string.Empty,
                Country = query.Location.Country,
                State = query.Location.State,
                City = query.Location.City,
                Phone = phone ?? string.Empty,
                Website = ProviderHelpers.NormalizeUrl(website),
                Latitude = latitude,
                Longitude = longitude,
                Rating = rating,
                ReviewCount = reviewCount,
                MapsUrl = page.Url,
                Source = BusinessSource.GoogleMapsBrowser,
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await ScrapeFailureLogger.CaptureAsync(page, "google-maps-place", ex, context.Options, logger, ct);
            throw;
        }
        finally
        {
            await page.CloseAsync();
        }
    }
}
