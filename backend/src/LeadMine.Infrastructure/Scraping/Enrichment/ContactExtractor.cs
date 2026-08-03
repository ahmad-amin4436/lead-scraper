using System.Text.Json;
using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace LeadMine.Infrastructure.Scraping.Enrichment;

public enum SocialKey
{
    Facebook,
    Instagram,
    LinkedIn,
    Twitter,
    YouTube,
    WhatsApp,
}

/// <summary>Everything one page yielded.</summary>
public sealed class PageHarvest
{
    public List<string> Emails { get; } = [];
    public List<string> Phones { get; } = [];
    public Dictionary<SocialKey, string> Social { get; } = [];
    public string? ContactFormUrl { get; set; }
    public List<string> ContactLinks { get; } = [];
}

/// <summary>
/// Pulls publicly listed contact details out of a page's HTML.
/// <para>
/// Reads only what the site publishes: mailto/tel links, visible text, JSON-LD
/// blocks and outbound social links. It never submits a form, follows a login,
/// or reads anything that is not already served to an anonymous visitor.
/// </para>
/// </summary>
public static partial class ContactExtractor
{
    /// <summary>Placeholder and third-party-service domains that are never real leads.</summary>
    private static readonly HashSet<string> BlockedDomains = new(StringComparer.OrdinalIgnoreCase)
    {
        "example.com", "example.org", "example.net", "domain.com", "yourdomain.com",
        "email.com", "yoursite.com", "mysite.com", "test.com", "company.com",
        "sentry.io", "sentry-next.wixpress.com", "wixpress.com", "wix.com",
        "squarespace.com", "godaddy.com", "shopify.com", "cloudflare.com",
        "schema.org", "w3.org", "googlemail.com", "jquery.com", "fontawesome.com",
    };

    private static readonly HashSet<string> BlockedLocalParts = new(StringComparer.OrdinalIgnoreCase)
    {
        "you", "your", "name", "email", "user", "username", "someone", "somebody",
        "firstname", "lastname", "test", "example", "sample", "noreply", "no-reply",
        "donotreply", "do-not-reply", "wordpress", "sentry",
    };

    /// <summary>Mailbox prefixes worth promoting to the record's primary email, best first.</summary>
    private static readonly string[] PreferredPrefixes =
    [
        "sales", "enquiries", "enquiry", "inquiries", "inquiry", "contact",
        "hello", "hi", "info", "office", "admin", "reception", "bookings",
        "support", "help", "team", "mail",
    ];

    private static readonly string[] ContactHints =
    [
        "contact", "contact-us", "contactus", "get-in-touch", "reach-us", "enquiry",
        "enquiries", "about", "about-us", "aboutus", "imprint", "impressum",
        "kontakt", "contacto", "contatti", "nous-contacter", "legal-notice",
        "support", "help", "team", "locations", "find-us",
    ];

    /// <summary>Social profile roots that carry no business identity.</summary>
    private static readonly HashSet<string> SocialNoise = new(StringComparer.OrdinalIgnoreCase)
    {
        "/", "/home", "/login", "/signup", "/share", "/sharer", "/sharer.php",
        "/share.php", "/intent/tweet", "/sharearticle", "/dialog/feed",
    };

    [GeneratedRegex(
        @"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9](?:[a-zA-Z0-9-]*[a-zA-Z0-9])?(?:\.[a-zA-Z0-9](?:[a-zA-Z0-9-]*[a-zA-Z0-9])?)+",
        RegexOptions.None, 500)]
    private static partial Regex EmailPattern();

    /// <summary>Asset extensions the email regex picks up out of names like <c>logo@2x.png</c>.</summary>
    [GeneratedRegex(
        @"\.(png|jpe?g|gif|svg|webp|avif|ico|css|js|mjs|json|xml|pdf|zip|woff2?|ttf|eot|mp4|webm|mp3)$",
        RegexOptions.IgnoreCase, 500)]
    private static partial Regex FileExtension();

