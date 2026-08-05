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

    /// <summary>
    /// Apify API tokens, used to run the Google Maps Scraper actor as a
    /// parallel data source alongside OpenStreetMap.
    /// <para>
    /// A list rather than one value: tried in order, and a token that is
    /// rate-limited or rejected is skipped in favour of the next one, so one
    /// exhausted or revoked Apify account does not take the whole data source
    /// down. Empty means the Apify data source cannot run; every other
    /// provider is unaffected.
    /// </para>
    /// <para>
    /// Secrets: supply through user-secrets, environment variables
    /// (<c>Scraper__ApifyApiTokens__0</c>, <c>__1</c>, …) or the host's
    /// configuration store — never <c>appsettings.json</c>, which is committed.
    /// </para>
    /// </summary>
    public List<string> ApifyApiTokens { get; set; } = [];

    /// <summary>
    /// Apify actor id (<c>owner~actor-name</c> form). Defaults to the most
    /// widely used Google Maps scraper on the Apify Store — change this if you
    /// use a different actor with a compatible input/output shape.
    /// </summary>
    public string ApifyActorId { get; set; } = "compass~crawler-google-places";

    /// <summary>LinkedIn company-detail actor. See <see cref="ApifyActorId"/> for the format.</summary>
    public string ApifyLinkedInCompanyActorId { get; set; } = "harvestapi~linkedin-company";

    /// <summary>LinkedIn company-employees search actor. See <see cref="ApifyActorId"/> for the format.</summary>
    public string ApifyLinkedInPeopleActorId { get; set; } = "harvestapi~linkedin-company-employees";

    /// <summary>
    /// Wall-clock budget for one Apify actor run (start → finish), not the
    /// timeout of any single HTTP call. Scraping Google Maps is far slower than
    /// a Places API call, so this is sized in minutes rather than seconds.
    /// </summary>
    [Range(30_000, 900_000)]
    public int ApifyRunTimeoutMs { get; set; } = 180_000;

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
    /// Browser contexts open at once. Each headless Chromium instance is
    /// roughly 300-500MB; a shared host has little RAM to spare for this
    /// process on top of what it needs to keep serving requests.
    /// </summary>
    [Range(1, 8)]
    public int PlaywrightConcurrency { get; set; } = 2;

    /// <summary>Lower bound of the random delay between browser actions.</summary>
    [Range(0, 60_000)]
    public int PlaywrightMinDelayMs { get; set; } = 800;

    /// <summary>
    /// Upper bound of the random delay between browser actions. A fixed wait is
    /// exactly the kind of pattern automated-traffic detection looks for.
    /// </summary>
    [Range(0, 120_000)]
    public int PlaywrightMaxDelayMs { get; set; } = 2500;

    /// <summary>Where a persisted LinkedIn login session (storageState.json) is read from.</summary>
    public string LinkedInStorageStatePath { get; set; } = "App_Data/linkedin-session.json";

    /// <summary>
    /// Self-imposed cap on LinkedIn company/people searches per UTC day.
    /// LinkedIn throttles accounts that search heavily ("commercial use
    /// limit"); finding that ceiling ourselves is cheaper than finding it by
    /// getting the dedicated account restricted.
    /// </summary>
    [Range(1, 1000)]
    public int LinkedInMaxSearchesPerDay { get; set; } = 80;

    /// <summary>Where a failed extraction's screenshot + HTML dump are written, for debugging selector drift.</summary>
    public string ScrapeFailureLogPath { get; set; } = "App_Data/scrape-failures";

    public string UserAgent =>
        string.IsNullOrWhiteSpace(CrawlerContactEmail)
            ? "LeadMineAI/1.0 (+https://github.com/leadmine-ai; business contact discovery bot)"
            : $"LeadMineAI/1.0 (+mailto:{CrawlerContactEmail}; business contact discovery bot)";
}
