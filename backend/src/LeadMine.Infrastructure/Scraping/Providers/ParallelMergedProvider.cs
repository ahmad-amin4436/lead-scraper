using System.Text.RegularExpressions;
using LeadMine.Application.DTOs;
using LeadMine.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace LeadMine.Infrastructure.Scraping.Providers;

/// <summary>
/// Runs a preferred provider and a secondary one at the same time and merges
/// their results, rather than picking one.
/// <para>
/// This is the whole "parallel" mechanism: every task calls
/// <see cref="Task.WhenAll(Task,Task)"/> on the two sources and never runs one
/// after the other. It implements <see cref="IPlaceProvider"/> like any other
/// source, so <c>SearchRunner</c>'s task loop, checkpointing, quota-fallback and
/// enrichment/verification pipeline need no changes at all — they simply call
/// <see cref="SearchAsync"/> on what looks like a single provider.
/// </para>
/// <para>
/// A failure in one source does not fail the task: whichever source succeeded
/// is still worth keeping. Only a failure in <em>both</em> propagates.
/// </para>
/// </summary>
public sealed partial class ParallelMergedProvider(
    IPlaceProvider preferred,
    IPlaceProvider secondary,
    ILogger<ParallelMergedProvider> logger) : IPlaceProvider
{
    public BusinessSource Source => preferred.Source;

    public string Label => $"{preferred.Label} + {secondary.Label} (parallel)";

    /// <summary>
    /// The secondary source (OpenStreetMap) needs no key, so the preferred
    /// source's readiness is what actually gates this mode — asking for the
    /// parallel sweep without the preferred source configured is a
    /// configuration error, not a silent downgrade to one source.
    /// </summary>
    public string? Readiness(ProviderContext context) => preferred.Readiness(context);

    public async Task<IReadOnlyList<ProviderBusiness>> SearchAsync(
        ProviderQuery query,
        ProviderContext context,
        CancellationToken ct = default)
    {
        var notReady = Readiness(context);
        if (notReady is not null) throw new ProviderException(notReady, ProviderFailure.MissingApiKey);

        var preferredTask = RunAsync(preferred, query, context, ct);
        var secondaryTask = RunAsync(secondary, query, context, ct);

        await Task.WhenAll(preferredTask, secondaryTask);

        var (preferredResult, preferredError) = preferredTask.Result;
        var (secondaryResult, secondaryError) = secondaryTask.Result;

        if (preferredResult is null && secondaryResult is null)
        {
            // Both failed — surface the preferred source's error since that is
            // the one the caller actually configured this mode for.
            throw preferredError ?? secondaryError ?? new ProviderException("Both parallel sources failed.");
        }

        return MergeResults.Merge(preferredResult ?? [], secondaryResult ?? []);
    }

    /// <summary>
    /// Runs one branch of the sweep, turning a failure into a null result
    /// rather than letting it fault the other branch's <c>Task.WhenAll</c>.
    /// Returned locally rather than through instance state, since this class
    /// is not guaranteed to be constructed fresh per call.
    /// </summary>
    private async Task<(IReadOnlyList<ProviderBusiness>? Result, ProviderException? Error)> RunAsync(
        IPlaceProvider provider,
        ProviderQuery query,
        ProviderContext context,
        CancellationToken ct)
    {
        try
        {
            return (await provider.SearchAsync(query, context, ct), null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (ProviderException ex)
        {
            logger.LogWarning(ex, "{Provider} failed during a parallel sweep; continuing with the other source", provider.Label);
            return (null, ex);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "{Provider} failed during a parallel sweep; continuing with the other source", provider.Label);
            return (null, new ProviderException(ex.Message, ProviderFailure.Transient, ex));
        }
    }
}

/// <summary>
/// Merges two providers' results into one list: matching businesses are
/// combined into a single record (preferring the first list's field values,
/// borrowing from the second wherever the first is blank), and anything that
/// only appears in one list is kept as-is.
/// </summary>
public static partial class MergeResults
{
    /// <summary>Businesses closer than this are candidates for the same place.</summary>
    private const double MaxMatchDistanceMeters = 75;

    public static IReadOnlyList<ProviderBusiness> Merge(
        IReadOnlyList<ProviderBusiness> preferred,
        IReadOnlyList<ProviderBusiness> secondary)
    {
        if (secondary.Count == 0) return preferred;
        if (preferred.Count == 0) return secondary;

        var merged = new List<ProviderBusiness>(preferred.Count + secondary.Count);
        var claimed = new bool[secondary.Count];

        foreach (var candidate in preferred)
        {
            var matchIndex = -1;

            for (var i = 0; i < secondary.Count; i++)
            {
                if (claimed[i]) continue;
                if (!IsSameBusiness(candidate, secondary[i])) continue;

                matchIndex = i;
                break;
            }

            merged.Add(matchIndex < 0 ? candidate : FillGaps(candidate, secondary[matchIndex]));
            if (matchIndex >= 0) claimed[matchIndex] = true;
        }

        for (var i = 0; i < secondary.Count; i++)
        {
            if (!claimed[i]) merged.Add(secondary[i]);
        }

        return merged;
    }

    /// <summary>
    /// True when two candidates are plausibly the same real-world business.
    /// <para>
    /// Any one strong signal (matching website host, matching phone, or the
    /// same normalized name within the same city) is enough. Coordinates alone
    /// are not — two different businesses in the same plaza can sit meters
    /// apart — so proximity only counts combined with a name that overlaps.
    /// </para>
    /// </summary>
    private static bool IsSameBusiness(ProviderBusiness a, ProviderBusiness b)
    {
        var hostA = NormalizeHost(a.Website);
        var hostB = NormalizeHost(b.Website);
        if (hostA is not null && hostA == hostB) return true;

        var phoneA = NormalizePhone(a.Phone);
        var phoneB = NormalizePhone(b.Phone);
        if (phoneA is not null && phoneA == phoneB) return true;

        var nameA = NormalizeName(a.Name);
        var nameB = NormalizeName(b.Name);
        var cityA = NormalizeName(a.City);
        var cityB = NormalizeName(b.City);

        if (nameA.Length > 0 && nameA == nameB && cityA == cityB) return true;

        if (a.Latitude is { } latA && a.Longitude is { } lngA &&
            b.Latitude is { } latB && b.Longitude is { } lngB &&
            DistanceMeters(latA, lngA, latB, lngB) <= MaxMatchDistanceMeters &&
            nameA.Length > 0 && (nameA.Contains(nameB, StringComparison.Ordinal) || nameB.Contains(nameA, StringComparison.Ordinal)))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// One merged record: <paramref name="primary"/>'s values win field by
    /// field, and a blank field borrows from <paramref name="fallback"/>.
    /// </summary>
    private static ProviderBusiness FillGaps(ProviderBusiness primary, ProviderBusiness fallback) => new()
    {
        ExternalId = primary.ExternalId,
        Name = Pick(primary.Name, fallback.Name),
        Category = Pick(primary.Category, fallback.Category),
        Address = Pick(primary.Address, fallback.Address),
        Country = Pick(primary.Country, fallback.Country),
        State = Pick(primary.State, fallback.State),
        City = Pick(primary.City, fallback.City),
        Phone = Pick(primary.Phone, fallback.Phone),
        Website = Pick(primary.Website, fallback.Website),
        Latitude = primary.Latitude ?? fallback.Latitude,
        Longitude = primary.Longitude ?? fallback.Longitude,
        Rating = primary.Rating ?? fallback.Rating,
        ReviewCount = primary.ReviewCount ?? fallback.ReviewCount,
        MapsUrl = Pick(primary.MapsUrl, fallback.MapsUrl),
        Source = primary.Source,
    };

    private static string Pick(string primary, string fallback) =>
        string.IsNullOrWhiteSpace(primary) ? fallback : primary;

    private static string? NormalizeHost(string? website)
    {
        if (string.IsNullOrWhiteSpace(website)) return null;

        var value = website.Trim();
        if (!value.Contains("://", StringComparison.Ordinal)) value = $"https://{value}";
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)) return null;

        var host = uri.Host.ToLowerInvariant();
        return host.StartsWith("www.", StringComparison.Ordinal) ? host[4..] : host;
    }

    private static string? NormalizePhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return null;

        var digits = NonDigit().Replace(phone, string.Empty);
        return digits.Length >= 7 ? digits[Math.Max(0, digits.Length - 10)..] : null;
    }

    private static string NormalizeName(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : NonAlphanumeric().Replace(value.ToLowerInvariant(), " ").Trim();

    /// <summary>Haversine distance in metres.</summary>
    private static double DistanceMeters(double lat1, double lng1, double lat2, double lng2)
    {
        const double earthRadiusMeters = 6_371_000;

        var dLat = ToRadians(lat2 - lat1);
        var dLng = ToRadians(lng2 - lng1);

        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) *
                Math.Sin(dLng / 2) * Math.Sin(dLng / 2);

        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return earthRadiusMeters * c;
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180;

    [GeneratedRegex(@"\D")]
    private static partial Regex NonDigit();

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonAlphanumeric();
}
