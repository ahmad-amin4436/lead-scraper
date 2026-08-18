using System.Reflection;

namespace LeadMine.Infrastructure.Scraping.Verification;

/// <summary>
/// ~8,300 known disposable/throwaway email domains, embedded from the
/// <c>disposable-email-domains</c> open-source list
/// (Resources/disposable-domains.txt) rather than hand-maintained — that
/// project tracks new throwaway-mail services far better than a short inline
/// list ever would. Loaded once, lazily, into a case-insensitive set.
/// </summary>
public static class DisposableDomainList
{
    private static readonly Lazy<HashSet<string>> Domains = new(Load);

    public static bool Contains(string domain) => Domains.Value.Contains(domain);

    public static int Count => Domains.Value.Count;

    private static HashSet<string> Load()
    {
        var assembly = typeof(DisposableDomainList).Assembly;
        const string resourceName = "LeadMine.Infrastructure.Scraping.Verification.Resources.disposable-domains.txt";

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"Embedded resource '{resourceName}' was not found — check the EmbeddedResource entry in LeadMine.Infrastructure.csproj.");

        using var reader = new StreamReader(stream);

        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            var domain = line.Trim();
            if (domain.Length > 0 && !domain.StartsWith('#')) set.Add(domain);
        }

        return set;
    }
}
