using LeadMine.Application.DTOs;
using LeadMine.Domain.Enums;
using LeadMine.Infrastructure.Scraping.Browser;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace LeadMine.Infrastructure.Scraping.Providers;

/// <summary>
/// Google Maps, driven directly with a real browser instead of Apify's actor.
/// <para>
/// Field extraction lives in <see cref="GoogleMapsPageExtractor"/>, shared with
/// <see cref="PlaywrightMapsEnrichmentService"/> since both parse the same
/// rendered page for a different subset of fields.
/// </para>
/// </summary>
public sealed class PlaywrightGoogleMapsProvider(
    PlaywrightBrowserManager browser,
    ILogger<PlaywrightGoogleMapsProvider> logger) : IPlaceProvider
{
    /// <summary>Consecutive scrolls that add no new result before giving up on finding more.</summary>
    private const int MaxNoChangeScrolls = 3;

    // Renamed to BusinessSource.GoogleMapsBrowser in Phase 3, together with the
    // matching frontend type union — until then this keeps the wire format
    // (JsonStringEnumConverter serializes the member name) in sync with what
    // the Next.js side still expects.
    public BusinessSource Source => BusinessSource.Apify;

    public string Label => "Google Maps (browser)";

    public string? Readiness(ProviderContext context) => browser.Readiness();

    public async Task<IReadOnlyList<ProviderBusiness>> SearchAsync(
        ProviderQuery query,
        ProviderContext context,
        CancellationToken ct = default)
    {
        var notReady = Readiness(context);
        if (notReady is not null) throw new ProviderException(notReady, ProviderFailure.MissingApiKey);

        var searchText = string.Join(
            " ",
            new[] { query.CategoryLabel, query.Location.City, query.Location.State, query.Location.Country }
                .Where(s => !string.IsNullOrWhiteSpace(s)));

        var searchUrl = $"https://www.google.com/maps/search/{Uri.EscapeDataString(searchText)}/?hl=en";

        await using var lease = await browser.AcquireContextAsync(
            new BrowserNewContextOptions
            {
                Locale = "en-US",
                ViewportSize = new ViewportSize { Width = 1366, Height = 900 },
            },
            ct);

        var searchPage = await lease.Context.NewPageAsync();

        await context.RateLimiter.WaitAsync(ct);
        await searchPage.GotoAsync(searchUrl, new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
            Timeout = context.Options.RequestTimeoutMs,
        });

        await GoogleMapsPageExtractor.AcceptConsentIfPresentAsync(searchPage);

        // A search with one dominant match sometimes lands directly on a place
        // page rather than a results list — treat the page itself as the sole
        // result rather than waiting forever for a feed that will not appear.
        if (searchPage.Url.Contains("/maps/place/"))
        {
            var single = await ExtractPlaceAsync(lease.Context, searchPage.Url, query, context, ct);
            await searchPage.CloseAsync();
            return single is null ? [] : [single];
        }

        var links = await CollectPlaceLinksAsync(searchPage, query.MaxResults, context, ct);
        await searchPage.CloseAsync();

        var results = new List<ProviderBusiness>(links.Count);

        foreach (var link in links)
        {
            if (results.Count >= query.MaxResults) break;
            ct.ThrowIfCancellationRequested();

            try
            {
                var business = await ExtractPlaceAsync(lease.Context, link, query, context, ct);
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

            await PlaywrightBrowserManager.RandomDelayAsync(
                context.Options.PlaywrightMinDelayMs, context.Options.PlaywrightMaxDelayMs, ct);
        }

        return results;
    }

    /// <summary>
    /// Scrolls the results feed, collecting place links as they load, until
    /// <paramref name="maxResults"/> is reached or a few consecutive scrolls add
    /// nothing new (end of the list). Confirmed live: the feed lazy-loads more
    /// results on <c>scrollTop</c> manipulation, going from 7 to 34 links over
    /// six scrolls on a real search.
    /// </summary>
    private static async Task<List<string>> CollectPlaceLinksAsync(
        IPage page, int maxResults, ProviderContext context, CancellationToken ct)
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

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var noChangeStreak = 0;

        while (seen.Count < maxResults && noChangeStreak < MaxNoChangeScrolls)
        {
            ct.ThrowIfCancellationRequested();

            var hrefs = await page.EvalOnSelectorAllAsync<string[]>(
                "a[href*='/maps/place/']", "els => els.map(e => e.href)");

            var before = seen.Count;
            foreach (var href in hrefs) seen.Add(href);

            noChangeStreak = seen.Count == before ? noChangeStreak + 1 : 0;
            if (seen.Count >= maxResults) break;

            await feed.EvaluateAsync("el => el.scrollTop = el.scrollHeight");
            await PlaywrightBrowserManager.RandomDelayAsync(
                context.Options.PlaywrightMinDelayMs, context.Options.PlaywrightMaxDelayMs, ct);
        }

        return seen.Take(maxResults).ToList();
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
                Source = BusinessSource.Apify,
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
