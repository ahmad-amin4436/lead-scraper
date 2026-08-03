using System.Text.RegularExpressions;
using LeadMine.Domain.Enums;
using PhoneNumbers;

namespace LeadMine.Infrastructure.Scraping.Verification;

public sealed record WhatsAppAssessment(WhatsAppStatus Status, string Link, string Reason)
{
    public static readonly WhatsAppAssessment None = new(WhatsAppStatus.None, string.Empty, string.Empty);
}

/// <summary>
/// WhatsApp reachability.
/// <para>
/// There is no legitimate way to test whether an arbitrary number is registered
/// on WhatsApp. The Business API only answers for numbers you own, and the
/// unofficial endpoints that claim to do it violate WhatsApp's terms and get
/// numbers banned. So this classifier never asserts registration — it reports
/// the strongest evidence available:
/// </para>
/// <list type="bullet">
/// <item><description><b>Confirmed</b> — the business published a WhatsApp link on its own website. Direct evidence, and the only reliable signal.</description></item>
/// <item><description><b>Likely</b> — the number is a valid mobile line. Mobile numbers carry the overwhelming majority of WhatsApp accounts, but this is an inference, not a check.</description></item>
/// <item><description><b>Unlikely</b> — the number is a landline or otherwise not a mobile line.</description></item>
/// <item><description><b>None</b> — no usable phone number on the record.</description></item>
/// </list>
/// <para>
/// Line type comes from libphonenumber's full metadata, so it is accurate
/// per-country rather than guessed from prefixes.
/// </para>
/// </summary>
public static partial class WhatsAppClassifier
{
    private static readonly PhoneNumberUtil PhoneUtil = PhoneNumberUtil.GetInstance();

    [GeneratedRegex(@"^[A-Za-z]{2}$", RegexOptions.None, 500)]
    private static partial Regex TwoLetterCode();

    [GeneratedRegex(@"\D", RegexOptions.None, 500)]
    private static partial Regex NonDigit();

    /// <summary>Builds a wa.me link from an E.164 number.</summary>
    public static string BuildWaMeLink(string e164)
    {
        var digits = NonDigit().Replace(e164 ?? string.Empty, string.Empty);
        return digits.Length >= 7 ? $"https://wa.me/{digits}" : string.Empty;
    }

    /// <param name="phone">Phone number as stored on the record.</param>
    /// <param name="countryCode">ISO 3166 alpha-2, used to parse national-format numbers.</param>
    /// <param name="confirmedLink">WhatsApp URL discovered on the business website, if any.</param>
    public static WhatsAppAssessment Assess(string? phone, string? countryCode, string? confirmedLink)
    {
        // A link the business published itself beats any inference we could make.
        if (!string.IsNullOrWhiteSpace(confirmedLink))
        {
            return new WhatsAppAssessment(
                WhatsAppStatus.Confirmed,
                confirmedLink,
                "WhatsApp link published on the business website");
        }

        var raw = (phone ?? string.Empty).Trim();
        if (raw.Length == 0) return WhatsAppAssessment.None;

        var region = TwoLetterCode().IsMatch(countryCode ?? string.Empty)
            ? countryCode!.ToUpperInvariant()
            : null;

        PhoneNumber parsed;

        try
        {
            parsed = PhoneUtil.Parse(raw, region);
        }
        catch (NumberParseException)
        {
            return new WhatsAppAssessment(WhatsAppStatus.Unlikely, string.Empty, "Phone number could not be validated");
        }

        if (!PhoneUtil.IsValidNumber(parsed))
        {
            return new WhatsAppAssessment(WhatsAppStatus.Unlikely, string.Empty, "Phone number could not be validated");
        }

        var type = PhoneUtil.GetNumberType(parsed);
        var link = BuildWaMeLink(PhoneUtil.Format(parsed, PhoneNumberFormat.E164));

        return type switch
        {
            PhoneNumberType.MOBILE =>
                new WhatsAppAssessment(WhatsAppStatus.Likely, link, "Mobile number — WhatsApp likely but unverified"),

            // Covers countries (notably the US) where the numbering plan makes
            // mobile and landline indistinguishable — treat as possible.
            PhoneNumberType.FIXED_LINE_OR_MOBILE =>
                new WhatsAppAssessment(WhatsAppStatus.Likely, link, "Mobile or landline — WhatsApp possible but unverified"),

            PhoneNumberType.UNKNOWN =>
                new WhatsAppAssessment(WhatsAppStatus.Unlikely, string.Empty, "Line type unknown for this number"),

            _ => new WhatsAppAssessment(
                WhatsAppStatus.Unlikely,
                string.Empty,
                $"Line type is {type.ToString().ToLowerInvariant().Replace('_', ' ')}"),
        };
    }
}
