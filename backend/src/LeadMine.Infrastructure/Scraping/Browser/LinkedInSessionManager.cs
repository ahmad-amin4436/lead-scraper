using System.Collections.Concurrent;
using LeadMine.Application.DTOs;
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
/// sharing one dedicated automation account: the caller submits their own
/// LinkedIn email/password to <c>POST /api/linkedin/session/login</c>
/// (<see cref="BeginCredentialLoginAsync"/>), this class drives a real
/// server-side Playwright browser through LinkedIn's own login page with
/// those credentials, and only the resulting session (cookies + local
/// storage) is stored against their user id — the password itself is used
/// in-memory for that one login and never persisted or logged. This spreads
/// LinkedIn traffic across real, individually-owned accounts instead of
/// concentrating all of it on one thin account with no network of its own,
/// and each user accepts LinkedIn's terms-of-service risk for their own
/// account, on their own behalf.
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
    /// One in-progress credential login per user, from
    /// <see cref="BeginCredentialLoginAsync"/> until it resolves — see that
    /// method's remarks. In-memory by design, same reasoning as the old
    /// connect-token dictionary this replaced: losing one on a restart just
    /// means the browser context it held is gone and the user starts over.
    /// </summary>
    private readonly ConcurrentDictionary<Guid, PendingLinkedInLogin> _pendingLogins = new();

    private static readonly TimeSpan PendingLoginTtl = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Caps how many consecutive checkpoint pages (verification code, then
    /// maybe a "trust this device" confirmation, ...) one login attempt will
    /// walk through before giving up — LinkedIn can chain more than one, but
    /// an unbounded loop here would mean a stuck attempt never lets go of the
    /// user's gate.
    /// </summary>
    private const int MaxChallengeHops = 3;

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
            return "No LinkedIn session found for your account. Connect your LinkedIn account from Settings.";
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
                       $"(about {remainingHours:F1}h left). This protects your account — reconnect it from " +
                       "Settings to confirm it's fine and clear this early.";
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

    // --- credential login (server-side Playwright, drives LinkedIn's own login form) ---

    /// <summary>
    /// Starts a fresh LinkedIn login for <paramref name="userId"/> using their
    /// own LinkedIn email/password: launches a brand-new, storage-state-free
    /// Playwright context, drives it through <c>linkedin.com/login</c>, and
    /// classifies whatever LinkedIn does in response.
    /// <para>
    /// The credentials are used only to fill the two form fields on that page
    /// — never persisted, never logged. If LinkedIn accepts the login
    /// outright, the captured session is saved immediately via
    /// <see cref="UploadSessionAsync"/> and the browser context is closed. If
    /// LinkedIn raises a checkpoint asking for a verification code, the
    /// context and page are kept alive in <see cref="_pendingLogins"/> —
    /// still holding this user's gate (see <see cref="GetGate"/>) — until
    /// <see cref="SubmitLoginVerificationAsync"/> resolves it or it expires.
    /// </para>
    /// <para>
    /// A second call for the same user first tears down any pending attempt
    /// of theirs still in flight (<see cref="CancelLoginAsync"/>) — starting
    /// over always supersedes a stuck or abandoned one rather than deadlocking
    /// on their own gate.
    /// </para>
    /// </summary>
    public async Task<LinkedInLoginResult> BeginCredentialLoginAsync(Guid userId, string email, string password, CancellationToken ct)
    {
        await CancelLoginAsync(userId, ct);
        await ReapExpiredPendingLoginsAsync();

        var gate = GetGate(userId);
        await gate.WaitAsync(ct);

        BrowserContextLease? lease = null;
        IPage? page = null;

        try
        {
            lease = await browser.AcquireContextAsync(new BrowserNewContextOptions
            {
                Locale = "en-US",
                ViewportSize = new ViewportSize { Width = 1366, Height = 900 },
                UserAgent = LoginUserAgent,
            }, ct);

            // LinkedIn's login page actively checks for automation signals — far
            // more aggressively than the pages an already-authenticated session
            // browses. Playwright's default fingerprint (navigator.webdriver =
            // true, a "HeadlessChrome"-flavoured UA) is exactly what that check
            // looks for, so a raw context here is likely to be met with a block
            // /verification interstitial instead of the real form. This is a
            // best-effort reduction of that signal, not a guarantee — a puzzle
            // challenge that gets through anyway resolves as
            // LoginPageKind.ChallengeUnsupported, not a crash.
            await lease.Context.AddInitScriptAsync(
                "Object.defineProperty(navigator, 'webdriver', { get: () => undefined });");

            page = await lease.Context.NewPageAsync();
            var timeoutMs = optionsMonitor.CurrentValue.RequestTimeoutMs;

            await page.GotoAsync("https://www.linkedin.com/login", new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout = timeoutMs,
            });

            var emailInput = await FindFirstVisibleAsync(page, EmailInputSelectors, ct)
                ?? throw new InvalidOperationException("Could not find LinkedIn's email field.");
            await emailInput.FillAsync(email);
            await PlaywrightBrowserManager.RandomDelayAsync(200, 700, ct);

            var passwordInput = await FindFirstVisibleAsync(page, PasswordInputSelectors, ct)
                ?? throw new InvalidOperationException("Could not find LinkedIn's password field.");
            await passwordInput.FillAsync(password);
            await PlaywrightBrowserManager.RandomDelayAsync(200, 700, ct);

            await ClickSubmitAsync(page, ct);
            await WaitForNavigationAwayFromLoginAsync(page, timeoutMs);
            await SafeWaitForLoadAsync(page, timeoutMs, ct);

            var classification = await ClassifyLoginPageAsync(page, ct);
            return await ResolveClassificationAsync(userId, lease, page, classification, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (page is not null)
            {
                await ScrapeFailureLogger.CaptureAsync(page, "linkedin-login", ex, optionsMonitor.CurrentValue, logger, ct);
            }

            if (lease is not null) await lease.DisposeAsync();
            gate.Release();
            logger.LogError(ex, "LinkedIn credential login failed for user {UserId}.", userId);
            return new LinkedInLoginResult
            {
                Status = LinkedInLoginStatus.Failed,
                Message = "Something went wrong talking to LinkedIn — it may have blocked this as automated " +
                          "traffic rather than rejecting the login itself. Try again in a moment.",
            };
        }
        catch
        {
            if (lease is not null) await lease.DisposeAsync();
            gate.Release();
            throw;
        }
    }

    /// <summary>
    /// A realistic, current desktop Chrome UA for the credential-login context
    /// specifically — LinkedIn's login page is the part of the site most likely
    /// to scrutinise this. Deliberately not <see cref="ScraperOptions.UserAgent"/>,
    /// which honestly identifies this app's own crawler for the plain-HTTP
    /// business-site fetches and would make automation here easier to spot, not harder.
    /// </summary>
    private const string LoginUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36";

    /// <summary>
    /// Submits a verification code into the checkpoint page a prior
    /// <see cref="BeginCredentialLoginAsync"/> call left pending for this
    /// user. May itself come back with another <see cref="LinkedInLoginStatus.VerificationRequired"/>
    /// — LinkedIn sometimes chains a second checkpoint (e.g. "trust this
    /// device") — bounded by <see cref="MaxChallengeHops"/>.
    /// </summary>
    public async Task<LinkedInLoginResult> SubmitLoginVerificationAsync(Guid userId, string code, CancellationToken ct)
    {
        if (!_pendingLogins.TryGetValue(userId, out var pending) || pending.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            await CancelLoginAsync(userId, ct);
            return new LinkedInLoginResult
            {
                Status = LinkedInLoginStatus.Failed,
                Message = "That login attempt has expired. Start again.",
            };
        }

        try
        {
            var codeInput = await FindCodeInputAsync(pending.Page, ct);

            if (codeInput is null)
            {
                _pendingLogins.TryRemove(userId, out _);
                await ReleaseLoginAsync(userId, pending.Lease);
                return new LinkedInLoginResult
                {
                    Status = LinkedInLoginStatus.Failed,
                    Message = "Could not find a verification field on LinkedIn's page. Start again.",
                };
            }

            await codeInput.FillAsync(code);
            await PlaywrightBrowserManager.RandomDelayAsync(200, 600, ct);

            var timeoutMs = optionsMonitor.CurrentValue.RequestTimeoutMs;
            await ClickSubmitAsync(pending.Page, ct);
            await WaitForNavigationAwayFromLoginAsync(pending.Page, timeoutMs);
            await SafeWaitForLoadAsync(pending.Page, timeoutMs, ct);

            var classification = await ClassifyLoginPageAsync(pending.Page, ct);
            return await ResolveClassificationAsync(userId, pending.Lease, pending.Page, classification, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await ScrapeFailureLogger.CaptureAsync(pending.Page, "linkedin-login-verify", ex, optionsMonitor.CurrentValue, logger, ct);

            _pendingLogins.TryRemove(userId, out _);
            await ReleaseLoginAsync(userId, pending.Lease);
            logger.LogError(ex, "LinkedIn verification-code submission failed for user {UserId}.", userId);
            return new LinkedInLoginResult
            {
                Status = LinkedInLoginStatus.Failed,
                Message = "Something went wrong talking to LinkedIn. Try again.",
            };
        }
        catch
        {
            _pendingLogins.TryRemove(userId, out _);
            await ReleaseLoginAsync(userId, pending.Lease);
            throw;
        }
    }

    /// <summary>
    /// Abandons this user's pending credential login, if any — closes the
    /// held browser context and releases their gate. Safe to call when
    /// nothing is pending (a no-op). Called both when the caller explicitly
    /// cancels (closing the "connect LinkedIn" dialog mid-checkpoint) and at
    /// the start of every fresh <see cref="BeginCredentialLoginAsync"/> to
    /// supersede a stuck or abandoned attempt.
    /// </summary>
    public async Task CancelLoginAsync(Guid userId, CancellationToken ct)
    {
        if (_pendingLogins.TryRemove(userId, out var pending))
        {
            await ReleaseLoginAsync(userId, pending.Lease);
        }
    }

    /// <summary>
    /// Turns a page classification into the next <see cref="LinkedInLoginResult"/>,
    /// shared by <see cref="BeginCredentialLoginAsync"/> and
    /// <see cref="SubmitLoginVerificationAsync"/> since both end up here after
    /// submitting a form and waiting for LinkedIn's response.
    /// </summary>
    private async Task<LinkedInLoginResult> ResolveClassificationAsync(
        Guid userId, BrowserContextLease lease, IPage page, LoginClassification classification, CancellationToken ct)
    {
        if (classification.Kind == LoginPageKind.VerificationCode)
        {
            var pending = _pendingLogins.AddOrUpdate(
                userId,
                _ => new PendingLinkedInLogin(lease, page, DateTimeOffset.UtcNow + PendingLoginTtl),
                (_, existing) =>
                {
                    existing.ExpiresAt = DateTimeOffset.UtcNow + PendingLoginTtl;
                    return existing;
                });

            pending.ChallengeHops++;

            if (pending.ChallengeHops > MaxChallengeHops)
            {
                _pendingLogins.TryRemove(userId, out _);
                await ReleaseLoginAsync(userId, lease);
                return new LinkedInLoginResult
                {
                    Status = LinkedInLoginStatus.Failed,
                    Message = "LinkedIn asked for too many additional verification steps. Try again later.",
                };
            }

            return new LinkedInLoginResult { Status = LinkedInLoginStatus.VerificationRequired, Message = classification.Message };
        }

        _pendingLogins.TryRemove(userId, out _);

        if (classification.Kind == LoginPageKind.Success)
        {
            // The URL-based classification above is a heuristic ("doesn't look
            // like a login/checkpoint page anymore"), not proof. The one
            // reliable signal that LinkedIn actually authenticated the
            // context is its own session cookie — check for that before ever
            // persisting or reporting success, so a page that quietly failed
            // to submit (still on /login, no error text found) can never be
            // saved as a working session and silently break every later call.
            var cookies = await lease.Context.CookiesAsync();
            var authenticated = cookies.Any(c => c.Name == "li_at" && !string.IsNullOrEmpty(c.Value));

            if (!authenticated)
            {
                await ReleaseLoginAsync(userId, lease);
                return new LinkedInLoginResult
                {
                    Status = LinkedInLoginStatus.Failed,
                    Message = "LinkedIn did not actually complete the sign-in even though the page moved on. Try again.",
                };
            }

            var storageStateJson = await lease.Context.StorageStateAsync();
            await ReleaseLoginAsync(userId, lease);
            await UploadSessionAsync(userId, storageStateJson, ct);
            return new LinkedInLoginResult { Status = LinkedInLoginStatus.Success, Message = "LinkedIn account connected." };
        }

        // Captured before releasing the context — a non-success classification
        // (especially the generic Failed case) is exactly what needs a
        // screenshot to actually debug, and unlike an exception this path
        // never otherwise triggers ScrapeFailureLogger.
        await ScrapeFailureLogger.CaptureAsync(
            page,
            $"linkedin-login-{classification.Kind}",
            new InvalidOperationException(classification.Message),
            optionsMonitor.CurrentValue,
            logger,
            ct);

        await ReleaseLoginAsync(userId, lease);

        var status = classification.Kind switch
        {
            LoginPageKind.InvalidCredentials => LinkedInLoginStatus.InvalidCredentials,
            LoginPageKind.ChallengeUnsupported => LinkedInLoginStatus.ChallengeUnsupported,
            LoginPageKind.Restricted => LinkedInLoginStatus.Restricted,
            _ => LinkedInLoginStatus.Failed,
        };

        return new LinkedInLoginResult { Status = status, Message = classification.Message };
    }

    /// <summary>Closes a login attempt's browser context and releases this user's gate — always done as a pair.</summary>
    private async Task ReleaseLoginAsync(Guid userId, BrowserContextLease lease)
    {
        await lease.DisposeAsync();
        GetGate(userId).Release();
    }

    /// <summary>
    /// Sweeps every user's expired pending login, not just the caller's own —
    /// run opportunistically at the start of each <see cref="BeginCredentialLoginAsync"/>
    /// call so a login someone abandoned entirely (closed the tab mid-checkpoint
    /// rather than cancelling) doesn't hold its browser context and gate open
    /// past <see cref="PendingLoginTtl"/>.
    /// </summary>
    private async Task ReapExpiredPendingLoginsAsync()
    {
        foreach (var (uid, pending) in _pendingLogins)
        {
            if (pending.ExpiresAt > DateTimeOffset.UtcNow) continue;
            if (_pendingLogins.TryRemove(uid, out _)) await ReleaseLoginAsync(uid, pending.Lease);
        }
    }

    /// <summary>
    /// High-confidence phrases from LinkedIn's own "wrong email/password"
    /// messaging. A fallback for when the page has no <c>role="alert"</c> or
    /// similarly reliable element to read the message off of — see the class
    /// remarks on <see cref="RestrictionPhrases"/> for why this app leans on
    /// LinkedIn's own wording rather than a generic id/class selector, which
    /// this login form does not have at all (React-generated, per-load ids).
    /// </summary>
    private static readonly string[] InvalidCredentialPhrases =
    [
        "wrong password",
        "not the right password",
        "don't recognize that email",
        "we don't recognize",
        "couldn't find a linkedin account",
        "please enter a valid email",
    ];

    /// <summary>Classifies whatever LinkedIn just did after a login-form or checkpoint-form submit.</summary>
    private async Task<LoginClassification> ClassifyLoginPageAsync(IPage page, CancellationToken ct)
    {
        var url = page.Url;

        if (await IsRestrictedContentAsync(page))
        {
            return new LoginClassification(LoginPageKind.Restricted, "LinkedIn showed a restriction warning while signing in.");
        }

        var stillOnLoginForm =
            url.Contains("/login", StringComparison.OrdinalIgnoreCase) ||
            url.Contains("/checkpoint/lg/login-submit", StringComparison.OrdinalIgnoreCase);

        if (stillOnLoginForm)
        {
            var errorText = await TryGetTextAsync(page, "[role='alert']")
                ?? await TryGetTextAsync(page, "#error-for-username")
                ?? await TryGetTextAsync(page, "#error-for-password");

            if (!string.IsNullOrWhiteSpace(errorText))
            {
                return new LoginClassification(LoginPageKind.InvalidCredentials, errorText.Trim());
            }

            var bodyText = await TryGetBodyTextAsync(page);
            if (bodyText is not null && InvalidCredentialPhrases.Any(p => bodyText.Contains(p, StringComparison.OrdinalIgnoreCase)))
            {
                return new LoginClassification(LoginPageKind.InvalidCredentials, "LinkedIn didn't accept that email or password.");
            }

            // Still on the login form and no error message found anywhere —
            // most likely the submit never actually registered (a selector
            // miss, or the click landing before the page finished settling).
            // Never fall through to the generic "doesn't look logged-out"
            // check below for this case: that check only recognizes a
            // handful of known logged-out URL patterns and does not include
            // the plain /login page itself, so without this early return a
            // silently-failed submit would be misread as a successful login.
            return new LoginClassification(
                LoginPageKind.Failed,
                "LinkedIn did not move past the login form. Try again — this can happen if the submit didn't register.");
        }

        if (url.Contains("/checkpoint/", StringComparison.OrdinalIgnoreCase))
        {
            var codeInput = await FindCodeInputAsync(page, ct);

            if (codeInput is not null)
            {
                var prompt = await TryGetTextAsync(page, "h1") ?? await TryGetTextAsync(page, "legend");
                var message = string.IsNullOrWhiteSpace(prompt) ? "LinkedIn needs a verification code to continue." : prompt.Trim();
                return new LoginClassification(LoginPageKind.VerificationCode, message);
            }

            return new LoginClassification(
                LoginPageKind.ChallengeUnsupported,
                "LinkedIn is asking for additional verification (like a puzzle) that can't be completed automatically. Try again later, or from a different network.");
        }

        if (!IsLoggedOutUrl(url))
        {
            return new LoginClassification(LoginPageKind.Success, "Logged in.");
        }

        return new LoginClassification(LoginPageKind.Failed, "LinkedIn did not accept the login for an unknown reason. Try again.");
    }

    /// <summary>
    /// LinkedIn's current login form (confirmed live, see the scrape-failure
    /// capture this replaced a broken <c>#username</c> selector with) renders
    /// its email/password fields with React-generated per-load ids and no
    /// <c>name</c> attribute at all — <c>id</c>/<c>name</c> selectors cannot
    /// work here regardless of what LinkedIn's markup looked like previously.
    /// <c>autocomplete</c> is the one attribute standardised for real browser
    /// autofill, so it survives obfuscated builds; <c>type</c> is the fallback.
    /// The form also renders more than one copy of each field (a hidden
    /// variant alongside the visible one), which is why every locator here is
    /// resolved through <see cref="FindFirstVisibleAsync"/> rather than a bare
    /// <c>.First</c> — the first DOM match is not reliably the visible one.
    /// </summary>
    private static readonly string[] EmailInputSelectors =
    [
        "input[autocomplete~='username']",
        "input[type='email']",
        "input#username",
    ];

    private static readonly string[] PasswordInputSelectors =
    [
        "input[autocomplete~='current-password']",
        "input[type='password']",
        "input#password",
    ];

    /// <summary>
    /// Candidate selectors for a LinkedIn checkpoint's verification-code field,
    /// tried in order. Best-effort against LinkedIn's current markup, not a
    /// contract — <see cref="LoginPageKind.ChallengeUnsupported"/> is the safe
    /// fallback when none match, rather than guessing wrong and submitting
    /// into the void.
    /// </summary>
    private static readonly string[] CodeInputSelectors =
    [
        "input[autocomplete='one-time-code']",
        "input[type='tel']",
        "input#input__email_verification_pin",
        "input[name='pin']",
        "input[name='code']",
        "input[type='text']:not([autocomplete~='username'])",
    ];

    private static Task<ILocator?> FindCodeInputAsync(IPage page, CancellationToken ct) => FindFirstVisibleAsync(page, CodeInputSelectors, ct);

    /// <summary>
    /// Deliberately <c>:text-is</c> (exact match), not <c>:has-text</c>
    /// (substring) — confirmed live, that substring version matched "Sign in
    /// <b>with Microsoft</b>" ahead of the real submit button in DOM order
    /// and clicked through to login.live.com instead of submitting the form.
    /// LinkedIn's login page renders several OAuth buttons whose labels all
    /// contain "Sign in", so an exact match is not optional here.
    /// </summary>
    private static readonly string[] SubmitSelectors =
    [
        "#two-step-submit-button",
        "button[type='submit']",
        "button:text-is('Sign in')",
        "button:text-is('Submit')",
        "button:text-is('Verify')",
        "button:text-is('Continue')",
        "button:text-is('Confirm')",
    ];

    private static async Task ClickSubmitAsync(IPage page, CancellationToken ct)
    {
        var submit = await FindFirstVisibleAsync(page, SubmitSelectors, ct);

        if (submit is not null)
        {
            await submit.ClickAsync();
            return;
        }

        // Last resort: Enter submits whichever field on LinkedIn's form last had focus.
        await page.Keyboard.PressAsync("Enter");
    }

    /// <summary>
    /// Gives a genuine submit time to actually take effect before classifying
    /// the page. LinkedIn's login form is a client-side SPA: a successful or
    /// checkpointed submit updates the URL via client-side routing rather than
    /// a full page load, so <c>DOMContentLoaded</c> (already satisfied for the
    /// document loaded back at the initial <c>GotoAsync</c>) does not wait for
    /// it — without this, classification could run before the route change
    /// happens at all. Best-effort: an invalid-credentials submit never
    /// navigates away from <c>/login</c>, so this simply times out for that
    /// case and classification proceeds to look for an inline error instead.
    /// </summary>
    private static async Task WaitForNavigationAwayFromLoginAsync(IPage page, int timeoutMs)
    {
        try
        {
            await page.WaitForURLAsync(
                url => !url.Contains("/login", StringComparison.OrdinalIgnoreCase),
                new PageWaitForURLOptions { Timeout = timeoutMs });
        }
        catch (TimeoutException)
        {
            // Never left /login — classification handles this correctly either
            // way (an inline error, or LoginPageKind.Failed), so this is not
            // itself an error.
        }
    }

    /// <summary>How long <see cref="FindFirstVisibleAsync"/> polls before giving up on every candidate.</summary>
    private const int VisibleElementPollTimeoutMs = 8000;

    private const int VisibleElementPollIntervalMs = 200;

    /// <summary>
    /// Resolves the first candidate selector with at least one currently
    /// visible match — appending Playwright's <c>:visible</c> pseudo-class
    /// (not native CSS; Playwright's own selector engine) so the query itself
    /// filters to visible elements, rather than resolving to whichever match
    /// happens to come first in DOM order and then discovering it is hidden.
    /// LinkedIn's own markup routinely renders more than one match for the
    /// same semantic field (see the class remarks above), so that distinction
    /// matters here.
    /// <para>
    /// Polls rather than checking once: <c>Locator.CountAsync()</c> reflects
    /// the DOM at the instant it's called, with none of the auto-waiting
    /// Playwright's own actions (<c>FillAsync</c>, <c>ClickAsync</c>) build
    /// in. LinkedIn's login page is a client-side-rendered SPA, so its inputs
    /// do not exist yet the moment <c>DOMContentLoaded</c> fires — calling
    /// this exactly once right after navigation is a real race that a single
    /// snapshot check loses often enough to matter (confirmed live: an
    /// "email field not found" failure with a screenshot showing the field
    /// rendered and visible moments later).
    /// </para>
    /// </summary>
    private static async Task<ILocator?> FindFirstVisibleAsync(IPage page, IEnumerable<string> selectors, CancellationToken ct)
    {
        var selectorList = selectors as IReadOnlyList<string> ?? selectors.ToList();
        var deadline = DateTime.UtcNow.AddMilliseconds(VisibleElementPollTimeoutMs);

        while (true)
        {
            foreach (var selector in selectorList)
            {
                try
                {
                    var locator = page.Locator($"{selector}:visible").First;
                    if (await locator.CountAsync() > 0) return locator;
                }
                catch
                {
                    // Selector didn't parse or match anything usable on this page — try the next candidate.
                }
            }

            if (DateTime.UtcNow >= deadline) return null;
            await Task.Delay(VisibleElementPollIntervalMs, ct);
        }
    }

    private static async Task<string?> TryGetTextAsync(IPage page, string selector)
    {
        try
        {
            var locator = page.Locator($"{selector}:visible").First;
            if (await locator.CountAsync() == 0) return null;
            var text = await locator.InnerTextAsync();
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch
        {
            return null;
        }
    }

    private static async Task<string?> TryGetBodyTextAsync(IPage page)
    {
        try
        {
            return await page.InnerTextAsync("body");
        }
        catch
        {
            return null;
        }
    }

    private static async Task SafeWaitForLoadAsync(IPage page, int timeoutMs, CancellationToken ct)
    {
        try
        {
            await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded, new PageWaitForLoadStateOptions { Timeout = timeoutMs });
        }
        catch (TimeoutException)
        {
            // LinkedIn sometimes keeps a connection open (analytics beacons, etc.)
            // well past DOMContentLoaded; the page itself is usually already usable.
        }

        await PlaywrightBrowserManager.RandomDelayAsync(500, 1200, ct);
    }

    /// <summary>
    /// Stores or replaces this user's LinkedIn session — the save side of the
    /// credential-login flow above. Also acts as a real human confirming the
    /// account is fine, so it clears any active restriction rather than
    /// making them wait out a cooldown they have already just personally
    /// verified past by logging in again.
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

/// <summary>
/// A credential login in flight for one user: the live browser context/page
/// parked at a checkpoint, waiting on <see cref="LinkedInSessionManager.SubmitLoginVerificationAsync"/>.
/// </summary>
internal sealed class PendingLinkedInLogin(BrowserContextLease lease, IPage page, DateTimeOffset expiresAt)
{
    public BrowserContextLease Lease { get; } = lease;
    public IPage Page { get; } = page;
    public DateTimeOffset ExpiresAt { get; set; } = expiresAt;
    public int ChallengeHops { get; set; }
}

/// <summary>What LinkedIn did after a login-form or checkpoint-form submit.</summary>
internal enum LoginPageKind
{
    Success,
    InvalidCredentials,
    VerificationCode,
    ChallengeUnsupported,
    Restricted,
    Failed,
}

/// <summary><paramref name="Message"/> is user-facing — either LinkedIn's own text or a plain-language fallback.</summary>
internal sealed record LoginClassification(LoginPageKind Kind, string Message);

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
