using System.Text.RegularExpressions;
using LeadMine.Infrastructure.Scraping.Browser;
using LeadMine.Infrastructure.Scraping.Providers;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace LeadMine.Infrastructure.Scraping.Enrichment;

/// <summary>
/// Finds individuals at a company by scrolling its LinkedIn "People" tab with a
/// real, logged-in session.
/// <para>
/// Two callers, two filters, one scroll/collect mechanism
/// (<see cref="ScrollAndCollectAsync"/>): <see cref="SearchAsync"/> (automatic
/// enrichment) keeps only cards that look like decision-makers;
/// <see cref="SearchByKeywordAsync"/> (the standalone People Search page) keeps
/// every match for a caller-chosen keyword/location filter and must not
/// additionally apply the decision-maker filter on top of that.
/// </para>
/// <para>
/// Confirmed live: LinkedIn blurs out-of-network results (name replaced with
/// "LinkedIn Member", no profile link) on its general cross-company people
/// search for an account with little network — but a company's own People tab
/// does not have that restriction, which is why both search paths here are
/// scoped to one company rather than searching all of LinkedIn at once.
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

    /// <summary>
    /// Used by automatic enrichment (<c>LinkedInEnrichmentRunner</c>): loads the
    /// company's People tab unfiltered and keeps only cards whose headline
    /// matches <see cref="DecisionMakerTitles"/>.
    /// </summary>
    public Task<IReadOnlyList<LinkedInPersonResult>> SearchAsync(
        string companyLinkedInUrl,
        int maxResults,
        ProviderContext context,
        CancellationToken ct)
    {
        var slugMatch = CompanySlugRegex().Match(companyLinkedInUrl);
        if (!slugMatch.Success) return Task.FromResult<IReadOnlyList<LinkedInPersonResult>>([]);

        var peopleUrl = $"https://www.linkedin.com/company/{slugMatch.Groups[1].Value}/people/";

        return ScrollAndCollectAsync(
            peopleUrl,
            maxResults,
            context,
            card =>
            {
                var (isMatch, role) = ClassifyDecisionMaker(card.Headline);
                return isMatch ? (Keep: true, IsDecisionMaker: true, Role: role) : (false, false, string.Empty);
            },
            "linkedin-people",
            ct);
    }

    /// <summary>
    /// Used by the standalone People Search page: loads the company's People
    /// tab filtered by <paramref name="keywords"/> (LinkedIn's own "Search
    /// employees by title, keyword or school" box on that page — passed as a
    /// URL query param rather than typed into the box, but the same server-side
    /// filter), keeping every match rather than only decision-maker-shaped
    /// ones, since the caller already chose the filter.
    /// </summary>
    public Task<IReadOnlyList<LinkedInPersonResult>> SearchByKeywordAsync(
        string companySlug,
        string? keywords,
        string? locationFilter,
        int maxResults,
        ProviderContext context,
        CancellationToken ct)
    {
        var peopleUrl = string.IsNullOrWhiteSpace(keywords)
            ? $"https://www.linkedin.com/company/{companySlug}/people/"
            : $"https://www.linkedin.com/company/{companySlug}/people/?keywords={Uri.EscapeDataString(keywords)}";

        return ScrollAndCollectAsync(
            peopleUrl,
            maxResults,
            context,
            card =>
            {
                // Best-effort: location isn't cleanly separated from the rest
                // of a card's text (see ReadProfileCardsAsync), so this checks
                // whatever text the card has, not a dedicated location field.
                if (!string.IsNullOrWhiteSpace(locationFilter) &&
                    !card.Headline.Contains(locationFilter, StringComparison.OrdinalIgnoreCase))
                {
                    return (false, false, string.Empty);
                }

                var (isMatch, role) = ClassifyDecisionMaker(card.Headline);
                return (true, isMatch, role);
            },
            "linkedin-people-search",
            ct);
    }

    /// <summary>
    /// Navigates to a company's People tab and scrolls, collecting cards that
    /// pass <paramref name="keep"/> until <paramref name="maxResults"/> is hit
    /// or a few consecutive scrolls add nothing new.
    /// </summary>
    private async Task<IReadOnlyList<LinkedInPersonResult>> ScrollAndCollectAsync(
        string peopleUrl,
        int maxResults,
        ProviderContext context,
        Func<ProfileCard, (bool Keep, bool IsDecisionMaker, string Role)> keep,
        string failureContext,
        CancellationToken ct)
    {
        var notReady = Readiness();
        if (notReady is not null) throw new ProviderException(notReady, ProviderFailure.MissingApiKey);

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

                    var (matched, isDecisionMaker, role) = keep(card);
                    if (!matched) continue;

                    results.Add(new LinkedInPersonResult(
                        FullName: card.Name,
                        // The People tab shows a headline, not a cleanly
                        // separated current-title field the way a profile
                        // page or a structured API would.
                        JobTitle: card.Headline,
                        Headline: card.Headline,
                        LinkedInUrl: card.ProfileUrl,
                        Location: card.Location ?? string.Empty,
                        ExperienceJson: null,
                        EducationJson: null,
                        SkillsJson: null,
                        IsDecisionMaker: isDecisionMaker,
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
            await ScrapeFailureLogger.CaptureAsync(page, failureContext, ex, context.Options, logger, ct);
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

    private sealed record ProfileCard(string Name, string Headline, string? Location, string ProfileUrl);

    [GeneratedRegex(@"linkedin\.com/company/([^/?]+)", RegexOptions.IgnoreCase)]
    private static partial Regex CompanySlugRegex();
}
