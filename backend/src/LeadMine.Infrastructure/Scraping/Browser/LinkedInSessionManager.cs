using System.Collections.Concurrent;
using System.Security.Cryptography;
using LeadMine.Domain.Entities;
using LeadMine.Infrastructure.Persistence;
using LeadMine.Infrastructure.Scraping.Providers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;

namespace LeadMine.Infrastructure.Scraping.Browser;

/// <summary>
/// Owns every user's own LinkedIn login session, one row per user in
/// <see cref="LinkedInAccountSession"/>.
/// <para>
/// Each app user brings their own LinkedIn account rather than the whole app
/// sharing one dedicated automation account: <c>backend/tools/LinkedInLogin</c>
/// runs on that user's own machine (the server has no desktop to log in
/// interactively), they log into their own LinkedIn account by hand, and the
/// resulting <c>storageState.json</c> is uploaded through the app — never a
/// password, only the already-authenticated session — and stored against
/// their user id. This spreads LinkedIn traffic across real,
/// individually-owned accounts instead of concentrating all of it on one thin
/// account with no network of its own, and each user accepts LinkedIn's
/// terms-of-service risk for their own account, on their own behalf.
/// </para>
/// <para>
/// Three things exist specifically to protect those accounts, each now scoped
/// per user rather than app-wide:
/// </para>
/// <list type="number">
/// <item>
/// <b>A per-user gate</b> (<see cref="_gates"/>). One user's own LinkedIn
/// calls still serialize against each other — several simultaneous tabs on
/// one logged-in session is a bot signal regardless of whose account it is —
/// but different users' accounts now run fully independently, since they are
/// genuinely different accounts with nothing to protect each other from.
/// </item>
/// <item>
/// <b>A daily search cap per account</b> (<see cref="EnsureSearchBudgetAsync"/>),
/// persisted on that user's row so a restart cannot reset it.
/// </item>
/// <item>
/// <b>A circuit breaker per account</b> (<see cref="ReportRestrictionAsync"/>):
/// the moment a call sees a restriction warning on one user's session, that
/// user's LinkedIn calls refuse to run for
/// <see cref="ScraperOptions.LinkedInRestrictionCooldownHours"/> — scoped to
/// them, not every user in the app, since the warning was about their
/// account specifically.
/// </item>
/// </list>
/// </summary>
public sealed class LinkedInSessionManager(
    PlaywrightBrowserManager browser,
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<ScraperOptions> optionsMonitor,
    ILogger<LinkedInSessionManager> logger)
{
    /// <summary>One gate per user — see the class remarks.</summary>
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _gates = new();

    /// <summary>
    /// Short-lived, single-use connect codes — see <see cref="CreateConnectToken"/>.
    /// In-memory by design: losing one on a restart just means the user
    /// generates a new one, and nothing worth persisting happened yet.
    /// </summary>
    private readonly ConcurrentDictionary<string, (Guid UserId, DateTimeOffset ExpiresAt)> _connectTokens = new();

    /// <summary>
    /// High-confidence phrases from LinkedIn's own restriction/verification
    /// interstitials. Kept narrow on purpose: this trips a 24-hour pause on a
    /// real user's account, so a false positive is expensive — a generic
    /// phrase like "please verify" (LinkedIn also uses that for routine,
    /// unrelated prompts) would cost a day of that user's automation for
    /// nothing.
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

    private SemaphoreSlim GetGate(Guid userId) => _gates.GetOrAdd(userId, _ => new SemaphoreSlim(1, 1));

    /// <summary>Null when ready; otherwise why this user's LinkedIn-backed calls cannot run.</summary>
    public async Task<string?> ReadinessAsync(Guid userId, CancellationToken ct)
    {
        var browserReadiness = browser.Readiness();
        if (browserReadiness is not null) return browserReadiness;

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LeadMineDbContext>();

        var session = await db.LinkedInAccountSessions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.UserId == userId, ct);

        if (session is null)
        {
            return "No LinkedIn session found for your account. Run backend/tools/LinkedInLogin on your own " +
                   "machine, logged into your own LinkedIn account, then upload the resulting storageState.json " +
                   "from Settings.";
        }

        if (session.RestrictedAt is { } restrictedAt)
        {
            var cooldown = TimeSpan.FromHours(optionsMonitor.CurrentValue.LinkedInRestrictionCooldownHours);
            var until = restrictedAt + cooldown;

            if (until > DateTimeOffset.UtcNow)
            {
                var remainingHours = (until - DateTimeOffset.UtcNow).TotalHours;
                var reason = string.IsNullOrWhiteSpace(session.RestrictedReason) ? "a restriction warning" : session.RestrictedReason;

                return $"Your LinkedIn automation is paused until {until:u} after {reason} " +
                       $"(about {remainingHours:F1}h left). This protects your account — re-run " +
                       "backend/tools/LinkedInLogin and re-upload your session to confirm it's fine and clear this early.";
            }
        }

        return null;
    }

    /// <summary>
    /// A browser context pre-loaded with this user's saved LinkedIn session.
    /// Throws if none exists — callers should check <see cref="ReadinessAsync"/>
    /// first for a cheaper pre-flight, same pattern every other provider uses.
    /// <para>
    /// Waits on this user's own gate before ever touching the browser — see
    /// the class remarks. The returned lease releases it on disposal,
    /// alongside the underlying context/concurrency-slot release
    /// <see cref="BrowserContextLease"/> already does.
    /// </para>
    /// </summary>
    public async Task<LinkedInContextLease> AcquireContextAsync(Guid userId, CancellationToken ct)
    {
        var notReady = await ReadinessAsync(userId, ct);
        if (notReady is not null) throw new ProviderException(notReady, ProviderFailure.MissingApiKey);

        var gate = GetGate(userId);
        await gate.WaitAsync(ct);

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<LeadMineDbContext>();

            var storageStateJson = await db.LinkedInAccountSessions
                .Where(s => s.UserId == userId)
                .Select(s => s.StorageStateJson)
                .FirstOrDefaultAsync(ct);

            if (string.IsNullOrWhiteSpace(storageStateJson))
            {
                throw new ProviderException(
                    "No LinkedIn session found for your account. Upload one from Settings.",
                    ProviderFailure.MissingApiKey);
            }

            var inner = await browser.AcquireContextAsync(
                new BrowserNewContextOptions
                {
                    // Handed the session content directly — never written to a
                    // shared file the way the single, app-wide session was.
                    StorageState = storageStateJson,
                    Locale = "en-US",
                    ViewportSize = new ViewportSize { Width = 1366, Height = 900 },
                },
                ct);

            return new LinkedInContextLease(inner, gate);
        }
        catch
        {
            gate.Release();
            throw;
        }
    }

    /// <summary>
    /// Confirms this user's saved session still works by visiting the feed and
    /// checking LinkedIn didn't bounce it to a login/checkpoint page, or show a
    /// restriction warning in place.
    /// </summary>
    public async Task<bool> IsSessionValidAsync(Guid userId, CancellationToken ct)
    {
        await using var lease = await AcquireContextAsync(userId, ct);
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
                await ReportRestrictionAsync(userId, "a restriction warning on the LinkedIn feed", ct);
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
    /// otherwise ordinary-looking URL rather than redirecting, so a URL check
    /// alone misses them.
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
    /// Trips this user's circuit breaker: their LinkedIn calls refuse to run
    /// (see <see cref="ReadinessAsync"/>) until
    /// <see cref="ScraperOptions.LinkedInRestrictionCooldownHours"/> has
    /// passed. Call the instant any LinkedIn-backed service sees
    /// <see cref="IsRestrictedContentAsync"/> return true for this user.
    /// </summary>
    public async Task ReportRestrictionAsync(Guid userId, string reason, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LeadMineDbContext>();

        var session = await db.LinkedInAccountSessions.FirstOrDefaultAsync(s => s.UserId == userId, ct);
        if (session is null) return;

        var cooldown = TimeSpan.FromHours(optionsMonitor.CurrentValue.LinkedInRestrictionCooldownHours);

        // A second warning while already paused extends the pause from now
        // rather than shortening it back to a fresh window from this later
        // moment — whatever tripped it plainly has not resolved.
        var alreadyPaused = session.RestrictedAt is { } existing && existing + cooldown > DateTimeOffset.UtcNow;

        if (!alreadyPaused) session.RestrictedAt = DateTimeOffset.UtcNow;
        session.RestrictedReason = reason;

        await db.SaveChangesAsync(ct);

        logger.LogError(
            "LinkedIn restriction detected for user {UserId} ({Reason}); pausing their LinkedIn automation for {Hours}h.",
            userId, reason, optionsMonitor.CurrentValue.LinkedInRestrictionCooldownHours);
    }

    /// <summary>
    /// Self-imposed daily cap on LinkedIn searches for this user's account, so
    /// this app finds LinkedIn's own "commercial use limit" by staying under a
    /// conservative number rather than by tripping it. Persisted on the
    /// user's row, so a restart does not quietly hand back a full day's budget.
    /// </summary>
    public async Task EnsureSearchBudgetAsync(Guid userId, CancellationToken ct)
    {
        var limit = optionsMonitor.CurrentValue.LinkedInMaxSearchesPerDay;

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LeadMineDbContext>();

        var session = await db.LinkedInAccountSessions.FirstOrDefaultAsync(s => s.UserId == userId, ct);

        if (session is null)
        {
            throw new ProviderException(
                "No LinkedIn session found for your account. Upload one from Settings.",
                ProviderFailure.MissingApiKey);
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (session.BudgetDate != today)
        {
            session.BudgetDate = today;
            session.SearchesToday = 0;
        }

        if (session.SearchesToday >= limit)
        {
            logger.LogWarning(
                "LinkedIn daily search cap ({Limit}) reached for user {UserId}; refusing further LinkedIn calls until UTC midnight",
                limit, userId);

            throw new ProviderException(
                $"Your LinkedIn daily search cap ({limit}) has been reached. Resets at UTC midnight. " +
                "Raise Scraper:LinkedInMaxSearchesPerDay if needed, but LinkedIn's own throttling is the real ceiling.",
                ProviderFailure.QuotaExceeded);
        }

        session.SearchesToday++;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Mints a short-lived (15-minute), single-use code so
    /// <c>backend/tools/LinkedInLogin</c> can push a captured session straight
    /// to this user's account over the API, instead of the user manually
    /// downloading and re-uploading a file. The tool has no browser session of
    /// its own to authenticate with, so this code — not a bearer token — is
    /// what proves which account the session it captures belongs to.
    /// </summary>
    public (string Token, DateTimeOffset ExpiresAt) CreateConnectToken(Guid userId)
    {
        foreach (var (key, entry) in _connectTokens)
        {
            if (entry.ExpiresAt <= DateTimeOffset.UtcNow) _connectTokens.TryRemove(key, out _);
        }

        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(15);
        _connectTokens[token] = (userId, expiresAt);

        return (token, expiresAt);
    }

    /// <summary>
    /// Redeems a code from <see cref="CreateConnectToken"/> and saves the
    /// session it carries against the user it was minted for. Single-use: the
    /// code is removed the moment it's looked up, valid or not. Returns false
    /// for an unknown, already-used, or expired code — the caller turns that
    /// into a 400 telling the user to generate a new one.
    /// </summary>
    public async Task<bool> RedeemConnectTokenAsync(string token, string storageStateJson, CancellationToken ct)
    {
        if (!_connectTokens.TryRemove(token, out var entry)) return false;
        if (entry.ExpiresAt <= DateTimeOffset.UtcNow) return false;

        await UploadSessionAsync(entry.UserId, storageStateJson, ct);
        return true;
    }

    /// <summary>
    /// Stores or replaces this user's LinkedIn session — the upload side of
    /// the self-service flow (see <c>LinkedInController</c>). A fresh upload
    /// is a real human confirming the account is fine, so it clears any
    /// active restriction rather than making them wait out a cooldown they
    /// have already personally verified past.
    /// </summary>
    public async Task UploadSessionAsync(Guid userId, string storageStateJson, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LeadMineDbContext>();

        var existing = await db.LinkedInAccountSessions.FirstOrDefaultAsync(s => s.UserId == userId, ct);

        if (existing is null)
        {
            db.LinkedInAccountSessions.Add(new LinkedInAccountSession
            {
                UserId = userId,
                StorageStateJson = storageStateJson,
                UploadedAt = DateTimeOffset.UtcNow,
                BudgetDate = DateOnly.FromDateTime(DateTime.UtcNow),
            });
        }
        else
        {
            existing.StorageStateJson = storageStateJson;
            existing.UploadedAt = DateTimeOffset.UtcNow;
            existing.RestrictedAt = null;
            existing.RestrictedReason = null;
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>Status for the Settings page — never the session content itself.</summary>
    public async Task<LinkedInSessionStatus?> GetStatusAsync(Guid userId, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LeadMineDbContext>();

        var session = await db.LinkedInAccountSessions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.UserId == userId, ct);

        if (session is null) return null;

        DateTimeOffset? restrictedUntil = null;

        if (session.RestrictedAt is { } at)
        {
            var until = at + TimeSpan.FromHours(optionsMonitor.CurrentValue.LinkedInRestrictionCooldownHours);
            if (until > DateTimeOffset.UtcNow) restrictedUntil = until;
        }

        return new LinkedInSessionStatus(session.UploadedAt, session.SearchesToday, restrictedUntil, session.RestrictedReason);
    }

    public async Task RemoveSessionAsync(Guid userId, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LeadMineDbContext>();

        await db.LinkedInAccountSessions.Where(s => s.UserId == userId).ExecuteDeleteAsync(ct);
    }
}

/// <summary>Status shown on the Settings page. Deliberately excludes the session content itself.</summary>
public sealed record LinkedInSessionStatus(
    DateTimeOffset UploadedAt,
    int SearchesToday,
    DateTimeOffset? RestrictedUntil,
    string? RestrictedReason);

/// <summary>
/// A LinkedIn browser context. Releasing it releases both this user's gate
/// (see <see cref="LinkedInSessionManager"/>'s remarks) and the underlying
/// context/concurrency-slot release <see cref="BrowserContextLease"/> already
/// does — in that order, so the slot frees only once this user's LinkedIn
/// traffic has actually stopped.
/// </summary>
public sealed class LinkedInContextLease(BrowserContextLease inner, SemaphoreSlim userGate) : IAsyncDisposable
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
            userGate.Release();
        }
    }
}
