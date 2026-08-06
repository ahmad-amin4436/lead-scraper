using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace LeadMine.Infrastructure.Scraping.Providers;

/// <summary>
/// Field-level extraction from a rendered Google Maps place page, shared by
/// <see cref="PlaywrightGoogleMapsProvider"/> (discovery) and
/// <see cref="PlaywrightMapsEnrichmentService"/> (detail enrichment) — both
/// parse the exact same page, just for a different subset of fields.
/// <para>
/// Every selector here is anchored on <c>role</c>/<c>data-item-id</c>/
/// <c>aria-label</c> rather than Maps' minified CSS classes, and was confirmed
/// against the live site (not guessed) as part of building this out. They will
/// still drift eventually — Google changes this markup on no fixed schedule —
/// which is the accepted trade-off of not using Apify.
/// </para>
/// </summary>
public static partial class GoogleMapsPageExtractor
{
    public static async Task AcceptConsentIfPresentAsync(IPage page)
    {
        try
        {
            var consent = page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { NameString = "Accept all" });
            await consent.ClickAsync(new LocatorClickOptions { Timeout = 3000 });
        }
        catch
        {
            // No consent dialog on this locale/session — the common case.
        }
    }

    public static async Task<bool> IsClosedAsync(IPage page)
    {
        var bodyText = await page.InnerTextAsync("body");
        return bodyText.Contains("Permanently closed", StringComparison.Ordinal) ||
               bodyText.Contains("Temporarily closed", StringComparison.Ordinal);
    }

    /// <summary>
    /// True when Maps served a verification interstitial — its "unusual
    /// traffic" notice or a redirect to <c>google.com/sorry/</c> — instead of
    /// real content. Checked so callers can stop and let the run's error
    /// surface that this happened, rather than trying to solve or click
    /// through a challenge that is not this app's to bypass.
    /// </summary>
    public static async Task<bool> IsBlockedAsync(IPage page)
    {
        if (page.Url.Contains("google.com/sorry/", StringComparison.OrdinalIgnoreCase)) return true;

        var bodyText = await page.InnerTextAsync("body");
        return bodyText.Contains("unusual traffic", StringComparison.OrdinalIgnoreCase) ||
               bodyText.Contains("verify you're a human", StringComparison.OrdinalIgnoreCase) ||
               bodyText.Contains("recaptcha", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Reads <c>[data-item-id="&lt;itemId&gt;"]</c>'s aria-label and strips a
    /// leading "Label: " prefix Maps adds — e.g. "Address: 123 Main St" → "123 Main St".
    /// </summary>
    public static async Task<string?> ReadDataItemLabelAsync(IPage page, string itemId, string stripPrefix)
    {
        var locator = page.Locator($"[data-item-id='{itemId}']").First;
        if (await locator.CountAsync() == 0) return null;

        var label = await locator.GetAttributeAsync("aria-label");
        if (string.IsNullOrWhiteSpace(label)) return null;

        var trimmed = label.Trim();
        return trimmed.StartsWith(stripPrefix, StringComparison.OrdinalIgnoreCase)
            ? trimmed[stripPrefix.Length..].Trim()
            : trimmed;
    }

    public static async Task<string?> ReadDataItemHrefAsync(IPage page, string itemId)
    {
        var locator = page.Locator($"[data-item-id='{itemId}']").First;
        if (await locator.CountAsync() == 0) return null;

        var href = await locator.GetAttributeAsync("href");
        return string.IsNullOrWhiteSpace(href) ? null : href.Trim();
    }

    /// <summary>
    /// The phone number lives in the <c>data-item-id</c> attribute itself
    /// (<c>phone:tel:+1234567890</c>), which is more reliable than parsing the
    /// locale-dependent "Phone: " prefix out of the aria-label text.
    /// </summary>
    public static async Task<string?> ReadPhoneAsync(IPage page)
    {
        var locator = page.Locator("[data-item-id^='phone:tel:']").First;
        if (await locator.CountAsync() == 0) return null;

        var itemId = await locator.GetAttributeAsync("data-item-id");
        if (string.IsNullOrWhiteSpace(itemId)) return null;

        var match = PhoneItemIdRegex().Match(itemId);
        return match.Success ? match.Groups[1].Value : null;
    }

    /// <summary>
    /// Rating and review count are separate, anchored accessible labels (e.g.
    /// "4.8 stars" and "1,234 reviews"), not one combined string. The page also
    /// contains unrelated star labels (a per-star review-count histogram, a
    /// "similar places" carousel), so both patterns are anchored end-to-end to
    /// avoid matching those.
    /// <para>
    /// Confirmed against multiple live listings that the review-count label is
    /// inconsistently present: some pages expose it right next to the rating,
    /// others (e.g. a plain <c>div.F7nice</c> with only the numeric rating and
    /// the star widget, no adjacent count) do not render it anywhere in the
    /// initially-loaded DOM at all. <see cref="ReviewCount"/> in the returned
    /// tuple is `null` in that case, not a bug — callers (and anything filtering
    /// on <c>MinReviews</c>) should treat it as best-effort.
    /// </para>
    /// </summary>
    public static async Task<(double? Rating, int? ReviewCount)> ReadRatingAsync(IPage page)
    {
        var labels = await page.EvalOnSelectorAllAsync<string[]>(
            "[aria-label]", "els => els.map(e => e.getAttribute('aria-label') || '')");

        double? rating = null;
        int? reviewCount = null;

        foreach (var label in labels)
        {
            if (rating is null)
            {
                var ratingMatch = RatingLabelRegex().Match(label);
                if (ratingMatch.Success)
                {
                    rating = double.Parse(ratingMatch.Groups[1].Value, CultureInfo.InvariantCulture);
                }
            }

            if (reviewCount is null)
            {
                var reviewMatch = ReviewCountLabelRegex().Match(label);
                if (reviewMatch.Success)
                {
                    reviewCount = int.Parse(
                        reviewMatch.Groups[1].Value.Replace(",", string.Empty),
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture);
                }
            }

            if (rating is not null && reviewCount is not null) break;
        }

        return (rating, reviewCount);
    }

    /// <summary>
    /// Best-effort weekly hours. Confirmed live: the "Show open hours for the
    /// week" toggle exists and is clickable, but the table it reveals only
    /// ever carried today's row in testing — Maps may only render the rest on
    /// a wider viewport or a different interaction this didn't find. Whatever
    /// rows are actually present are still returned; a caller gets partial data
    /// rather than nothing.
    /// </summary>
    public static async Task<IReadOnlyList<(string Day, string Hours)>> ReadOpeningHoursAsync(IPage page)
    {
        try
        {
            var expandTrigger = page.Locator("[aria-label='Show open hours for the week']").First;
            if (await expandTrigger.CountAsync() > 0)
            {
                await expandTrigger.ClickAsync(new LocatorClickOptions { Timeout = 3000 });
            }
        }
        catch
        {
            // Fall through and read whatever is already rendered.
        }

        var rows = await page.EvalOnSelectorAllAsync<string[][]>(
            "table tr",
            "els => els.map(e => { const c = e.querySelectorAll('td'); " +
            "return [ (c[0]?.textContent||'').trim(), (c[1]?.textContent||'').trim() ]; })");

        return rows
            .Where(r => r.Length == 2 && r[0].Length > 0)
            .Select(r => (Day: r[0], Hours: r[1]))
            .ToList();
    }

    /// <summary>Place pages encode coordinates as <c>!3d{lat}!4d{lng}</c>, not the <c>@lat,lng</c> the search page URL uses.</summary>
    public static (double? Latitude, double? Longitude) ParseCoordinates(string url)
    {
        var match = CoordinatesRegex().Match(url);
        if (!match.Success) return (null, null);

        var lat = double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        var lng = double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
        return (lat, lng);
    }

    /// <summary>
    /// The <c>!1s0x...:0x...</c> hex pair Maps URLs carry — the closest
    /// available substitute for Apify's structured Place ID.
    /// </summary>
    public static string? ParsePlaceToken(string url)
    {
        var match = PlaceTokenRegex().Match(url);
        return match.Success ? match.Groups[1].Value : null;
    }

    /// <summary>Best-effort US-style ZIP parsed off the end of a Maps address string.</summary>
    public static string? ParsePostalCode(string? address)
    {
        if (string.IsNullOrWhiteSpace(address)) return null;
        var match = PostalCodeRegex().Match(address);
        return match.Success ? match.Value : null;
    }

    [GeneratedRegex(@"^phone:tel:(.+)$")]
    private static partial Regex PhoneItemIdRegex();

    [GeneratedRegex(@"^(\d+(?:\.\d+)?)\s+stars?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex RatingLabelRegex();

    [GeneratedRegex(@"^([\d,]+)\s+reviews?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex ReviewCountLabelRegex();

    [GeneratedRegex(@"!3d(-?\d+\.\d+)!4d(-?\d+\.\d+)")]
    private static partial Regex CoordinatesRegex();

    [GeneratedRegex(@"!1s([^!]+)!")]
    private static partial Regex PlaceTokenRegex();

    [GeneratedRegex(@"\b\d{5}(?:-\d{4})?\b")]
    private static partial Regex PostalCodeRegex();
}
