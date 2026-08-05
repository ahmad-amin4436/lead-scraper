using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;

namespace LeadMine.Infrastructure.Scraping.Browser;

/// <summary>
/// Owns the single shared Chromium instance every scraping service borrows a
/// context from.
/// <para>
/// "Start Browser Once → Reuse Browser": launching a fresh Chromium per lead
/// would dominate a run's wall-clock time on its own. Contexts (cookies,
/// storage, viewport) are cheap and short-lived; the browser process
/// underneath them is not, so this is the one thing in the scraping pipeline
/// deliberately built to outlive a single request — a singleton, not injected
/// per-call like the stateless Apify services it replaces.
/// </para>
/// <para>
/// Launching a real browser process has never been exercised on this host
/// before — every earlier data source here (Google Places, Apify) was an HTTP
/// API call, specifically because nobody knew whether this shared host could
/// spawn a child process at all. <see cref="Readiness"/> and
/// <see cref="TryLaunchAsync"/> exist so that uncertainty surfaces as a clear
/// message through the diagnostics endpoint, rather than as a mid-run 500 the
/// first time a real search needs the browser.
/// </para>
/// </summary>
public sealed class PlaywrightBrowserManager(
    IOptionsMonitor<ScraperOptions> optionsMonitor,
    ILogger<PlaywrightBrowserManager> logger) : IAsyncDisposable
{
    private readonly SemaphoreSlim _contextGate = new(
        Math.Max(1, optionsMonitor.CurrentValue.PlaywrightConcurrency));

    /// <summary>Serializes the (at-most-once) launch, not ordinary context acquisition.</summary>
    private readonly SemaphoreSlim _launchGate = new(1, 1);

    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private string? _lastFailure;

    /// <summary>
    /// Null unless a launch has already been tried and failed.
    /// <para>
    /// Deliberately optimistic before the first attempt — unlike an Apify API
    /// token, whose presence is knowable without making a call, whether this
    /// host can spawn a browser at all is genuinely unknown until something
    /// tries. Reporting "not ready" until proven otherwise would mean nothing
    /// ever calls <see cref="EnsureBrowserAsync"/> in the first place, since
    /// every provider checks this before it acts. Once a launch does fail, the
    /// reason is cached here so the rest of a run fails fast instead of
    /// retrying a slow, doomed launch on every task.
    /// </para>
    /// </summary>
    public string? Readiness() => _lastFailure;

    /// <summary>
    /// Confirms the browser can actually launch — distinct from
    /// <see cref="Readiness"/>, which only reports what is already known
    /// rather than attempting anything. This is what the diagnostics endpoint
    /// calls.
    /// </summary>
    public async Task<string?> TryLaunchAsync(CancellationToken ct)
    {
        try
        {
            await EnsureBrowserAsync(ct);
            return null;
        }
        catch (Exception ex)
        {
            return $"{ex.GetType().Name}: {ex.Message}";
        }
    }

    /// <summary>
    /// Hands out a new context, bounded by
    /// <see cref="ScraperOptions.PlaywrightConcurrency"/> so this process never
    /// runs more Chromium tabs at once than the host has memory for. Dispose
    /// the result to release both the context and its concurrency slot.
    /// </summary>
    public async Task<BrowserContextLease> AcquireContextAsync(
        BrowserNewContextOptions? contextOptions, CancellationToken ct)
    {
        await _contextGate.WaitAsync(ct);

        try
        {
            var browser = await EnsureBrowserAsync(ct);
            var context = await browser.NewContextAsync(contextOptions);
            return new BrowserContextLease(context, _contextGate);
        }
        catch
        {
            _contextGate.Release();
            throw;
        }
    }

    /// <summary>
    /// Jitter between browser actions. A fixed wait is exactly the kind of
    /// pattern automated-traffic detection looks for.
    /// </summary>
    public static async Task RandomDelayAsync(int minMs, int maxMs, CancellationToken ct)
    {
        var lower = Math.Max(0, Math.Min(minMs, maxMs));
        var upper = Math.Max(lower, maxMs);
        if (upper <= 0) return;

        var delay = Random.Shared.Next(lower, upper + 1);
        if (delay > 0) await Task.Delay(delay, ct);
    }

    /// <summary>
    /// Installs Chromium (first call only — a no-op if it is already present at
    /// <see cref="ScraperOptions.PlaywrightBrowsersPath"/>) and launches the
    /// shared browser. Safe to call concurrently: only the first caller
    /// through the gate actually does the work.
    /// </summary>
    private async Task<IBrowser> EnsureBrowserAsync(CancellationToken ct)
    {
        if (_browser is { IsConnected: true }) return _browser;

        await _launchGate.WaitAsync(ct);
        try
        {
            if (_browser is { IsConnected: true }) return _browser;

            var options = optionsMonitor.CurrentValue;
            var browsersPath = Path.GetFullPath(options.PlaywrightBrowsersPath);
            Directory.CreateDirectory(browsersPath);

            // Must be set before both the CLI install below and Playwright.CreateAsync()
            // — the two need to agree on where the browser binary lives.
            Environment.SetEnvironmentVariable("PLAYWRIGHT_BROWSERS_PATH", browsersPath);

            logger.LogInformation("Installing Chromium into {Path} (first use only)", browsersPath);

            var installExitCode = Microsoft.Playwright.Program.Main(["install", "chromium"]);
            if (installExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"`playwright install chromium` exited with code {installExitCode}. " +
                    "This host may not allow spawning the install/browser process at all.");
            }

            _playwright = await Playwright.CreateAsync();

            _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = true,
                Args = ["--disable-gpu", "--disable-dev-shm-usage", "--no-sandbox"],
            });

            logger.LogInformation("Chromium launched successfully.");
            _lastFailure = null;
            return _browser;
        }
        catch (Exception ex)
        {
            _lastFailure = $"{ex.GetType().Name}: {ex.Message}";
            logger.LogError(ex, "Failed to launch Chromium.");
            throw;
        }
        finally
        {
            _launchGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_browser is not null) await _browser.CloseAsync();
        _playwright?.Dispose();
    }
}

/// <summary>A borrowed <see cref="IBrowserContext"/> that releases its concurrency slot on dispose.</summary>
public sealed class BrowserContextLease(IBrowserContext context, SemaphoreSlim gate) : IAsyncDisposable
{
    public IBrowserContext Context { get; } = context;

    public async ValueTask DisposeAsync()
    {
        try
        {
            await Context.CloseAsync();
        }
        finally
        {
            gate.Release();
        }
    }
}
