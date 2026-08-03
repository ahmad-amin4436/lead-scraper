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

/// <summary>
/// Opens outbound connections the way curl and browsers do, instead of the way
/// <see cref="SocketsHttpHandler"/> does by default.
/// <para>
/// The default handler connects through a single dual-mode IPv6 socket. On a
/// network where IPv6 is present but broken — common, and measured on the
/// original development machine — that socket stalls for tens of seconds before
/// failing, even when the host has perfectly good IPv4 addresses. Against Google
/// Places it turned a 1.2-second call into a 20+ second one, which then tripped
/// the request deadline and failed the task.
/// </para>
/// <para>
/// This implements the essential part of Happy Eyeballs (RFC 8305): resolve all
/// addresses, start a family-matched connection attempt for each staggered by
/// <see cref="AttemptStagger"/>, and take whichever completes first. A broken
/// family costs a quarter of a second rather than the whole request budget.
/// </para>
/// </summary>
public static class ScraperConnect
{
    /// <summary>Delay before racing the next address. RFC 8305 suggests 250 ms.</summary>
    private static readonly TimeSpan AttemptStagger = TimeSpan.FromMilliseconds(250);

    public static async ValueTask<Stream> ConnectAsync(
        SocketsHttpConnectionContext context,
        CancellationToken ct)
    {
        var host = context.DnsEndPoint.Host;
        var port = context.DnsEndPoint.Port;

        // A literal address needs no resolution and no racing.
        if (IPAddress.TryParse(host, out var literal))
        {
            return await OpenAsync(literal, port, ct);
        }

        var addresses = await Dns.GetHostAddressesAsync(host, ct);

        if (addresses.Length == 0)
        {
            throw new SocketException((int)SocketError.HostNotFound);
        }

        if (addresses.Length == 1)
        {
            return await OpenAsync(addresses[0], port, ct);
        }

        return await RaceAsync(Interleave(addresses), port, ct);
    }

    /// <summary>
    /// Alternates address families, so a host that returns eight IPv4 addresses
    /// before its IPv6 one still races the families against each other rather
    /// than working through one family first.
    /// </summary>
    private static IPAddress[] Interleave(IPAddress[] addresses)
    {
        var v6 = addresses.Where(a => a.AddressFamily == AddressFamily.InterNetworkV6).ToArray();
        var v4 = addresses.Where(a => a.AddressFamily == AddressFamily.InterNetwork).ToArray();

        var ordered = new List<IPAddress>(addresses.Length);

        for (var i = 0; i < Math.Max(v6.Length, v4.Length); i++)
        {
            // IPv6 first, per RFC 8305 — the stagger is what protects us when it
            // is the broken one.
            if (i < v6.Length) ordered.Add(v6[i]);
            if (i < v4.Length) ordered.Add(v4[i]);
        }

        return [.. ordered];
    }

    private static async ValueTask<Stream> RaceAsync(IPAddress[] addresses, int port, CancellationToken ct)
    {
        using var winner = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var attempts = new List<Task<Stream>>(addresses.Length);

        for (var i = 0; i < addresses.Length; i++)
        {
            var address = addresses[i];
            var delay = AttemptStagger * i;

            attempts.Add(Task.Run(async () =>
            {
                if (delay > TimeSpan.Zero) await Task.Delay(delay, winner.Token);
                return await OpenAsync(address, port, winner.Token);
            }, winner.Token));
        }

        var pending = new List<Task<Stream>>(attempts);
        Exception? lastError = null;

        while (pending.Count > 0)
        {
            var finished = await Task.WhenAny(pending);
            pending.Remove(finished);

            if (finished.IsCompletedSuccessfully)
            {
                // Stop the losers, then drain them so their sockets are closed
                // rather than left dangling on a background thread.
                await winner.CancelAsync();
                _ = DiscardAsync(pending);

                return finished.Result;
            }

            lastError = finished.Exception?.GetBaseException() ?? lastError;
        }

        ct.ThrowIfCancellationRequested();
        throw lastError ?? new SocketException((int)SocketError.HostUnreachable);
    }

    /// <summary>Disposes any connection that won the race too late to be used.</summary>
    private static async Task DiscardAsync(IEnumerable<Task<Stream>> losers)
    {
        foreach (var loser in losers)
        {
            try
            {
                await using var stream = await loser;
            }
            catch
            {
                // Cancelled or failed — nothing to clean up.
            }
        }
    }

