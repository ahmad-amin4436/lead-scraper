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