    [GeneratedRegex(@"^[0-9a-f]+$", RegexOptions.IgnoreCase, 500)]
    private static partial Regex HexOnly();

    [GeneratedRegex(@"^[a-z]+$", RegexOptions.IgnoreCase, 500)]
    private static partial Regex LettersOnly();

    [GeneratedRegex(@"\D", RegexOptions.None, 500)]
    private static partial Regex NonDigit();

    [GeneratedRegex(@"^/(company|in|school)/", RegexOptions.IgnoreCase, 500)]
    private static partial Regex LinkedInProfilePath();

    // --- public API ---------------------------------------------------------

    public static PageHarvest Harvest(HtmlDocument document, Uri pageUrl, int maxContactLinks)
    {
        var harvest = new PageHarvest();

        // Structured data first: a JSON-LD `email` is stated by the site owner,
        // so it outranks anything scraped out of the prose.
        var (structuredEmails, structuredPhones) = ExtractStructuredContacts(document);

        foreach (var email in structuredEmails) AddDistinct(harvest.Emails, email);
        foreach (var email in ExtractEmails(document, pageUrl)) AddDistinct(harvest.Emails, email);

        foreach (var phone in structuredPhones) AddDistinct(harvest.Phones, phone);
        foreach (var phone in ExtractPhones(document)) AddDistinct(harvest.Phones, phone);

        foreach (var (key, url) in ExtractSocialLinks(document, pageUrl))
        {
            harvest.Social.TryAdd(key, url);
        }

        harvest.ContactFormUrl = FindContactFormUrl(document, pageUrl);
        harvest.ContactLinks.AddRange(FindContactPageLinks(document, pageUrl, maxContactLinks));

        return harvest;
    }

    public static List<string> ExtractEmails(HtmlDocument document, Uri pageUrl)
    {
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var node in SelectNodes(document, "//a[@href]"))
        {
            var href = node.GetAttributeValue("href", string.Empty);
            if (!href.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)) continue;

            var address = href["mailto:".Length..].Split('?')[0].Trim();
            if (address.Length == 0) continue;