    private static async ValueTask<Stream> OpenAsync(IPAddress address, int port, CancellationToken ct)
    {
        // Family-matched, never dual-mode: that is the whole point.
        var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
        {
            // Latency matters more than packet efficiency for small API calls.
            NoDelay = true,
        };

        try
        {
            await socket.ConnectAsync(new IPEndPoint(address, port), ct);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}

/// <summary>
/// An HTTP 429 whose <c>Retry-After</c> the caller read before disposing the
/// response. Carrying the wait time on the exception is what lets
/// <see cref="ScraperRetry.ExecuteAsync{T}"/> honour the server's own stated
/// cooldown instead of guessing one.
/// </summary>
public sealed class RateLimitedException(string message, TimeSpan? retryAfter)
    : HttpRequestException(message, null, HttpStatusCode.TooManyRequests)
{
    public TimeSpan? RetryAfter { get; } = retryAfter;
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
    /// <para>
    /// When <paramref name="rateLimiter"/> is supplied and the failure is a
    /// <see cref="RateLimitedException"/>, the limiter is told to slow itself
    /// down for every future call on this run — not just this one retry — and a
    /// server-stated <c>Retry-After</c> wins over the guessed jitter delay.
    /// </para>
    /// </summary>
    public static async Task<T> ExecuteAsync<T>(
        Func<int, Task<T>> action,
        int attempts,
        ILogger? logger = null,
        CancellationToken ct = default,
        RateLimiter? rateLimiter = null)
    {
        Exception? lastError = null;

        for (var attempt = 1; attempt <= Math.Max(1, attempts); attempt++)
        {
            try
            {
                var result = await action(attempt);

                // A success after an earlier penalty is evidence the wall has
                // eased; let the limiter start easing off too.
                if (attempt > 1 && rateLimiter is not null) await rateLimiter.NotifySuccessAsync(ct);

                return result;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (IsTransient(ex))
            {
                lastError = ex;

                TimeSpan delay;

                if (ex is RateLimitedException rateLimited)
                {
                    if (rateLimiter is not null) await rateLimiter.NotifyRateLimitedAsync(rateLimited.RetryAfter, ct);
                    delay = rateLimited.RetryAfter ?? JitterDelay(attempt);
                }
                else
                {
                    delay = JitterDelay(attempt);
                }

                if (attempt >= attempts) break;

                logger?.LogDebug(
                    "Transient failure ({Message}); retrying in {Delay}ms",
                    ex.Message, delay.TotalMilliseconds);

                await Task.Delay(delay, ct);
            }
        }

        throw lastError ?? new InvalidOperationException("Retry failed without an exception.");
    }

    /// <summary>Exponential backoff with full jitter, capped at 15 seconds.</summary>
    public static TimeSpan JitterDelay(int attempt)
    {
        var ceiling = Math.Min(15000, 500 * Math.Pow(2, attempt - 1));
        return TimeSpan.FromMilliseconds(Random.Shared.NextDouble() * ceiling);
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

    /// <summary>
    /// Reads <c>Retry-After</c> as either a delta-seconds or an HTTP-date value,
    /// per RFC 9110 §10.2.3. Null when the header is absent or unparseable —
    /// both are handled by falling back to a guessed delay.
    /// </summary>
    public static TimeSpan? ParseRetryAfter(HttpResponseMessage response)
    {
        var header = response.Headers.RetryAfter;
        if (header is null) return null;

        if (header.Delta is { } delta) return delta;
        if (header.Date is { } date) return date - DateTimeOffset.UtcNow;

        return null;
    }
}

/// <summary>
/// Spaces outbound calls so a run stays inside a provider's quota.
/// <para>
/// Instance-per-run: two concurrent searches each get their own budget, which is
/// the behaviour a per-provider quota actually implies.
/// </para>
/// <para>
/// Adaptive: a 429 slows the limiter down beyond its configured rate, because a
/// server that already said "too many" is telling us the configured rate is
/// wrong for this window, not that we got unlucky once. A run that keeps
/// succeeding gradually eases the penalty back off, so one bad patch does not
/// throttle the rest of the run at the slowest rate it ever saw.
/// </para>
/// </summary>
public sealed class RateLimiter(int requestsPerMinute)
{
    private readonly TimeSpan _minInterval =
        TimeSpan.FromMilliseconds(60_000d / Math.Max(1, requestsPerMinute));

    /// <summary>Ceiling on how much slower than configured the limiter will go.</summary>
    private const double MaxPenaltyMultiplier = 8.0;

    /// <summary>How much a success eases the penalty back toward 1x.</summary>
    private const double PenaltyDecay = 0.85;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTimeOffset _nextSlot = DateTimeOffset.MinValue;
    private double _penaltyMultiplier = 1.0;

    public async Task WaitAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);

        TimeSpan delay;
        try
        {
            var now = DateTimeOffset.UtcNow;
            var runAt = now > _nextSlot ? now : _nextSlot;
            delay = runAt - now;
            _nextSlot = runAt + ScaledInterval();
        }
        finally
        {
            _gate.Release();
        }

        if (delay > TimeSpan.Zero) await Task.Delay(delay, ct);
    }

    /// <summary>
    /// Reports a 429 so every subsequent call in this run backs off, not just
    /// the one being retried. A server-stated <paramref name="retryAfter"/> is
    /// better evidence than a guess, so it wins when the next slot is computed.
    /// </summary>
    public async Task NotifyRateLimitedAsync(TimeSpan? retryAfter, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            _penaltyMultiplier = Math.Min(MaxPenaltyMultiplier, _penaltyMultiplier * 2);

            var now = DateTimeOffset.UtcNow;
            var candidate = now + (retryAfter ?? ScaledInterval());
            if (candidate > _nextSlot) _nextSlot = candidate;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Eases a standing penalty after a call succeeds.</summary>
    public async Task NotifySuccessAsync(CancellationToken ct = default)
    {
        if (_penaltyMultiplier <= 1.0) return;

        await _gate.WaitAsync(ct);
        try
        {
            _penaltyMultiplier = Math.Max(1.0, _penaltyMultiplier * PenaltyDecay);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Caller must already hold <see cref="_gate"/>.</summary>
    private TimeSpan ScaledInterval() =>
        TimeSpan.FromMilliseconds(_minInterval.TotalMilliseconds * _penaltyMultiplier);
}
