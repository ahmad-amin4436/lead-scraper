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

    public string UserAgent =>
        string.IsNullOrWhiteSpace(CrawlerContactEmail)
            ? "LeadMineAI/1.0 (+https://github.com/leadmine-ai; business contact discovery bot)"
            : $"LeadMineAI/1.0 (+mailto:{CrawlerContactEmail}; business contact discovery bot)";
}
