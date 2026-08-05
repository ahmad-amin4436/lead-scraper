using System.Text.RegularExpressions;
using LeadMine.Infrastructure.Scraping.Browser;
using LeadMine.Infrastructure.Scraping.Providers;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace LeadMine.Infrastructure.Scraping.Enrichment;

/// <summary>
/// Finds individuals at a company by scrolling its LinkedIn "People" tab with a
/// real, logged-in session, and flags which of them look like decision-makers
/// from their headline.
/// <para>
/// Unlike Apify's actor, this has no server-side title filter to lean on, so
/// it loads the People tab once (unfiltered) and classifies every visible card
/// client-side against <see cref="DecisionMakerTitles"/> — one page load per
/// company regardless of how many title variants matter, which matters for
/// <see cref="LinkedInSessionManager"/>'s daily search budget. The trade-off is
/// scrolling further into a large company's employee list to find the same
/// people a server-side filter would have surfaced immediately.
/// </para>
/// <para>
/// <b>Unverified against a live session</b> — see the equivalent note on
/// <see cref="PlaywrightLinkedInCompanyService"/>. <see cref="ReadProfileCardsAsync"/>
/// is the piece most likely to need adjustment after the first real run.
/// </para>
/// </summary>
public sealed partial class PlaywrightLinkedInPeopleService(
    LinkedInSessionManager session,
    ILogger<PlaywrightLinkedInPeopleService> logger)
{
    private const int MaxNoChangeScrolls = 3;

    /// <summary>
    /// Title fragments that read as ownership/leadership. A substring match
    /// against the headline, not an exhaustive enum — real-world titles vary
    /// too much for a fixed list to cover exactly, and a false positive here
    /// just means the record shows a decision-maker badge someone can ignore,
    /// not a wrong contact. Mirrors <c>ApifyLinkedInPeopleService</c>'s list —
    /// moved here unchanged rather than reinvented.
    /// </summary>
    private static readonly string[] DecisionMakerTitles =
    [
        "owner", "founder", "co-founder", "ceo", "chief executive", "president",
        "managing director", "managing partner", "director", "vice president",
        " vp ", "vp,", "vp.", "chief", "general manager", "gm,", "principal",
        "partner", "head of",
    ];

    public string? Readiness() => session.Readiness();

    public async Task<IReadOnlyList<LinkedInPersonResult>> SearchAsync(
        string companyLinkedInUrl,
        int maxResults,
        ProviderContext context,
        CancellationToken ct)
    {
        var notReady = Readiness();
        if (notReady is not null) throw new ProviderException(notReady, ProviderFailure.MissingApiKey);
        if (string.IsNullOrWhiteSpace(companyLinkedInUrl)) return [];

        var peopleUrl = BuildPeopleUrl(companyLinkedInUrl);
        if (peopleUrl is null) return [];

        session.EnsureSearchBudget();

        await using var lease = await session.AcquireContextAsync(ct);
        var page = await lease.Context.NewPageAsync();

        try
        {
            await context.RateLimiter.WaitAsync(ct);
            await page.GotoAsync(peopleUrl, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout = context.Options.RequestTimeoutMs,
            });

            if (LinkedInSessionManager.IsLoggedOutUrl(page.Url))
            {
                throw new ProviderException(
                    "LinkedIn session expired — re-run backend/tools/LinkedInLogin and re-upload storageState.json.",
                    ProviderFailure.MissingApiKey);
            }

            var results = new List<LinkedInPersonResult>();
            var seenProfiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var noChangeStreak = 0;

            while (results.Count < maxResults && noChangeStreak < MaxNoChangeScrolls)
            {
                ct.ThrowIfCancellationRequested();

                var cards = await ReadProfileCardsAsync(page);
                var before = seenProfiles.Count;

                foreach (var card in cards)
                {
                    if (results.Count >= maxResults) break;
                    if (!seenProfiles.Add(card.ProfileUrl)) continue;

                    var (isMatch, role) = ClassifyDecisionMaker(card.Headline);
                    if (!isMatch) continue;

                    results.Add(new LinkedInPersonResult(
                        FullName: card.Name,
                        // The People tab shows a headline, not a cleanly
                        // separated current-title field the way a profile
                        // page or Apify's structured actor output does.
                        JobTitle: card.Headline,
                        Headline: card.Headline,
                        LinkedInUrl: card.ProfileUrl,
                        Location: card.Location ?? string.Empty,
                        ExperienceJson: null,
                        EducationJson: null,
                        SkillsJson: null,
                        IsDecisionMaker: true,
                        DecisionMakerRole: role));
                }

                noChangeStreak = seenProfiles.Count == before ? noChangeStreak + 1 : 0;
                if (results.Count >= maxResults) break;

                await page.Mouse.WheelAsync(0, 2200);
                await PlaywrightBrowserManager.RandomDelayAsync(
                    context.Options.PlaywrightMinDelayMs, context.Options.PlaywrightMaxDelayMs, ct);
            }

            return results;
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not ProviderException)
        {
            await ScrapeFailureLogger.CaptureAsync(page, "linkedin-people", ex, context.Options, logger, ct);
            throw;
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    private static (bool IsMatch, string Role) ClassifyDecisionMaker(string? headline)
    {
        var text = $" {headline} ".ToLowerInvariant();

        foreach (var keyword in DecisionMakerTitles)
        {
            if (text.Contains(keyword, StringComparison.Ordinal))
            {
                return (true, headline ?? keyword);
            }
        }

        return (false, string.Empty);
    }

    /// <summary>
    /// Reads every visible profile card on the People tab. Best-effort: a
    /// card's "headline" here is its whole visible text block (name, title,
    /// location run together) rather than cleanly separated fields, since this
    /// app has no verified selector distinguishing them from a live session.
    /// </summary>
    private static async Task<List<ProfileCard>> ReadProfileCardsAsync(IPage page)
    {
        var raw = await page.EvalOnSelectorAllAsync<string[][]>(
            "a[href*='/in/']",
            """
            els => {
                const seen = new Set();
                const out = [];
                for (const a of els) {
                    const href = a.href.split('?')[0];
                    if (seen.has(href)) continue;
                    seen.add(href);
                    const card = a.closest('li') || a.closest('div');
                    const cardText = (card ? card.innerText : a.innerText) || '';
                    const name = (a.innerText || '').split('\n')[0].trim();
                    out.push([name, cardText.trim(), href]);
                }
                return out;
            }
            """);

        var cards = new List<ProfileCard>();

        foreach (var row in raw)
        {
            if (row.Length < 3 || row[0].Length == 0) continue;
            cards.Add(new ProfileCard(row[0], row[1], null, row[2]));
        }

        return cards;
    }

    private static string? BuildPeopleUrl(string companyLinkedInUrl)
    {
        var match = CompanySlugRegex().Match(companyLinkedInUrl);
        return match.Success ? $"https://www.linkedin.com/company/{match.Groups[1].Value}/people/" : null;
    }

    private sealed record ProfileCard(string Name, string Headline, string? Location, string ProfileUrl);

    [GeneratedRegex(@"linkedin\.com/company/([^/?]+)", RegexOptions.IgnoreCase)]
    private static partial Regex CompanySlugRegex();
}