            foreach (var part in address.Split(','))
            {
                var email = Unescape(part).Trim().ToLowerInvariant();
                if (IsValidEmail(email)) found.Add(email);
            }
        }

        foreach (Match match in EmailPattern().Matches(VisibleText(document)))
        {
            var email = match.Value.ToLowerInvariant();
            if (IsValidEmail(email)) found.Add(email);
        }

        var siteHost = NormalizeHost(pageUrl.ToString());

        return found
            .OrderByDescending(email => ScoreEmail(email, siteHost))
            .ToList();
    }

    public static List<string> ExtractPhones(HtmlDocument document)
    {
        var found = new List<string>();

        foreach (var node in SelectNodes(document, "//a[@href]"))
        {
            var href = node.GetAttributeValue("href", string.Empty);
            if (!href.StartsWith("tel:", StringComparison.OrdinalIgnoreCase)) continue;

            var phone = NormalizePhone(Unescape(href["tel:".Length..]));

            // Below 7 digits it's an extension or a false positive.
            if (NonDigit().Replace(phone, string.Empty).Length >= 7) AddDistinct(found, phone);
        }

        return found;
    }

    public static Dictionary<SocialKey, string> ExtractSocialLinks(HtmlDocument document, Uri pageUrl)
    {
        var links = new Dictionary<SocialKey, string>();

        foreach (var node in SelectNodes(document, "//a[@href]"))
        {
            var href = node.GetAttributeValue("href", string.Empty);
            if (href.Length == 0) continue;

            if (!TryResolve(href, pageUrl, out var absolute)) continue;

            var host = NormalizeHost(absolute.ToString());
            if (host.Length == 0) continue;

            var key = MatchSocial(host, absolute);
            if (key is null || links.ContainsKey(key.Value)) continue;

            var path = absolute.AbsolutePath.TrimEnd('/');
            if (path.Length == 0) path = "/";

            // Skip share widgets and bare platform homepages.
            if (SocialNoise.Contains(path) || SocialNoise.Contains(absolute.AbsolutePath)) continue;

            // A LinkedIn link that isn't a profile is a share button.
            if (key == SocialKey.LinkedIn && !LinkedInProfilePath().IsMatch(absolute.AbsolutePath)) continue;

            links[key.Value] = absolute.ToString();
        }

        return links;
    }

    private static SocialKey? MatchSocial(string host, Uri url)
    {
        if (host is "facebook.com" or "fb.com" || host.EndsWith(".facebook.com", StringComparison.Ordinal))
        {
            return SocialKey.Facebook;
        }

        if (host == "instagram.com" || host.EndsWith(".instagram.com", StringComparison.Ordinal))
        {
            return SocialKey.Instagram;
        }

        if (host == "linkedin.com" || host.EndsWith(".linkedin.com", StringComparison.Ordinal))
        {
            return SocialKey.LinkedIn;
        }

        if (host is "twitter.com" or "x.com" || host.EndsWith(".twitter.com", StringComparison.Ordinal))
        {
            return SocialKey.Twitter;
        }

        if (host is "youtube.com" or "youtu.be" || host.EndsWith(".youtube.com", StringComparison.Ordinal))
        {
            return SocialKey.YouTube;
        }

        if (host is "wa.me" or "chat.whatsapp.com" or "api.whatsapp.com" ||
            (host.EndsWith("whatsapp.com", StringComparison.Ordinal) &&
             url.ToString().Contains("send", StringComparison.OrdinalIgnoreCase)))
        {
            return SocialKey.WhatsApp;
        }

        return null;
    }

    /// <summary>
    /// Internal pages most likely to carry contact details, ranked by how
    /// strongly the URL and link text suggest a contact page.
    /// </summary>
    public static List<string> FindContactPageLinks(HtmlDocument document, Uri pageUrl, int limit)
    {
        var siteHost = NormalizeHost(pageUrl.ToString());
        var scored = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var node in SelectNodes(document, "//a[@href]"))
        {
            var href = node.GetAttributeValue("href", string.Empty);
            if (href.Length == 0) continue;

            if (!TryResolve(href, pageUrl, out var absolute)) continue;
            if (NormalizeHost(absolute.ToString()) != siteHost) continue;

            var path = absolute.AbsolutePath.ToLowerInvariant();
            if (path == "/" || FileExtension().IsMatch(path)) continue;

            var linkText = HtmlEntity.DeEntitize(node.InnerText ?? string.Empty)
                .Trim()
                .ToLowerInvariant();

            if (linkText.Length > 80) linkText = linkText[..80];

            var score = 0;

            for (var index = 0; index < ContactHints.Length; index++)
            {
                var weight = ContactHints.Length - index;
                var hint = ContactHints[index];

                if (path.Contains(hint, StringComparison.Ordinal)) score += weight * 2;
                if (linkText.Contains(hint.Replace('-', ' '), StringComparison.Ordinal)) score += weight;
            }

            if (score == 0) continue;

            // Prefer shallow URLs: /contact beats /blog/2019/contact-form-tips.
            score -= (path.Split('/', StringSplitOptions.RemoveEmptyEntries).Length - 1) * 3;

            var url = absolute.ToString();
            if (!scored.TryGetValue(url, out var existing) || score > existing) scored[url] = score;
        }

        return scored
            .OrderByDescending(pair => pair.Value)
            .Take(Math.Max(0, limit))
            .Select(pair => pair.Key)
            .ToList();
    }

    /// <summary>Locates a public contact form (one with an email and a message field).</summary>
    public static string? FindContactFormUrl(HtmlDocument document, Uri pageUrl)
    {
        foreach (var form in SelectNodes(document, "//form"))
        {
            var hasEmailField = form.SelectSingleNode(
                ".//input[translate(@type,'EMAIL','email')='email' " +
                "or contains(translate(@name,'EMAIL','email'),'email') " +
                "or contains(translate(@id,'EMAIL','email'),'email')]") is not null;

            var hasMessageField = form.SelectSingleNode(
                ".//textarea | .//input[contains(translate(@name,'MESAGQUIRY','mesagquiry'),'message') " +
                "or contains(translate(@name,'MESAGQUIRY','mesagquiry'),'enquiry')]") is not null;

            // A search box has neither; a login form lacks the message field.
            if (!hasEmailField || !hasMessageField) continue;
            if (form.SelectSingleNode(".//input[translate(@type,'PASWORD','pasword')='password']") is not null) continue;

            var action = form.GetAttributeValue("action", string.Empty);

            if (action.Length > 0 && TryResolve(action, pageUrl, out var resolved))
            {
                return resolved.ToString();
            }

            return pageUrl.ToString();
        }

        return null;
    }

    /// <summary>Reads emails and phones out of JSON-LD blocks.</summary>
    public static (List<string> Emails, List<string> Phones) ExtractStructuredContacts(HtmlDocument document)
    {
        var emails = new List<string>();
        var phones = new List<string>();

        foreach (var node in SelectNodes(document, "//script[@type='application/ld+json']"))
        {
            var raw = HtmlEntity.DeEntitize(node.InnerText ?? string.Empty).Trim();
            if (raw.Length == 0) continue;

            try
            {
                using var json = JsonDocument.Parse(raw);
                Visit(json.RootElement, 0);
            }
            catch (JsonException)
            {
                // Malformed JSON-LD is common; ignore it.
            }
        }

        return (emails, phones);

        void Visit(JsonElement element, int depth)
        {
            if (depth > 6) return;

            switch (element.ValueKind)
            {
                case JsonValueKind.Array:
                    foreach (var item in element.EnumerateArray()) Visit(item, depth + 1);
                    break;

                case JsonValueKind.Object:
                    foreach (var property in element.EnumerateObject())
                    {
                        if (property.Value.ValueKind == JsonValueKind.String)
                        {
                            var name = property.Name.ToLowerInvariant();
                            var value = property.Value.GetString() ?? string.Empty;

                            if (name == "email")
                            {
                                var email = value.Trim().ToLowerInvariant();
                                if (email.StartsWith("mailto:", StringComparison.Ordinal)) email = email[7..];
                                if (IsValidEmail(email)) AddDistinct(emails, email);
                            }
                            else if (name is "telephone" or "phone")
                            {
                                var phone = NormalizePhone(value);
                                if (NonDigit().Replace(phone, string.Empty).Length >= 7) AddDistinct(phones, phone);
                            }
                        }
                        else
                        {
                            Visit(property.Value, depth + 1);
                        }
                    }

                    break;
            }
        }
    }

    /// <summary>Normalises a WhatsApp link to the canonical <c>wa.me/&lt;number&gt;</c> form.</summary>
    public static string CanonicalWhatsApp(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed)) return url;

        // chat.whatsapp.com/<invite> is a group link, not a number — keep as-is.
        if (string.Equals(parsed.Host, "chat.whatsapp.com", StringComparison.OrdinalIgnoreCase)) return url;

        var fromQuery = System.Web.HttpUtility.ParseQueryString(parsed.Query)["phone"];
        var source = fromQuery ?? parsed.AbsolutePath.Replace("/", string.Empty);
        var digits = NonDigit().Replace(source, string.Empty);

        return digits.Length >= 7 ? $"https://wa.me/{digits}" : url;
    }

    // --- helpers ------------------------------------------------------------

    public static bool IsValidEmail(string candidate)
    {
        var email = candidate.ToLowerInvariant();

        if (email.Length is 0 or > 254 || FileExtension().IsMatch(email)) return false;

        var at = email.IndexOf('@');
        if (at <= 0 || at == email.Length - 1) return false;

        var localPart = email[..at];
        var domain = email[(at + 1)..];

        if (domain.Contains('@')) return false;
        if (BlockedDomains.Contains(domain) || BlockedLocalParts.Contains(localPart)) return false;

        // Cache-busting hashes and minified asset names produce long hex parts.
        if (localPart.Length > 24 && HexOnly().IsMatch(localPart)) return false;

        var firstLabel = domain.Split('.')[0];
        if (firstLabel.Length >= 32 && HexOnly().IsMatch(firstLabel)) return false;

        var tld = domain.Split('.').LastOrDefault() ?? string.Empty;
        return tld.Length >= 2 && LettersOnly().IsMatch(tld);
    }

    /// <summary>Ranks candidates so the most contactable mailbox becomes primary.</summary>
    private static int ScoreEmail(string email, string siteHost)
    {
        var at = email.IndexOf('@');
        if (at <= 0) return 0;

        var localPart = email[..at];
        var domain = email[(at + 1)..];
        var score = 0;

        var prefixIndex = Array.FindIndex(
            PreferredPrefixes,
            prefix => localPart == prefix || localPart.StartsWith($"{prefix}.", StringComparison.Ordinal));

        if (prefixIndex >= 0) score += 100 - prefixIndex;

        // An address on the business's own domain beats a gmail.com fallback.
        if (siteHost.Length > 0 &&
            (domain == siteHost || domain.EndsWith($".{siteHost}", StringComparison.Ordinal)))
        {
            score += 40;
        }

        if (localPart.Contains("webmaster", StringComparison.Ordinal) ||
            localPart.Contains("postmaster", StringComparison.Ordinal))
        {
            score -= 20;
        }

        return score;
    }

    /// <summary>Page text with script, style and SVG stripped.</summary>
    private static string VisibleText(HtmlDocument document)
    {
        var root = document.DocumentNode.CloneNode(deep: true);

        var noise = root.SelectNodes(".//script | .//style | .//noscript | .//svg");

        if (noise is not null)
        {
            // Snapshot first: removing while enumerating the live collection skips nodes.
            foreach (var node in noise.ToList()) node.Remove();
        }

        return HtmlEntity.DeEntitize(root.InnerText ?? string.Empty);
    }

    private static IEnumerable<HtmlNode> SelectNodes(HtmlDocument document, string xpath) =>
        document.DocumentNode.SelectNodes(xpath) ?? Enumerable.Empty<HtmlNode>();

    private static bool TryResolve(string href, Uri baseUri, out Uri absolute)
    {
        absolute = baseUri;

        var value = HtmlEntity.DeEntitize(href).Trim();

        if (value.Length == 0 ||
            value.StartsWith('#') ||
            value.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("tel:", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!Uri.TryCreate(baseUri, value, out var resolved)) return false;
        if (resolved.Scheme != Uri.UriSchemeHttp && resolved.Scheme != Uri.UriSchemeHttps) return false;

        absolute = resolved;
        return true;
    }

    /// <summary>Host without <c>www.</c>, lowercased. Matches the dedupe key format.</summary>
    public static string NormalizeHost(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return string.Empty;

        var value = url.Trim();
        if (!value.Contains("://", StringComparison.Ordinal)) value = $"https://{value}";

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)) return string.Empty;

        var host = uri.Host.ToLowerInvariant();
        return host.StartsWith("www.", StringComparison.Ordinal) ? host[4..] : host;
    }

    /// <summary>Collapses whitespace and drops punctuation a phone never needs.</summary>
    public static string NormalizePhone(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;

        var trimmed = raw.Trim();
        var keepPlus = trimmed.StartsWith('+');
        var digits = NonDigit().Replace(trimmed, string.Empty);

        return digits.Length == 0 ? string.Empty : keepPlus ? $"+{digits}" : digits;
    }

    private static string Unescape(string value)
    {
        try
        {
            return Uri.UnescapeDataString(value);
        }
        catch (UriFormatException)
        {
            return value;
        }
    }

    private static void AddDistinct(List<string> list, string value)
    {
        if (value.Length == 0) return;
        if (!list.Contains(value, StringComparer.OrdinalIgnoreCase)) list.Add(value);
    }
}
