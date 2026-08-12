using System.Text.Json;
using LeadMine.Infrastructure.Scraping.Providers;
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
/// This account is doing something LinkedIn's terms prohibit, and protecting
/// it from ever being restricted is a hard, explicit requirement here — not a
/// nice-to-have. Three things exist specifically for that:
/// </para>
/// <list type="number">
/// <item>
/// <b>A single, process-wide gate on LinkedIn traffic</b> (<see cref="_linkedInGate"/>).
/// Every other concurrency knob in this app (<see cref="ScraperOptions.Concurrency"/>,
/// <see cref="ScraperOptions.PlaywrightConcurrency"/>) was deliberately raised
/// to let unrelated jobs run in parallel for throughput — Google Maps has no
/// account behind a request, so nothing there minds. LinkedIn is not mirrored:
/// several simultaneous tabs hitting linkedin.com from the one saved,
/// logged-in session at once — because three different users happened to
/// queue LinkedIn work at the same moment — is a far stronger automation
/// signal than the same volume spread out one at a time, which is what a
/// human with one browser actually looks like. This gate is what keeps every
/// LinkedIn call in the app serialized regardless of how parallel everything
/// else is.
/// </item>
/// <item>
/// <b>A persisted daily search cap</b> (<see cref="EnsureSearchBudget"/>),
/// surviving restarts via <see cref="ScraperOptions.LinkedInUsageStatePath"/>
/// rather than living only in memory — a restart used to silently reset the
/// count to zero, which defeats a *daily* cap.
/// </item>
/// <item>
/// <b>A circuit breaker</b> (<see cref="ReportRestriction"/>): the moment any
/// call sees a restriction warning — not just a logged-out redirect, an active
/// "you're being throttled" signal — every LinkedIn call anywhere in the app
/// refuses to run for <see cref="ScraperOptions.LinkedInRestrictionCooldownHours"/>,
/// so a warning does not get immediately followed by three more jobs doing the
/// exact thing that triggered it.
/// </item>
/// </list>
/// </summary>
public sealed class LinkedInSessionManager(
    PlaywrightBrowserManager browser,
    IOptionsMonitor<ScraperOptions> optionsMonitor,
    ILogger<LinkedInSessionManager> logger)
{
    private readonly object _stateLock = new();

    /// <summary>
    /// The one gate every LinkedIn browser operation in the app funnels
    /// through. See the class remarks — this is deliberately not scaled with
    /// <see cref="ScraperOptions.PlaywrightConcurrency"/>.
    /// </summary>
    private readonly SemaphoreSlim _linkedInGate = new(1, 1);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// High-confidence phrases from LinkedIn's own restriction/verification
    /// interstitials. Kept narrow on purpose: this trips a 24-hour, app-wide
    /// pause, so a false positive is expensive — a generic phrase like "please
    /// verify" (LinkedIn also uses that for routine, unrelated prompts) would
    /// cost a day of automation for nothing.
    /// </summary>
    private static readonly string[] RestrictionPhrases =
    [
        "unusual activity",
        "temporarily restricted",
        "your account has been restricted",
        "we've restricted",
        "commercial use limit",
        "security verification",
        "restricted from performing this action",
    ];

    /// <summary>Null when ready; otherwise why a LinkedIn-backed call cannot run.</summary>
    public string? Readiness()
    {
        var browserReadiness = browser.Readiness();
        if (browserReadiness is not null) return browserReadiness;

        var path = Path.GetFullPath(optionsMonitor.CurrentValue.LinkedInStorageStatePath);

        if (!File.Exists(path))
        {
            return $"No LinkedIn session found at {path}. Run backend/tools/LinkedInLogin on your own machine " +
                   "and upload the resulting storageState.json to this path.";
        }

        var state = LoadState();

        if (state.RestrictedAt is { } restrictedAt)
        {
            // Re-uploading the session file is a real, human confirmation the
            // account is fine — a file written after the restriction was
            // flagged clears the cooldown early instead of making someone
            // wait out the clock once they have already checked.
            var sessionRefreshedSince = File.GetLastWriteTimeUtc(path) > restrictedAt.UtcDateTime;

            if (!sessionRefreshedSince)
            {
                var cooldown = TimeSpan.FromHours(optionsMonitor.CurrentValue.LinkedInRestrictionCooldownHours);
                var until = restrictedAt + cooldown;

                if (until > DateTimeOffset.UtcNow)
                {
                    var remainingHours = (until - DateTimeOffset.UtcNow).TotalHours;
                    var reason = string.IsNullOrWhiteSpace(state.RestrictedReason) ? "a restriction warning" : state.RestrictedReason;

                    return $"LinkedIn automation is paused until {until:u} after {reason} " +
                           $"(about {remainingHours:F1}h left). This is a deliberate cooldown to protect the " +
                           "account, not a bug — re-run backend/tools/LinkedInLogin and re-upload the session " +
                           "to confirm the account is fine and clear it early.";
                }
            }
        }

        return null;
    }

    /// <summary>
    /// A browser context pre-loaded with the saved LinkedIn session. Throws if
    /// no session file exists — callers should check <see cref="Readiness"/>
    /// first for a cheaper pre-flight, same pattern every other provider uses.
    /// <para>
    /// Waits on the process-wide LinkedIn gate before ever touching the
    /// browser — see the class remarks. The returned lease releases it on
    /// disposal, alongside the underlying context/concurrency-slot release
    /// <see cref="BrowserContextLease"/> already does.
    /// </para>
    /// </summary>
    public async Task<LinkedInContextLease> AcquireContextAsync(CancellationToken ct)
    {
        var notReady = Readiness();
        if (notReady is not null) throw new ProviderException(notReady, ProviderFailure.MissingApiKey);

        await _linkedInGate.WaitAsync(ct);

        try
        {
            var statePath = Path.GetFullPath(optionsMonitor.CurrentValue.LinkedInStorageStatePath);

            var inner = await browser.AcquireContextAsync(
                new BrowserNewContextOptions
                {
                    StorageStatePath = statePath,
                    Locale = "en-US",
                    ViewportSize = new ViewportSize { Width = 1366, Height = 900 },
                },
                ct);

            return new LinkedInContextLease(inner, _linkedInGate);
        }
        catch
        {
            _linkedInGate.Release();
            throw;
        }
    }

    /// <summary>
    /// Confirms the saved session still works by visiting the feed and
    /// checking LinkedIn didn't bounce it to a login/checkpoint page, or show a
    /// restriction warning in place. Costs a real page load, so callers use it
    /// to fail fast on a dead session rather than calling it before every
    /// single search.
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

            if (IsLoggedOutUrl(page.Url)) return false;

            if (await IsRestrictedContentAsync(page))
            {
                ReportRestriction("a restriction warning on the LinkedIn feed");
                return false;
            }

            return true;
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
    /// True when LinkedIn is showing a restriction/verification warning on the
    /// current page — distinct from <see cref="IsLoggedOutUrl"/>, which only
    /// catches a redirect. LinkedIn often renders these as a banner on an
    /// otherwise ordinary-looking URL (a search results page, a company page)
    /// rather than redirecting, so a URL check alone misses them.
    /// </summary>
    public static async Task<bool> IsRestrictedContentAsync(IPage page)
    {
        string body;

        try
        {
            body = await page.InnerTextAsync("body");
        }
        catch
        {
            return false;
        }

        return RestrictionPhrases.Any(phrase => body.Contains(phrase, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Trips the circuit breaker: every LinkedIn call anywhere in the app —
    /// any job, any user — refuses to run (see <see cref="Readiness"/>) until
    /// <see cref="ScraperOptions.LinkedInRestrictionCooldownHours"/> has
    /// passed. Call the instant any LinkedIn-backed service sees
    /// <see cref="IsRestrictedContentAsync"/> return true.
    /// </summary>
    public void ReportRestriction(string reason)
    {
        lock (_stateLock)
        {
            var state = LoadStateNoLock();

            // A second warning while already paused extends the pause from
            // now rather than shortening it back to a fresh window from this
            // later moment — whatever tripped it plainly has not resolved.
            var alreadyPaused = state.RestrictedAt is { } existing &&
                existing + TimeSpan.FromHours(optionsMonitor.CurrentValue.LinkedInRestrictionCooldownHours) > DateTimeOffset.UtcNow;

            if (!alreadyPaused) state.RestrictedAt = DateTimeOffset.UtcNow;

            state.RestrictedReason = reason;
            SaveStateNoLock(state);
        }

        logger.LogError(
            "LinkedIn restriction detected ({Reason}); pausing all LinkedIn automation for {Hours}h.",
            reason, optionsMonitor.CurrentValue.LinkedInRestrictionCooldownHours);
    }

    /// <summary>
    /// Self-imposed daily cap on LinkedIn searches, so this app finds LinkedIn's
    /// own "commercial use limit" by staying under a conservative number rather
    /// than by tripping it on the dedicated account. Persisted (see the class
    /// remarks), so a restart does not quietly hand back a full day's budget.
    /// </summary>
    public void EnsureSearchBudget()
    {
        var limit = optionsMonitor.CurrentValue.LinkedInMaxSearchesPerDay;

        lock (_stateLock)
        {
            var state = LoadStateNoLock();

            if (state.SearchesToday >= limit)
            {
                logger.LogWarning(
                    "LinkedIn daily search cap ({Limit}) reached; refusing further LinkedIn calls until UTC midnight", limit);

                throw new ProviderException(
                    $"LinkedIn daily search cap ({limit}) reached for this process. Resets at UTC midnight. " +
                    "Raise Scraper:LinkedInMaxSearchesPerDay if needed, but LinkedIn's own throttling is the real ceiling.",
                    ProviderFailure.QuotaExceeded);
            }

            state.SearchesToday++;
            SaveStateNoLock(state);
        }
    }

    private LinkedInUsageState LoadState()
    {
        lock (_stateLock)
        {
            return LoadStateNoLock();
        }
    }

    /// <summary>Caller must already hold <see cref="_stateLock"/>.</summary>
    private LinkedInUsageState LoadStateNoLock()
    {
        var path = Path.GetFullPath(optionsMonitor.CurrentValue.LinkedInUsageStatePath);

        LinkedInUsageState state;

        try
        {
            state = File.Exists(path)
                ? JsonSerializer.Deserialize<LinkedInUsageState>(File.ReadAllText(path), JsonOptions) ?? new LinkedInUsageState()
                : new LinkedInUsageState();
        }
        catch (Exception ex)
        {
            // A corrupt file costs today's search count, not the app — starting
            // fresh is safe here since the day resets it anyway.
            logger.LogDebug(ex, "Could not read LinkedIn usage state at {Path}; starting fresh", path);
            state = new LinkedInUsageState();
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (state.Date != today)
        {
            state.Date = today;
            state.SearchesToday = 0;
        }

        return state;
    }

    /// <summary>Caller must already hold <see cref="_stateLock"/>.</summary>
    private void SaveStateNoLock(LinkedInUsageState state)
    {
        try
        {
            var path = Path.GetFullPath(optionsMonitor.CurrentValue.LinkedInUsageStatePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(state, JsonOptions));
        }
        catch (Exception ex)
        {
            // Best-effort: losing this write costs accuracy of the daily cap
            // and cooldown across the next restart, not correctness right now.
            logger.LogWarning(ex, "Could not persist LinkedIn usage state; it may not survive the next restart.");
        }
    }

    private sealed class LinkedInUsageState
    {
        public DateOnly Date { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow);
        public int SearchesToday { get; set; }
        public DateTimeOffset? RestrictedAt { get; set; }
        public string? RestrictedReason { get; set; }
    }
}

/// <summary>
/// A LinkedIn browser context. Releasing it releases both the process-wide
/// LinkedIn gate (see <see cref="LinkedInSessionManager"/>'s remarks) and the
/// underlying context/concurrency-slot release <see cref="BrowserContextLease"/>
/// already does — in that order, so the slot frees only once LinkedIn traffic
/// has actually stopped.
/// </summary>
public sealed class LinkedInContextLease(BrowserContextLease inner, SemaphoreSlim linkedInGate) : IAsyncDisposable
{
    public IBrowserContext Context => inner.Context;

    public async ValueTask DisposeAsync()
    {
        try
        {
            await inner.DisposeAsync();
        }
        finally
        {
            linkedInGate.Release();
        }
    }
}
