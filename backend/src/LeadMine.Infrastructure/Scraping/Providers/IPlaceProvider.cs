using LeadMine.Application.DTOs;
using LeadMine.Domain.Enums;

namespace LeadMine.Infrastructure.Scraping.Providers;

public sealed record ProviderQuery(
    string CategoryId,
    string CategoryLabel,
    ResolvedLocation Location,
    int RadiusMeters,
    int MaxResults);

public sealed record ProviderContext(
    ScraperOptions Options,
    RateLimiter RateLimiter,
    string? GoogleApiKey);

/// <summary>A source of business listings.</summary>
public interface IPlaceProvider
{
    BusinessSource Source { get; }

    string Label { get; }

    /// <summary>Null when ready; otherwise why it cannot run.</summary>
    string? Readiness(ProviderContext context);

    Task<IReadOnlyList<ProviderBusiness>> SearchAsync(
        ProviderQuery query,
        ProviderContext context,
        CancellationToken ct = default);
}

/// <summary>Turns a city into coordinates.</summary>
public interface IGeocoder
{
    Task<ResolvedLocation?> ResolveAsync(
        string country,
        string? state,
        string city,
        ProviderContext context,
        CancellationToken ct = default);
}

public static class ProviderHelpers
{
    /// <summary>Google Maps deep link built from a name and address.</summary>
    public static string BuildMapsSearchUrl(string name, string address)
    {
        var query = string.Join(", ", new[] { name, address }.Where(s => !string.IsNullOrWhiteSpace(s)));
        return $"https://www.google.com/maps/search/?api=1&query={Uri.EscapeDataString(query)}";
    }

    /// <summary>Absolute http(s) URL, or empty when the value is unusable.</summary>
    public static string NormalizeUrl(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;

        var value = raw.Trim();
        if (value.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("tel:", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        if (!value.Contains("://", StringComparison.Ordinal)) value = $"https://{value}";

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)) return string.Empty;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return string.Empty;

        return uri.ToString();
    }
}
