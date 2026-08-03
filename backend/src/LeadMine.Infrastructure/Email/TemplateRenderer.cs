using System.Net;
using System.Text.RegularExpressions;
using LeadMine.Domain.Entities;

namespace LeadMine.Infrastructure.Email;

/// <summary>
/// Substitutes <c>{{placeholder}}</c> tokens in a preset from the recipient lead.
/// <para>
/// Deliberately not a general template engine. Presets are authored by admins
/// and rendered server-side into mail sent on the organisation's behalf, so the
/// substitution set is a fixed, known list — there is no expression evaluation
/// for a malicious template to abuse.
/// </para>
/// </summary>
public static partial class TemplateRenderer
{
    /// <summary>Tokens an admin may use, shown in the preset editor.</summary>
    public static readonly IReadOnlyList<(string Token, string Description)> AvailableTokens =
    [
        ("{{business_name}}", "The lead's business name"),
        ("{{category}}", "Business category"),
        ("{{city}}", "City"),
        ("{{state}}", "State or region"),
        ("{{country}}", "Country"),
        ("{{address}}", "Street address"),
        ("{{phone}}", "Phone number"),
        ("{{email}}", "Email address"),
        ("{{website}}", "Website URL"),
        ("{{sender_name}}", "Name of the user sending"),
        ("{{sender_email}}", "Email of the user sending"),
    ];

    public static string Render(
        string template,
        Business? lead,
        string senderName,
        string senderEmail,
        bool htmlEncode)
    {
        if (string.IsNullOrWhiteSpace(template)) return string.Empty;

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["business_name"] = lead?.Name ?? string.Empty,
            ["category"] = lead?.Category ?? string.Empty,
            ["city"] = lead?.City ?? string.Empty,
            ["state"] = lead?.State ?? string.Empty,
            ["country"] = lead?.Country ?? string.Empty,
            ["address"] = lead?.Address ?? string.Empty,
            ["phone"] = lead?.Phone ?? string.Empty,
            ["email"] = lead?.Email ?? string.Empty,
            ["website"] = lead?.Website ?? string.Empty,
            ["sender_name"] = senderName,
            ["sender_email"] = senderEmail,
        };

        return PlaceholderPattern().Replace(template, match =>
        {
            var key = match.Groups[1].Value.Trim();

            if (!values.TryGetValue(key, out var value))
            {
                // Leave unknown tokens intact rather than blanking them — a typo
                // in a preset should be obvious in the preview, not invisible.
                return match.Value;
            }

            // Lead data is third-party text. Encoding it stops a business name
            // containing markup from breaking or injecting into the HTML body.
            return htmlEncode ? WebUtility.HtmlEncode(value) : value;
        });
    }

    /// <summary>Appends a signature block to a rendered HTML body.</summary>
    public static string AppendSignature(string bodyHtml, string? signatureHtml)
    {
        if (string.IsNullOrWhiteSpace(signatureHtml)) return bodyHtml;

        return $"{bodyHtml}<br /><div class=\"leadmine-signature\">{signatureHtml}</div>";
    }

    public static string AppendSignatureText(string bodyText, string? signatureText)
    {
        if (string.IsNullOrWhiteSpace(signatureText)) return bodyText;
        return $"{bodyText}\n\n--\n{signatureText}";
    }

    [GeneratedRegex(@"\{\{\s*([a-zA-Z0-9_]+)\s*\}\}", RegexOptions.Compiled)]
    private static partial Regex PlaceholderPattern();
}
