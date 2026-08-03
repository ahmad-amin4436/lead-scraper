using System.Net;
using System.Net.Sockets;

namespace LeadMine.Infrastructure.Scraping;

public sealed record CrawlTargetCheck(bool Safe, string? Reason)
{
    public static readonly CrawlTargetCheck Allowed = new(true, null);
}

/// <summary>
/// SSRF guard for the enrichment crawler.
/// <para>
/// Website URLs come from third-party search providers, so they are untrusted
/// input. Before fetching, the hostname is resolved and every returned address
/// checked against private, loopback, link-local and reserved ranges — otherwise
/// a crafted listing could point this server at its own internal network or a
/// cloud metadata endpoint.
/// </para>
/// <para>
/// This matters more here than it did in the standalone worker: the crawler now
/// runs inside the API process, which sits on the same network as the database.
/// </para>
/// </summary>
public static class UrlGuard
{
    private static readonly string[] BlockedSuffixes =
        [".local", ".localhost", ".internal", ".home.arpa", ".onion"];

    public static async Task<CrawlTargetCheck> CheckAsync(string url, CancellationToken ct = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return new CrawlTargetCheck(false, "Invalid website URL");
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            return new CrawlTargetCheck(false, "Website URL is not HTTP or HTTPS");
        }

        // Credentials in a URL signal redirect abuse, not a business homepage.
        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            return new CrawlTargetCheck(false, "Website URL contains embedded credentials");
        }

        return await CheckHostAsync(uri.Host, ct);
    }

    private static async Task<CrawlTargetCheck> CheckHostAsync(string host, CancellationToken ct)
    {
        var name = host.ToLowerInvariant().TrimEnd('.');

        if (string.IsNullOrWhiteSpace(name) || name == "localhost")
        {
            return new CrawlTargetCheck(false, "Host is not publicly routable");
        }

        if (BlockedSuffixes.Any(suffix => name.EndsWith(suffix, StringComparison.Ordinal)))
        {
            return new CrawlTargetCheck(false, "Host uses an internal-only domain suffix");
        }

        // A literal IP skips DNS entirely.
        if (IPAddress.TryParse(name, out var literal))
        {
            return IsPrivate(literal)
                ? new CrawlTargetCheck(false, "Host is a private or reserved IP address")
                : CrawlTargetCheck.Allowed;
        }

        IPAddress[] addresses;
        try
        {
            addresses = await Dns.GetHostAddressesAsync(name, ct);
        }
        catch (SocketException ex)
        {
            // A dead domain is different from a blocked one, and the distinction
            // is what the operator needs to act on.
            return new CrawlTargetCheck(false, $"Website domain could not be resolved ({ex.SocketErrorCode})");
        }

        if (addresses.Length == 0)
        {
            return new CrawlTargetCheck(false, "Website domain could not be resolved");
        }

        return addresses.Any(IsPrivate)
            ? new CrawlTargetCheck(false, "Host resolves to a private or reserved IP address")
            : CrawlTargetCheck.Allowed;
    }

    public static bool IsPrivate(IPAddress address)
    {
        if (IPAddress.IsLoopback(address)) return true;

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();

            return b[0] switch
            {
                0 => true,                                   // "this network"
                10 => true,                                  // RFC1918
                127 => true,                                 // loopback
                169 when b[1] == 254 => true,                // link-local + cloud metadata
                172 when b[1] >= 16 && b[1] <= 31 => true,   // RFC1918
                192 when b[1] == 168 => true,                // RFC1918
                192 when b[1] == 0 => true,                  // IETF assignments
                100 when b[1] >= 64 && b[1] <= 127 => true,  // CGNAT
                198 when b[1] is 18 or 19 => true,           // benchmarking
                >= 224 => true,                              // multicast + reserved
                _ => false,
            };
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast) return true;

            // IPv4-mapped addresses inherit the IPv4 rules.
            if (address.IsIPv4MappedToIPv6) return IsPrivate(address.MapToIPv4());

            var bytes = address.GetAddressBytes();

            // fc00::/7 unique local
            if ((bytes[0] & 0xFE) == 0xFC) return true;

            // ::
            if (bytes.All(b => b == 0)) return true;
        }

        return false;
    }
}
