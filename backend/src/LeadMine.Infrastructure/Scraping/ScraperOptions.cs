using System.ComponentModel.DataAnnotations;

namespace LeadMine.Infrastructure.Scraping;

/// <summary>
/// Tuning for the in-process scraper.
/// <para>
/// The worker runs inside the API rather than as a separate process, because the
/// deployment target is a .NET application host with no way to run a second
/// service. That makes politeness settings matter more, not less: this process
/// also serves user requests, so the scraper must never monopolise it.
/// </para>
/// </summary>
public sealed class ScraperOptions
{
    public const string SectionName = "Scraper";

    /// <summary>
    /// Turns the background worker on. Disable it on instances that should only
    /// serve HTTP — for example a second replica behind a load balancer.
    /// </summary>
    public bool WorkerEnabled { get; set; } = true;

    /// <summary>Search runs this instance executes at once.</summary>
    [Range(1, 8)]
    public int Concurrency { get; set; } = 1;

    /// <summary>Pause between polls when the queue is empty.</summary>
    [Range(1, 300)]
    public int IdlePollSeconds { get; set; } = 5;

    /// <summary>
    /// How long a claim holds without a heartbeat. Shorter recovers faster after
    /// a crash or an app-pool recycle; longer tolerates a slow task.
    /// </summary>
    [Range(30, 3600)]
    public int LeaseSeconds { get; set; } = 120;

    /// <summary>Websites crawled in parallel during enrichment.</summary>
    [Range(1, 16)]
    public int EnrichmentConcurrency { get; set; } = 4;

    /// <summary>Delay between requests to the same host.</summary>
    [Range(0, 60000)]
    public int DelayMs { get; set; } = 400;

    /// <summary>Cap on calls to a search provider, to stay inside quota.</summary>
    [Range(1, 600)]
    public int RateLimitPerMinute { get; set; } = 120;

    [Range(1, 10)]
    public int RetryAttempts { get; set; } = 3;

    [Range(1000, 120000)]
    public int RequestTimeoutMs { get; set; } = 15000;

    /// <summary>Homepage plus this many contact/about pages per site.</summary>
    [Range(1, 12)]
    public int MaxPagesPerSite { get; set; } = 4;

    /// <summary>
    /// Honour robots.txt. Leave on unless you own the sites being crawled.
    /// </summary>
    public bool RespectRobotsTxt { get; set; } = true;

    /// <summary>Contact address advertised in the crawler's User-Agent.</summary>
    public string CrawlerContactEmail { get; set; } = string.Empty;

    /// <summary>
    /// Google Places / Geocoding API key. Empty means the app runs on
    /// OpenStreetMap alone, which needs no key.
    /// <para>
    /// A secret: supply it through user-secrets, an environment variable
    /// (<c>Scraper__GoogleApiKey</c>) or the host's configuration store — never
    /// <c>appsettings.json</c>, which is committed.
    /// </para>
    /// </summary>
    public string GoogleApiKey { get; set; } = string.Empty;

    /// <summary>Leads buffered before a write to the database.</summary>
    [Range(1, 500)]
    public int SaveBatchSize { get; set; } = 25;

    // --- Playwright browser automation (Google Maps / LinkedIn) -------------

    /// <summary>
    /// Where Playwright's Chromium download lands. Under the app's own folder
    /// rather than a system-wide cache, since a shared host may not grant write
    /// access anywhere else — and it makes the install visible in a normal
    /// deployment backup instead of living outside it.
    /// <para>
    /// Keep this short. Confirmed against a real local launch: Chromium's own
    /// nested folder names (e.g.
    /// <c>chromium_headless_shell-1228\chrome-headless-shell-win64\chrome-headless-shell.exe</c>,
    /// ~70 characters on its own) push the full path over Windows' classic
    /// 260-character limit surprisingly easily, and <c>CreateProcess</c> fails
    /// with a bare <c>ENOENT</c> that gives no hint why. If the deployment
    /// root is already deep, point this at a short absolute path instead (e.g.
    /// <c>C:\pw\</c>), or enable
    /// <c>HKLM\SYSTEM\CurrentControlSet\Control\FileSystem\LongPathsEnabled</c>
    /// on the host.
    /// </para>
    /// </summary>
    public string PlaywrightBrowsersPath { get; set; } = "App_Data/playwright-browsers";

