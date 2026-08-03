using System.Diagnostics;
using System.Net;
using System.Text;
using HtmlAgilityPack;
using LeadMine.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace LeadMine.Infrastructure.Scraping.Enrichment;

/// <summary>Contact details discovered on a business website.</summary>
public sealed class BusinessContact
{
    public string? Email { get; set; }
    public List<string> AdditionalEmails { get; set; } = [];
    public string? WhatsApp { get; set; }
    public string? Facebook { get; set; }
    public string? Instagram { get; set; }
    public string? LinkedIn { get; set; }
    public string? Twitter { get; set; }
    public string? YouTube { get; set; }
    public string? ContactFormUrl { get; set; }
    public List<string> WebsitePhones { get; set; } = [];
}

public sealed class EnrichmentResult
{
    public required BusinessContact Contact { get; init; }
    public required BusinessStatus Status { get; init; }
    public int PagesVisited { get; init; }
    public int PagesBlocked { get; init; }
    public long ElapsedMs { get; init; }
    public string? Error { get; init; }
    public string Notes { get; init; } = string.Empty;

    public static EnrichmentResult Empty(BusinessStatus status, string notes, string? error = null) => new()
    {
        Contact = new BusinessContact(),
        Status = status,
        Notes = notes,
        Error = error,
    };
}

