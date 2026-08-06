using System.Text.Json;
using LeadMine.Infrastructure.Scraping.Browser;
using LeadMine.Infrastructure.Scraping.Providers;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace LeadMine.Infrastructure.Scraping.Enrichment;

public sealed record MapsEnrichmentResult(
    string? PostalCode,
    string? OpeningHoursJson,
    string? PlaceId,
    string? ImageUrlsJson,
    bool? PermanentlyClosed,
    double? Rating,
    int? ReviewCount,
    string? Description);

/// <summary>
/// Fills in the Google Maps detail the discovery pass does not stop to collect
/// (opening hours, postal code, a fresher rating/review count) for a business
/// already found — via a real browser, the same page
/// <see cref="PlaywrightGoogleMapsProvider"/> already knows how to read, just
/// pointed at one specific listing instead of a fresh search.
/// </summary>
public sealed class PlaywrightMapsEnrichmentService(
    PlaywrightBrowserManager browser,
    ILogger<PlaywrightMapsEnrichmentService> logger)
{
    public string? Readiness() => browser.Readiness();

    /// <summary>
    /// Null when nothing could be matched. Takes plain fields rather than a
    /// <see cref="Domain.Entities.Business"/> entity because the caller
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
        var notReady = Readiness();
        if (notReady is not null) throw new ProviderException(notReady, ProviderFailure.MissingApiKey);

        await using var lease = await browser.AcquireContextAsync(
            new BrowserNewContextOptions
            {
                Locale = "en-US",
                ViewportSize = new ViewportSize { Width = 1366, Height = 900 },
            },
            ct);

        var page = await lease.Context.NewPageAsync();

        try
        {
            var targetUrl = await ResolveTargetUrlAsync(page, name, city, state, country, mapsUrl, context, ct);
            if (targetUrl is null) return null;

            if (page.Url != targetUrl)
            {
                await context.RateLimiter.WaitAsync(ct);
                await page.GotoAsync(targetUrl, new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.DOMContentLoaded,
                    Timeout = context.Options.RequestTimeoutMs,
                });
            }

            var heading = page.Locator("h1").First;
            try
            {
                await heading.WaitForAsync(new LocatorWaitForOptions { Timeout = context.Options.RequestTimeoutMs });
            }
            catch (TimeoutException)
            {
                return null;
            }

            if (await GoogleMapsPageExtractor.IsBlockedAsync(page))
            {
                await ScrapeFailureLogger.CaptureAsync(
                    page, "google-maps-enrich-blocked",
                    new ProviderException("Google Maps presented a verification challenge during enrichment."),
                    context.Options, logger, ct);

                return null;
            }

            var address = await GoogleMapsPageExtractor.ReadDataItemLabelAsync(page, "address", "Address:");
            var (rating, reviewCount) = await GoogleMapsPageExtractor.ReadRatingAsync(page);
            var hours = await GoogleMapsPageExtractor.ReadOpeningHoursAsync(page);
            var closed = await GoogleMapsPageExtractor.IsClosedAsync(page);

            return new MapsEnrichmentResult(
                PostalCode: GoogleMapsPageExtractor.ParsePostalCode(address),
                OpeningHoursJson: hours.Count > 0
                    ? JsonSerializer.Serialize(hours.Select(h => new { day = h.Day, hours = h.Hours }))
                    : null,
                PlaceId: GoogleMapsPageExtractor.ParsePlaceToken(page.Url),
                // Not attempted: the discovery/enrichment pass has no reliable,
                // stable way to enumerate a listing's photos short of clicking
                // into the photo gallery per lead, which is not worth the extra
                // page loads for a field nothing downstream currently uses.
                ImageUrlsJson: null,
                PermanentlyClosed: closed,
                Rating: rating,
                ReviewCount: reviewCount,
                Description: null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await ScrapeFailureLogger.CaptureAsync(page, "google-maps-enrich", ex, context.Options, logger, ct);
            throw;
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    /// <summary>
    /// A direct link to the exact listing beats re-searching by name: no fuzzy
    /// matching, and it is the URL discovery already gave us. Falls back to a
    /// fresh search only when no URL was saved.
    /// </summary>
    private static async Task<string?> ResolveTargetUrlAsync(
        IPage page,
        string name,
        string city,
        string state,
        string country,
        string mapsUrl,
        ProviderContext context,
        CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(mapsUrl))
        {
            await context.RateLimiter.WaitAsync(ct);
            await page.GotoAsync(mapsUrl, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout = context.Options.RequestTimeoutMs,
            });

            return page.Url;
        }

        var searchText = string.Join(" ", new[] { name, city, state, country }.Where(s => !string.IsNullOrWhiteSpace(s)));
        var searchUrl = $"https://www.google.com/maps/search/{Uri.EscapeDataString(searchText)}/?hl=en";

        await context.RateLimiter.WaitAsync(ct);
        await page.GotoAsync(searchUrl, new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
            Timeout = context.Options.RequestTimeoutMs,
        });

        await GoogleMapsPageExtractor.AcceptConsentIfPresentAsync(page);

        if (page.Url.Contains("/maps/place/")) return page.Url;

        var links = await page.EvalOnSelectorAllAsync<string[]>(
            "a[href*='/maps/place/']", "els => els.map(e => e.href)");

        return links.FirstOrDefault();
    }
}