    /// <summary>
    /// Browser <em>contexts</em> open at once, across every concurrent job.
    /// <para>
    /// This is the single most important setting for multi-user throughput,
    /// and it used to be set as if each context were a whole browser: it is
    /// not. <c>PlaywrightBrowserManager</c> launches one Chromium process and
    /// hands out contexts on it — a context is an isolated profile (own
    /// cookies, storage, cache), costing tens of megabytes, not the 300-500MB
    /// a separate browser instance costs. Sizing this like browser instances
    /// throttled the whole application to two simultaneous browser operations
    /// for no real resource reason.
    /// </para>
    /// <para>
    /// Must comfortably exceed <see cref="Concurrency"/>, because a browser
    /// search holds its context for the entire duration of that search: with
    /// fewer contexts than concurrent jobs, the extra jobs are claimed and then
    /// block here waiting for one to free up, which looks exactly like the
    /// queue being stuck. Allow headroom for
    /// <see cref="EnrichmentConcurrency"/> on top, since each parallel
    /// enrichment lane takes its own context.
    /// </para>
    /// </summary>
    [Range(1, 64)]
    public int PlaywrightConcurrency { get; set; } = 12;

    /// <summary>Lower bound of the random delay between browser actions.</summary>
    [Range(0, 60_000)]
    public int PlaywrightMinDelayMs { get; set; } = 800;

    /// <summary>
    /// Upper bound of the random delay between browser actions. A fixed wait is
    /// exactly the kind of pattern automated-traffic detection looks for.
    /// </summary>
    [Range(0, 120_000)]
    public int PlaywrightMaxDelayMs { get; set; } = 2500;

    /// <summary>
    /// Self-imposed cap on LinkedIn company/people searches per UTC day, per
    /// user account. LinkedIn throttles accounts that search heavily
    /// ("commercial use limit"); finding that ceiling ourselves is cheaper
    /// than finding it by getting a user's account restricted. Persisted per
    /// user on <c>LinkedInAccountSession</c> (see <c>LinkedInSessionManager</c>),
    /// not a shared file — an app restart cannot reset it.
    /// </summary>
    [Range(1, 1000)]
    public int LinkedInMaxSearchesPerDay { get; set; } = 80;

    /// <summary>
    /// Lower/upper bound of the random delay between LinkedIn actions
    /// specifically — deliberately wider than <see cref="PlaywrightMinDelayMs"/>/
    /// <see cref="PlaywrightMaxDelayMs"/>, and not shared with them. Google Maps
    /// is a public page with no account behind a request; LinkedIn is a real,
    /// logged-in account doing something its terms prohibit (see
    /// <c>LinkedInSessionManager</c>'s remarks), so it gets the more
    /// conservative pacing of the two on purpose.
    /// </summary>
    [Range(0, 60_000)]
    public int LinkedInMinDelayMs { get; set; } = 2500;

    [Range(0, 180_000)]
    public int LinkedInMaxDelayMs { get; set; } = 6000;

    /// <summary>
    /// How long a user's LinkedIn-backed calls refuse to run after one of them
    /// detects a restriction warning on that user's account — not a plain
    /// logged-out redirect (the session simply died), but an active signal
    /// LinkedIn is flagging their account's traffic. Scoped to that one user;
    /// it says nothing about anyone else's account. Long enough that whatever
    /// tripped it has had time to settle; the user can still end it early by
    /// reconnecting their LinkedIn account from Settings, which counts as a
    /// fresh confirmation the account is fine.
    /// </summary>
    [Range(1, 168)]
    public int LinkedInRestrictionCooldownHours { get; set; } = 24;

    /// <summary>Where a failed extraction's screenshot + HTML dump are written, for debugging selector drift.</summary>
    public string ScrapeFailureLogPath { get; set; } = "App_Data/scrape-failures";

    public string UserAgent =>
        string.IsNullOrWhiteSpace(CrawlerContactEmail)
            ? "LeadMineAI/1.0 (+https://github.com/leadmine-ai; business contact discovery bot)"
            : $"LeadMineAI/1.0 (+mailto:{CrawlerContactEmail}; business contact discovery bot)";
}