/// <summary>
/// Discovers publicly listed contact details from a business website.
/// <para>
/// Scope is deliberately narrow: it fetches the homepage and a small number of
/// linked contact/about pages over plain HTTP, reads what the site publishes,
/// and stops. It honours robots.txt, sends an identifying User-Agent, paces
/// requests, and never attempts to authenticate, submit a form, or work around
/// any access control. Pages behind a login are simply not read.
/// </para>
/// </summary>
public sealed class EnrichmentService(
    IHttpClientFactory httpClientFactory,
    RobotsCache robotsCache,
    ILogger<EnrichmentService> logger)
{
    private const int MaxHtmlBytes = 3 * 1024 * 1024;

    public async Task<EnrichmentResult> EnrichAsync(
        string website,
        ScraperOptions options,
        CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();

        if (string.IsNullOrWhiteSpace(website))
        {
            return EnrichmentResult.Empty(BusinessStatus.NoWebsite, "No website listed");
        }

        if (!Uri.TryCreate(website, UriKind.Absolute, out var homepage) ||
            (homepage.Scheme != Uri.UriSchemeHttp && homepage.Scheme != Uri.UriSchemeHttps))
        {
            return EnrichmentResult.Empty(BusinessStatus.EnrichmentFailed, "Invalid website URL", "Invalid URL");
        }

        var targetCheck = await UrlGuard.CheckAsync(homepage.ToString(), ct);

        if (!targetCheck.Safe)
        {
            var reason = targetCheck.Reason ?? "Website host is not reachable";
            return EnrichmentResult.Empty(BusinessStatus.EnrichmentFailed, reason, reason);
        }

        var origin = new Uri(homepage.GetLeftPart(UriPartial.Authority));

        var robots = options.RespectRobotsTxt
            ? await robotsCache.GetAsync(origin, options.UserAgent, ct)
            : RobotsPolicy.Allow;

        // A site asking for a slower crawl gets it, even if our own delay is lower.
        var delay = TimeSpan.FromMilliseconds(
            Math.Max(options.DelayMs, robots.CrawlDelay?.TotalMilliseconds ?? 0));

        var state = new CrawlState();
        var home = await VisitAsync(homepage, robots, options, state, ct);

        if (home is null && state.PagesBlocked > 0)
        {
            return new EnrichmentResult
            {
                Contact = new BusinessContact(),
                Status = BusinessStatus.EnrichmentFailed,
                Notes = "Blocked by robots.txt",
                Error = state.FirstError ?? "Blocked by robots.txt",
                PagesBlocked = state.PagesBlocked,
                ElapsedMs = stopwatch.ElapsedMilliseconds,
            };
        }

        if (home is not null) Absorb(state, home);

        // Follow a few contact/about pages — that's where addresses usually live.
        var budget = Math.Max(0, options.MaxPagesPerSite - 1);

        foreach (var candidate in (home?.ContactLinks ?? []).Take(budget))
        {
            if (delay > TimeSpan.Zero) await Task.Delay(delay, ct);

            if (!Uri.TryCreate(candidate, UriKind.Absolute, out var candidateUri)) continue;

            var harvest = await VisitAsync(candidateUri, robots, options, state, ct);
            if (harvest is not null) Absorb(state, harvest);
        }

        return Finish(state, homepage, stopwatch);
    }

    private sealed class CrawlState
    {
        public List<string> Emails { get; } = [];
        public List<string> Phones { get; } = [];
        public Dictionary<SocialKey, string> Social { get; } = [];
        public string? ContactFormUrl { get; set; }
        public int PagesVisited { get; set; }
        public int PagesBlocked { get; set; }
        public string? FirstError { get; set; }
    }

    private async Task<PageHarvest?> VisitAsync(
        Uri url,
        RobotsPolicy robots,
        ScraperOptions options,
        CrawlState state,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (!robots.IsAllowed(url))
        {
            state.PagesBlocked++;
            return null;
        }

        try
        {
            var client = httpClientFactory.CreateClient(ScraperHttpClients.Crawler);

            using var response = await ScraperRetry.ExecuteAsync(
                async _ =>
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, url);
                    var result = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

                    if (ScraperRetry.IsRetryableStatus(result.StatusCode))
                    {
                        var status = result.StatusCode;
                        result.Dispose();
                        throw new HttpRequestException($"HTTP {(int)status}", null, status);
                    }

                    return result;
                },
                options.RetryAttempts,
                logger,
                ct);

            // 401/403 means the page is not public; respect that and move on.
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                state.PagesBlocked++;
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                state.FirstError ??= $"HTTP {(int)response.StatusCode}";
                return null;
            }

            var contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
            if (contentType.Length > 0 && !contentType.Contains("html", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var html = await RobotsCache.ReadCappedAsync(response, MaxHtmlBytes, ct);
            if (html.Length == 0) return null;

            state.PagesVisited++;

            var document = new HtmlDocument();
            document.LoadHtml(html);

            // The response URL, not the requested one: redirects change what
            // "same host" and relative links mean.
            var effectiveUrl = response.RequestMessage?.RequestUri ?? url;

            return ContactExtractor.Harvest(document, effectiveUrl, options.MaxPagesPerSite);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            state.FirstError ??= ex.Message;
            logger.LogDebug(ex, "Could not read {Url}", url);
            return null;
        }
    }

    private static void Absorb(CrawlState state, PageHarvest harvest)
    {
        foreach (var email in harvest.Emails)
        {
            if (!state.Emails.Contains(email, StringComparer.OrdinalIgnoreCase)) state.Emails.Add(email);
        }

        foreach (var phone in harvest.Phones)
        {
            if (!state.Phones.Contains(phone, StringComparer.OrdinalIgnoreCase)) state.Phones.Add(phone);
        }

        foreach (var (key, value) in harvest.Social) state.Social.TryAdd(key, value);

        state.ContactFormUrl ??= harvest.ContactFormUrl;
    }

    private static EnrichmentResult Finish(CrawlState state, Uri homepage, Stopwatch stopwatch)
    {
        var contact = new BusinessContact
        {
            Email = state.Emails.FirstOrDefault(),
            AdditionalEmails = state.Emails.Skip(1).Take(5).ToList(),
            WebsitePhones = state.Phones.Take(5).ToList(),
            ContactFormUrl = state.ContactFormUrl,
            Facebook = state.Social.GetValueOrDefault(SocialKey.Facebook),
            Instagram = state.Social.GetValueOrDefault(SocialKey.Instagram),
            LinkedIn = state.Social.GetValueOrDefault(SocialKey.LinkedIn),
            Twitter = state.Social.GetValueOrDefault(SocialKey.Twitter),
            YouTube = state.Social.GetValueOrDefault(SocialKey.YouTube),
        };

        if (state.Social.TryGetValue(SocialKey.WhatsApp, out var whatsapp))
        {
            contact.WhatsApp = ContactExtractor.CanonicalWhatsApp(whatsapp);
        }

        var status = state.PagesVisited == 0
            ? BusinessStatus.EnrichmentFailed
            : contact.Email is not null
                ? BusinessStatus.Enriched
                : BusinessStatus.Partial;

        var notes = new StringBuilder();

        if (state.PagesBlocked > 0) notes.Append($"{state.PagesBlocked} page(s) not publicly accessible");

        var siteHost = ContactExtractor.NormalizeHost(homepage.ToString());

        if (siteHost.Length > 0)
        {
            if (notes.Length > 0) notes.Append("; ");
            notes.Append($"Crawled {state.PagesVisited} page(s) on {siteHost}");
        }

        return new EnrichmentResult
        {
            Contact = contact,
            Status = status,
            PagesVisited = state.PagesVisited,
            PagesBlocked = state.PagesBlocked,
            ElapsedMs = stopwatch.ElapsedMilliseconds,
            // An error is only worth surfacing when nothing was read at all —
            // one broken sub-page on an otherwise-crawled site is not a failure.
            Error = state.PagesVisited == 0 ? state.FirstError ?? "No readable pages" : null,
            Notes = notes.ToString(),
        };
    }
}
