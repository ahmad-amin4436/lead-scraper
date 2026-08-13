using LeadMine.Infrastructure.Scraping.Browser;
using LeadMine.Infrastructure.Scraping.Providers;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace LeadMine.Infrastructure.Scraping.Enrichment;

/// <summary>
/// Finds individuals at a company via LinkedIn's general people search, scoped
/// to that company by folding its name into the search keywords, with a real,
/// logged-in session.
/// <para>
/// Two callers, two filters, one scroll/collect mechanism
/// (<see cref="ScrollAndCollectAsync"/>): <see cref="SearchAsync"/> (automatic
/// enrichment) keeps only cards that look like decision-makers;
/// <see cref="SearchByKeywordAsync"/> (the standalone People Search page) keeps
/// every match for a caller-chosen keyword/location filter and must not
/// additionally apply the decision-maker filter on top of that.
/// </para>
/// <para>
/// <b>Confirmed live, with two real constraints — not "unverified," but not
/// unconditionally reliable either:</b>
/// </para>
/// <list type="number">
/// <item>
/// A company's own <c>/company/{slug}/people/</c> page — this class's original
/// target — is no longer a browsable roster. LinkedIn replaced it with an
/// aggregate insights page (member counts, "where they live/studied" charts);
/// there is no list of individual cards there at all anymore, confirmed against
/// a real session, regardless of any keyword filter appended to that URL. The
/// general people-search below is the only URL that still returns individual
/// profile cards.
/// </item>
/// <item>
/// Confirmed live: LinkedIn blurs out-of-network results on that general search
/// — name replaced with "LinkedIn Member", no <c>/in/</c> profile link — for an
/// account whose own network is small. This is a per-account restriction, not a
/// selector problem: on the dedicated scraping account this app was built
/// against, even a 1st-degree-only filter with no keyword returned zero
/// results, meaning that account currently has close to no real connections, so
/// close to 100% of search results come back anonymised. <see cref="ScrollAndCollectAsync"/>
/// already only matches cards with a real <c>/in/</c> link — which blurred
/// cards do not have — plus an explicit name check as a second guard, so this
/// shows up as a low or zero result count rather than garbage rows, but no
/// selector or URL change fixes it. It resolves as the account accumulates
/// real connections.
/// </item>
/// </list>
/// </summary>
public sealed class PlaywrightLinkedInPeopleService(
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
        // Ownership / founding
        "owner", "co-owner", "founder", "co-founder", "cofounder", "proprietor",

        // C-suite, spelled out and as initialisms. The bare "chief" catches
        // titles this list does not name individually (Chief Revenue Officer,
        // Chief People Officer, and whatever gets invented next).
        "ceo", "cfo", "cto", "coo", "cmo", "cio", "ciso", "cco", "cpo", "cro",
        "chief", "chief executive", "chief financial", "chief technology",
        "chief operating", "chief marketing", "chief information",

        // Board / executive
        "president", "vice president", " vp ", "vp,", "vp.", "svp", "evp",
        "managing director", "managing partner", "board member", "chairman",
        "chairperson", "executive director", "director",

        // Senior management and functional heads — the people who actually sign
        // off on a purchase in an SMB, which is what this app is prospecting.
        "head of", "general manager", "gm,", "principal", "partner",
        "business development", "operations manager", "hr director",
        "recruiting manager", "recruitment manager", "talent acquisition",
        "procurement", "purchasing manager", "it manager", "sales director",
        "sales manager", "marketing director", "marketing manager",
        "engineering director", "engineering manager", "product manager",
        "project manager", "account director", "regional manager",
        "branch manager", "practice lead", "team lead",
    ];

    public Task<string?> ReadinessAsync(Guid userId, CancellationToken ct) => session.ReadinessAsync(userId, ct);

    /// <summary>
    /// Used by automatic enrichment (<c>LinkedInEnrichmentRunner</c>): searches
    /// LinkedIn for <paramref name="companyName"/> and keeps only cards whose
    /// headline matches <see cref="DecisionMakerTitles"/>. Runs against
    /// <paramref name="userId"/>'s own LinkedIn session.
    /// </summary>
    public Task<IReadOnlyList<LinkedInPersonResult>> SearchAsync(
        string companyName,
        int maxResults,
        Guid userId,
        ProviderContext context,
        CancellationToken ct)
    {
        var peopleUrl = $"https://www.linkedin.com/search/results/people/?keywords={Uri.EscapeDataString(companyName)}";

        return ScrollAndCollectAsync(
            peopleUrl,
            maxResults,
            userId,
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
    /// Used by the standalone People Search page: searches LinkedIn for
    /// <paramref name="companyName"/> plus <paramref name="keywords"/> together
    /// (LinkedIn's search keywords are a single free-text field — there is no
    /// separate "restrict to this company" parameter that still works, see the
    /// class remarks), keeping every match rather than only decision-maker-shaped
    /// ones, since the caller already chose the filter. Runs against
    /// <paramref name="userId"/>'s own LinkedIn session.
    /// </summary>
    public Task<IReadOnlyList<LinkedInPersonResult>> SearchByKeywordAsync(
        string companyName,
        string? keywords,
        string? locationFilter,
        int maxResults,
        Guid userId,
        ProviderContext context,
        CancellationToken ct)
    {
        var terms = string.Join(" ", new[] { companyName, keywords }.Where(s => !string.IsNullOrWhiteSpace(s)));
        var peopleUrl = $"https://www.linkedin.com/search/results/people/?keywords={Uri.EscapeDataString(terms)}";

        return ScrollAndCollectAsync(
            peopleUrl,
            maxResults,
            userId,
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

    /// <summary>Exact text LinkedIn substitutes for a name it will not reveal to this account. Never save it.</summary>
    private const string BlurredMemberName = "LinkedIn Member";

    /// <summary>
    /// Navigates to a LinkedIn people-search URL and scrolls, collecting cards
    /// that pass <paramref name="keep"/> until <paramref name="maxResults"/> is
    /// hit or a few consecutive scrolls add nothing new.
    /// </summary>
    private async Task<IReadOnlyList<LinkedInPersonResult>> ScrollAndCollectAsync(
        string peopleUrl,
        int maxResults,
        Guid userId,
        ProviderContext context,
        Func<ProfileCard, (bool Keep, bool IsDecisionMaker, string Role)> keep,
        string failureContext,
        CancellationToken ct)
    {
        var notReady = await ReadinessAsync(userId, ct);
        if (notReady is not null) throw new ProviderException(notReady, ProviderFailure.MissingApiKey);

        await session.EnsureSearchBudgetAsync(userId, ct);

        await using var lease = await session.AcquireContextAsync(userId, ct);
        var page = await lease.Context.NewPageAsync();

        try
        {
            await context.RateLimiter.WaitAsync(ct);
            await page.GotoAsync(peopleUrl, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout = context.Options.RequestTimeoutMs,
            });

            await PlaywrightLinkedInCompanyService.EnsureNotRestrictedAsync(page, session, userId, ct);

            // Search results render client-side; reading immediately after
            // DOMContentLoaded finds nothing there yet (confirmed live — 0
            // results before this wait, real results after it). A timeout here
            // is still a legitimate outcome (a search that genuinely has no
            // results renders no cards either), so it falls through to the
            // scroll loop below rather than failing the call.
            try
            {
                await page.Locator("a[href*='/in/']").First
                    .WaitForAsync(new LocatorWaitForOptions { Timeout = context.Options.RequestTimeoutMs });
            }
            catch (TimeoutException)
            {
                logger.LogDebug("No LinkedIn people-search results rendered in time for {Url}", page.Url);
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

                    // Belt-and-suspenders: a card with a real /in/ link (the
                    // selector ReadProfileCardsAsync already requires) should
                    // never carry this placeholder name — LinkedIn's blurred,
                    // out-of-network cards don't expose that link at all,
                    // confirmed live. Guards the rare case where it does.
                    if (card.Name.Equals(BlurredMemberName, StringComparison.Ordinal)) continue;

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
                    context.Options.LinkedInMinDelayMs, context.Options.LinkedInMaxDelayMs, ct);
            }

            // Nothing usable: say *why*, rather than letting the caller report
            // "nobody matched your filters" — which blames the filters for what
            // is almost always the search returning no people to filter at all.
            if (results.Count == 0) await ExplainEmptyResultAsync(page, seenProfiles.Count);

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

    /// <summary>
    /// Turns "found nobody" into a specific, actionable reason.
    /// <para>
    /// Confirmed live, and the reason this exists: LinkedIn's people search
    /// only ever returns people inside the signed-in account's own network
    /// (1st/2nd/3rd degree). The account whose session this app was running
    /// with had <b>zero connections</b>, so it has no network — and every
    /// people search returned literally nothing, for companies whose employees
    /// are plainly visible to a normal account. No selector, URL, or scroll
    /// strategy can recover from that, so reporting it as "no matches" sent
    /// people looking for a bug in the scraper instead of at the account.
    /// </para>
    /// <para>
    /// The two empty cases are genuinely different and are reported
    /// differently: anonymised results mean the network works but these
    /// particular people are too distant, whereas a completely empty result
    /// set points at the account having no network at all.
    /// </para>
    /// </summary>
    private async Task ExplainEmptyResultAsync(IPage page, int profileLinksSeen)
    {
        string body;

        try
        {
            body = await page.InnerTextAsync("body");
        }
        catch
        {
            return;
        }

        var anonymised = body.Contains(BlurredMemberName, StringComparison.Ordinal);

        if (anonymised)
        {
            logger.LogWarning(
                "LinkedIn returned results for {Url}, but they are anonymised (\"{Placeholder}\", no profile link) — " +
                "they sit outside this account's network, so their names cannot be read. " +
                "Connecting the LinkedIn account to more people in this industry/region widens what it can see.",
                page.Url, BlurredMemberName);

            return;
        }

        if (profileLinksSeen == 0)
        {
            throw new ProviderException(
                "LinkedIn returned no people at all for this search. LinkedIn only surfaces people within the " +
                "signed-in account's own network, so an account with no connections gets an empty result for " +
                "every search — even for companies whose staff are visible to a normal account. Check that your " +
                "connected LinkedIn account has real connections (open linkedin.com/mynetwork/ as that account) " +
                "— reconnecting it from Settings won't help unless the account itself has a network.",
                ProviderFailure.MissingApiKey);
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
}
