using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace LeadMine.Infrastructure.Scraping.Enrichment;

/// <summary>
/// A site's robots.txt, parsed into the directives that matter to this crawler:
/// User-agent grouping, Allow, Disallow and Crawl-delay.
/// </summary>
public sealed class RobotsPolicy
{
    private readonly record struct Rule(bool Allow, string Path, Regex Pattern);

    private readonly List<Rule> _rules;

    /// <summary>True when robots.txt was missing or unreadable — crawling is permitted.</summary>
    public bool Permissive { get; }

    /// <summary>The site's requested pause between requests, if it asked for one.</summary>
    public TimeSpan? CrawlDelay { get; }

    /// <summary>Nothing fetched, nothing forbidden.</summary>
    public static readonly RobotsPolicy Allow = new([], null, permissive: true);

    private RobotsPolicy(List<Rule> rules, TimeSpan? crawlDelay, bool permissive)
    {
        _rules = rules;
        CrawlDelay = crawlDelay;
        Permissive = permissive;
    }

    /// <summary>
    /// Decides whether a path may be fetched.
    /// <para>
    /// Follows the de-facto standard: the longest matching pattern wins, and
    /// Allow beats Disallow on a tie. An unmatched path is allowed.
    /// </para>
    /// </summary>
    public bool IsAllowed(Uri url)
    {
        if (Permissive || _rules.Count == 0) return true;

        var path = url.PathAndQuery;

        Rule? best = null;

        foreach (var rule in _rules)
        {
            // An empty Disallow means "allow everything" and matches nothing.
            if (rule.Path.Length == 0) continue;
            if (!rule.Pattern.IsMatch(path)) continue;

            if (best is null ||
                rule.Path.Length > best.Value.Path.Length ||
                (rule.Path.Length == best.Value.Path.Length && rule.Allow))
            {
                best = rule;
            }
        }

        return best?.Allow ?? true;
    }

    public static RobotsPolicy Parse(string text, string userAgentToken)
    {
        var groups = new List<(List<string> Agents, List<Rule> Rules, double? CrawlDelay)>();
        var lastLineWasAgent = false;
        var currentIndex = -1;

        foreach (var rawLine in text.Split('\n'))
        {
            // Strip comments, then whitespace (including the \r of CRLF files).
            var hash = rawLine.IndexOf('#');
            var line = (hash >= 0 ? rawLine[..hash] : rawLine).Trim();

            if (line.Length == 0) continue;

            var separator = line.IndexOf(':');
            if (separator < 0) continue;

            var field = line[..separator].Trim().ToLowerInvariant();
            var value = line[(separator + 1)..].Trim();

            if (field == "user-agent")
            {
                // Consecutive User-agent lines share one rule block.
                if (currentIndex < 0 || !lastLineWasAgent)
                {
                    groups.Add(([], [], null));
                    currentIndex = groups.Count - 1;
                }

                groups[currentIndex].Agents.Add(value.ToLowerInvariant());
                lastLineWasAgent = true;
                continue;
            }

            lastLineWasAgent = false;
            if (currentIndex < 0) continue;

            switch (field)
            {
                case "disallow":
                    groups[currentIndex].Rules.Add(new Rule(false, value, ToRegex(value)));
                    break;

                case "allow":
                    groups[currentIndex].Rules.Add(new Rule(true, value, ToRegex(value)));
                    break;

                case "crawl-delay":
                    if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) &&
                        double.IsFinite(seconds) && seconds >= 0)
                    {
                        var group = groups[currentIndex];
                        groups[currentIndex] = (group.Agents, group.Rules, seconds);
                    }

                    break;
            }
        }

        var token = userAgentToken.ToLowerInvariant();

        // The most specific matching group wins, falling back to `*`.
        var chosen = groups.FirstOrDefault(g => g.Agents.Any(a => a != "*" && token.Contains(a, StringComparison.Ordinal)));

        if (chosen.Agents is null)
        {
            chosen = groups.FirstOrDefault(g => g.Agents.Contains("*"));
        }

        if (chosen.Agents is null) return Allow;

        return new RobotsPolicy(
            chosen.Rules,
            chosen.CrawlDelay is { } d ? TimeSpan.FromSeconds(d) : null,
            permissive: false);
    }

    /// <summary>Converts a robots path pattern (<c>*</c> and <c>$</c>) to a regex.</summary>
    private static Regex ToRegex(string pattern)
    {
        var builder = new StringBuilder("^");
        var anchorEnd = pattern.EndsWith('$');
        var body = anchorEnd ? pattern[..^1] : pattern;

        foreach (var ch in body)
        {
            if (ch == '*') builder.Append(".*");
            else builder.Append(Regex.Escape(ch.ToString()));
        }

        if (anchorEnd) builder.Append('$');

        // Timeout: a pathological pattern from an untrusted site must not hang
        // the crawl.
        return new Regex(builder.ToString(), RegexOptions.None, TimeSpan.FromMilliseconds(250));
    }
}

/// <summary>
/// Fetches and caches robots.txt per origin.
/// <para>
/// A missing, erroring or unparseable robots.txt is treated as permissive, which
/// is how mainstream crawlers behave. Failing open keeps the run alive on a
/// network blip, but an explicit Disallow that was successfully read is always
/// honoured.
/// </para>
/// </summary>
public sealed class RobotsCache(IHttpClientFactory httpClientFactory, ILogger<RobotsCache> logger)
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(15);
    private const int MaxBytes = 512 * 1024;

    private static readonly ConcurrentDictionary<string, (RobotsPolicy Policy, DateTimeOffset ExpiresAt)> Cache =
        new(StringComparer.OrdinalIgnoreCase);

    public async Task<RobotsPolicy> GetAsync(Uri origin, string userAgent, CancellationToken ct = default)
    {
        var key = origin.GetLeftPart(UriPartial.Authority);

        if (Cache.TryGetValue(key, out var entry) && entry.ExpiresAt > DateTimeOffset.UtcNow)
        {
            return entry.Policy;
        }

        var policy = RobotsPolicy.Allow;

        try
        {
            var client = httpClientFactory.CreateClient(ScraperHttpClients.Crawler);

            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(origin, "/robots.txt"));
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

            if (response.IsSuccessStatusCode)
            {
                var text = await ReadCappedAsync(response, MaxBytes, ct);
                policy = RobotsPolicy.Parse(text, userAgent);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "robots.txt unreachable for {Origin}; treating as permissive", key);
        }

        Cache[key] = (policy, DateTimeOffset.UtcNow.Add(Ttl));
        return policy;
    }

    /// <summary>Reads at most <paramref name="maxBytes"/>, so a huge file cannot exhaust memory.</summary>
    internal static async Task<string> ReadCappedAsync(
        HttpResponseMessage response,
        int maxBytes,
        CancellationToken ct)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(ct);

        var buffer = new byte[8192];
        using var memory = new MemoryStream();

        int read;
        while ((read = await stream.ReadAsync(buffer, ct)) > 0)
        {
            var take = Math.Min(read, maxBytes - (int)memory.Length);
            memory.Write(buffer, 0, take);

            if (memory.Length >= maxBytes) break;
        }

        return Encoding.UTF8.GetString(memory.GetBuffer(), 0, (int)memory.Length);
    }
}
