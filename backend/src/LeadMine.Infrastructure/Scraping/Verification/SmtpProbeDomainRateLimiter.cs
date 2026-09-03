using System.Collections.Concurrent;

namespace LeadMine.Infrastructure.Scraping.Verification;

/// <summary>
/// The actual enforcement of <see cref="EmailValidationOptions.SmtpProbeMaxPerHourPerDomain"/> —
/// caps RCPT TO probes against any one mail domain to a rolling hour, so a
/// batch drawn from real leads that happens to share a domain (a franchise
/// chain, several leads on the same Google Workspace tenant, ...) can't
/// hammer that one mail server. In-memory and process-local by design: this
/// is a politeness guard, not a security boundary, and resetting it on
/// restart is an acceptable trade for not needing a database round trip on
/// every probe attempt.
/// </summary>
public sealed class SmtpProbeDomainRateLimiter
{
    private readonly ConcurrentDictionary<string, ConcurrentQueue<DateTimeOffset>> _recentByDomain =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// True and records the attempt if this domain is still under its hourly
    /// cap; false (and records nothing) if it isn't — the caller should skip
    /// this lead for now rather than probe it.
    /// </summary>
    public bool TryAcquire(string domain, int maxPerHour)
    {
        var now = DateTimeOffset.UtcNow;
        var cutoff = now.AddHours(-1);
        var queue = _recentByDomain.GetOrAdd(domain, _ => new ConcurrentQueue<DateTimeOffset>());

        // Drop anything that's aged out of the window before counting.
        while (queue.TryPeek(out var oldest) && oldest < cutoff)
        {
            queue.TryDequeue(out _);
        }

        if (queue.Count >= maxPerHour) return false;

        queue.Enqueue(now);
        return true;
    }
}
