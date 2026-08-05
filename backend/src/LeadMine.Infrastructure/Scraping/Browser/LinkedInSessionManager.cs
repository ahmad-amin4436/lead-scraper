using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;

namespace LeadMine.Infrastructure.Scraping.Browser;

/// <summary>
/// Owns the LinkedIn login session every LinkedIn-backed service borrows a
/// context from.
/// <para>
/// The server has no desktop to log in interactively, so login happens
/// off-server: <c>backend/tools/LinkedInLogin</c> is a standalone console tool
/// the user runs on their own machine, logging into the dedicated LinkedIn
/// account by hand once. It writes a Playwright <c>storageState.json</c>
/// (cookies + local storage), which gets uploaded to the server at
/// <see cref="ScraperOptions.LinkedInStorageStatePath"/>. Every context this
/// class hands out loads that file, so it starts already authenticated — no
/// LinkedIn credential is ever stored or entered on the server itself.
/// </para>
/// <para>
/// This account is doing something LinkedIn's terms prohibit and the user has
/// accepted that it may eventually be restricted. <see cref="IsSessionValidAsync"/>
/// and the daily search cap exist to make that a slow, visible degradation
/// (a clear "session expired" error) rather than something a run discovers by
/// silently returning nothing.
/// </para>
/// </summary>
public sealed class LinkedInSessionManager(
    PlaywrightBrowserManager browser,
    IOptionsMonitor<ScraperOptions> optionsMonitor,
    ILogger<LinkedInSessionManager> logger)
{
    private readonly object _counterLock = new();
    private DateOnly _counterDate = DateOnly.FromDateTime(DateTime.UtcNow);
    private int _searchesToday;

    /// <summary>Null when ready; otherwise why a LinkedIn-backed call cannot run.</summary>
    public string? Readiness()
    {
        var browserReadiness = browser.Readiness();
        if (browserReadiness is not null) return browserReadiness;

        var path = Path.GetFullPath(optionsMonitor.CurrentValue.LinkedInStorageStatePath);

        return File.Exists(path)
            ? null
            : $"No LinkedIn session found at {path}. Run backend/tools/LinkedInLogin on your own machine " +
              "and upload the resulting storageState.json to this path.";
    }

    /// <summary>
    /// A browser context pre-loaded with the saved LinkedIn session. Throws if
    /// no session file exists — callers should check <see cref="Readiness"/>
    /// first for a cheaper pre-flight, same pattern every other provider uses.
    /// </summary>
    public async Task<BrowserContextLease> AcquireContextAsync(CancellationToken ct)
    {
        var notReady = Readiness();
        if (notReady is not null) throw new ProviderException(notReady, ProviderFailure.MissingApiKey);

        var statePath = Path.GetFullPath(optionsMonitor.CurrentValue.LinkedInStorageStatePath);

        return await browser.AcquireContextAsync(
            new BrowserNewContextOptions
            {
                StorageStatePath = statePath,
                Locale = "en-US",
                ViewportSize = new ViewportSize { Width = 1366, Height = 900 },
            },
            ct);
    }

    /// <summary>
    /// Confirms the saved session still works by visiting the feed and
    /// checking LinkedIn didn't bounce it to a login/checkpoint page. Costs a
    /// real page load, so callers use it to fail fast on an expired session
    /// rather than calling it before every single search.
    /// </summary>
    public async Task<bool> IsSessionValidAsync(CancellationToken ct)
    {
        await using var lease = await AcquireContextAsync(ct);
        var page = await lease.Context.NewPageAsync();

        try
        {
            await page.GotoAsync("https://www.linkedin.com/feed/", new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout = optionsMonitor.CurrentValue.RequestTimeoutMs,
            });

            return !IsLoggedOutUrl(page.Url);
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    public static bool IsLoggedOutUrl(string url) =>
        url.Contains("/uas/login", StringComparison.OrdinalIgnoreCase) ||
        url.Contains("/checkpoint/", StringComparison.OrdinalIgnoreCase) ||
        url.Contains("/authwall", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Self-imposed daily cap on LinkedIn searches, so this app finds LinkedIn's
    /// own "commercial use limit" by staying under a conservative number rather
    /// than by tripping it on the dedicated account.
    /// </summary>
    public void EnsureSearchBudget()
    {
        var limit = optionsMonitor.CurrentValue.LinkedInMaxSearchesPerDay;

        lock (_counterLock)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            if (today != _counterDate)
            {
                _counterDate = today;
                _searchesToday = 0;
            }

            if (_searchesToday >= limit)
            {
                throw new ProviderException(
                    $"LinkedIn daily search cap ({limit}) reached for this process. Resets at UTC midnight. " +
                    "Raise Scraper:LinkedInMaxSearchesPerDay if needed, but LinkedIn's own throttling is the real ceiling.",
                    ProviderFailure.QuotaExceeded);
            }

            _searchesToday++;
        }
    }
}
