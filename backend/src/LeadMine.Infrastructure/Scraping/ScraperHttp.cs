using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;

namespace LeadMine.Infrastructure.Scraping;

/// <summary>Named <see cref="HttpClient"/> registrations used by the scraper.</summary>
public static class ScraperHttpClients
{
    /// <summary>Calls to search providers (Google Places, Overpass, Nominatim).</summary>
    public const string Provider = "scraper-provider";

    /// <summary>
    /// Server-side budget sent to Overpass, as <c>[timeout:N]</c>.
    /// <para>
    /// Must stay below <see cref="ProviderClientTimeout"/>. A community mirror
    /// under load genuinely takes most of a minute to answer, and hanging up
    /// first turns a slow-but-successful query into a failed task.
    /// </para>
    /// </summary>
    public const int OverpassQuerySeconds = 60;

    /// <summary>
    /// Ceiling for any provider call, sized around the slowest of them
    /// (Overpass). Faster providers are given their own shorter deadline at the
    /// call site rather than inheriting this one.
    /// </summary>
    public static readonly TimeSpan ProviderClientTimeout = TimeSpan.FromSeconds(90);

    /// <summary>
    /// Crawling business websites. Kept separate so redirects and response size
    /// can be constrained without affecting provider calls.
    /// </summary>
    public const string Crawler = "scraper-crawler";

    /// <summary>
    /// Narrows the shared provider client's ceiling for one call.
    /// <para>
    /// <see cref="HttpClient.Timeout"/> applies per client, so a fast provider
    /// sharing the client with Overpass would otherwise wait the full 90 seconds
    /// before giving up. Dispose the returned source when the call completes.
    /// </para>
    /// </summary>
    public static CancellationTokenSource Deadline(int milliseconds, CancellationToken ct)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(ct);
        source.CancelAfter(milliseconds);
        return source;
    }
}

/// <summary>Retry helpers shared by the scraper's outbound calls.</summary>
public static class ScraperRetry
{
    /// <summary>
    /// Runs <paramref name="action"/> with exponential backoff and full jitter.
    /// <para>
    /// Only transient conditions are retried — timeouts, socket failures, 429 and
    /// 5xx. A 404 or a malformed request will fail identically on the next
    /// attempt, so retrying it just wastes the run's time budget.
    /// </para>
    /// </summary>
    public static async Task<T> ExecuteAsync<T>(
        Func<int, Task<T>> action,
        int attempts,
        ILogger? logger = null,
        CancellationToken ct = default)
    {
        Exception? lastError = null;

        for (var attempt = 1; attempt <= Math.Max(1, attempts); attempt++)
        {
            try
            {
                return await action(attempt);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (IsTransient(ex))
            {
                lastError = ex;

                if (attempt >= attempts) break;

                var ceiling = Math.Min(15000, 500 * Math.Pow(2, attempt - 1));
                var delay = TimeSpan.FromMilliseconds(Random.Shared.NextDouble() * ceiling);

                logger?.LogDebug(
                    "Transient failure ({Message}); retrying in {Delay}ms",
                    ex.Message, delay.TotalMilliseconds);

                await Task.Delay(delay, ct);
            }
        }

        throw lastError ?? new InvalidOperationException("Retry failed without an exception.");
    }

    public static bool IsTransient(Exception ex) => ex switch
    {
        TaskCanceledException => true,
        TimeoutException => true,
        SocketException => true,
        HttpRequestException http => http.StatusCode is null
                                     || http.StatusCode == HttpStatusCode.TooManyRequests
                                     || (int)http.StatusCode >= 500,
        _ => false,
    };

    /// <summary>True when the status is worth another attempt.</summary>
    public static bool IsRetryableStatus(HttpStatusCode status) =>
        status == HttpStatusCode.TooManyRequests || (int)status >= 500;
}

/// <summary>
/// Spaces outbound calls so a run stays inside a provider's quota.
/// <para>
/// Instance-per-run: two concurrent searches each get their own budget, which is
/// the behaviour a per-provider quota actually implies.
/// </para>
/// </summary>
public sealed class RateLimiter(int requestsPerMinute)
{
    private readonly TimeSpan _minInterval =
        TimeSpan.FromMilliseconds(60_000d / Math.Max(1, requestsPerMinute));

    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTimeOffset _nextSlot = DateTimeOffset.MinValue;

    public async Task WaitAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);

        TimeSpan delay;
        try
        {
            var now = DateTimeOffset.UtcNow;
            var runAt = now > _nextSlot ? now : _nextSlot;
            delay = runAt - now;
            _nextSlot = runAt + _minInterval;
        }
        finally
        {
            _gate.Release();
        }

        if (delay > TimeSpan.Zero) await Task.Delay(delay, ct);
    }
}
