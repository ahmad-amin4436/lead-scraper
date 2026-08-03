using System.Collections.Concurrent;
using System.Globalization;

namespace LeadMine.Infrastructure.Scraping;

/// <summary>
/// ISO 3166-1 alpha-2 codes to English country names.
/// <para>
/// The front end sends codes ("PK", "GB"); geocoders want names. Rather than
/// carry a 250-row table that has to be maintained alongside the front end's,
/// this reads <see cref="RegionInfo"/>, which ships with the framework and is
/// already the authority the rest of .NET uses. Anything unrecognised passes
/// through unchanged, so a free-text country still searches.
/// </para>
/// </summary>
public static class LocationNames
{
    private static readonly ConcurrentDictionary<string, string> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static string CountryName(string countryOrCode)
    {
        if (string.IsNullOrWhiteSpace(countryOrCode)) return string.Empty;

        var value = countryOrCode.Trim();

        // Only two-letter values are codes; a longer string is already a name.
        if (value.Length != 2) return value;

        return Cache.GetOrAdd(value, static code =>
        {
            try
            {
                return new RegionInfo(code).EnglishName;
            }
            catch (ArgumentException)
            {
                return code;
            }
        });
    }
}
