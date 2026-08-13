using System.Text.RegularExpressions;
using LeadMine.Infrastructure.Scraping.Browser;
using LeadMine.Infrastructure.Scraping.Providers;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace LeadMine.Infrastructure.Scraping.Enrichment;

/// <summary>
/// Enriches a business with publicly available LinkedIn company data — industry,
/// employee count, headquarters, description, the company's own LinkedIn URL —
/// by driving a real, logged-in browser session against LinkedIn's company
/// "About" page.
/// <para>
/// <b>Confirmed against a live session.</b> <see cref="ReadFieldByLabelAsync"/>,
/// <see cref="ReadWebsiteAsync"/> and <see cref="ReadDescriptionAsync"/> all
/// correctly extract real values when given time to render — verified against
/// a real company page with a real logged-in session. The one thing that made
/// this look broken in production is <see cref="EnrichAsync"/> reading the page
/// immediately after <c>DOMContentLoaded</c>: LinkedIn's About page is a
/// client-rendered SPA, and its fields (including the very "Industry" label
/// this class waits for below) are not in the DOM yet at that point — every
/// field read would silently return null, not because the selector was wrong,
/// but because it ran before the content existed.
/// </para>
/// </summary>
public sealed partial class PlaywrightLinkedInCompanyService(
    LinkedInSessionManager session,
    ILogger<PlaywrightLinkedInCompanyService> logger)
{
    public Task<string?> ReadinessAsync(Guid userId, CancellationToken ct) => session.ReadinessAsync(userId, ct);

    /// <summary>
    /// Null when nothing on LinkedIn matched. Takes plain fields rather than a
    /// <see cref="Domain.Entities.Business"/> entity because the caller
    /// (<c>SearchRunner</c>) runs this before a discovered lead has been saved.
    /// Runs against <paramref name="userId"/>'s own LinkedIn session — see
    /// <see cref="LinkedInSessionManager"/>'s remarks.
    /// </summary>
    public async Task<LinkedInCompanyResult?> EnrichAsync(
        string name,
        string? existingLinkedInUrl,
        Guid userId,
        ProviderContext context,
        CancellationToken ct)
    {
        var notReady = await ReadinessAsync(userId, ct);
        if (notReady is not null) throw new ProviderException(notReady, ProviderFailure.MissingApiKey);

        await using var lease = await session.AcquireContextAsync(userId, ct);
        var page = await lease.Context.NewPageAsync();

        try
        {
            var aboutUrl = await ResolveCompanyAboutUrlAsync(page, name, existingLinkedInUrl, userId, session, context, ct);
            if (aboutUrl is null) return null;

            if (page.Url != aboutUrl)
            {
                await context.RateLimiter.WaitAsync(ct);
                await page.GotoAsync(aboutUrl, new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.DOMContentLoaded,
                    Timeout = context.Options.RequestTimeoutMs,
                });
            }

            // Checked before the human-pacing delay below, not after: no point
            // waiting several seconds to be polite on a page that is already a
            // restriction warning.
            await EnsureNotRestrictedAsync(page, session, userId, ct);

            await PlaywrightBrowserManager.RandomDelayAsync(
                context.Options.LinkedInMinDelayMs, context.Options.LinkedInMaxDelayMs, ct);

            // The About page renders its detail fields client-side; reading them
            // right after DOMContentLoaded finds nothing there yet. "Industry" is
            // the first field this class reads, so it doubles as the readiness
            // signal for the rest — a timeout means the page loaded but its
            // detail section did not render in time, not that the fields below
            // are individually missing (each already tolerates that on its own).
            try
            {
                await page.GetByText("Industry", new PageGetByTextOptions { Exact = true }).First
                    .WaitForAsync(new LocatorWaitForOptions { Timeout = context.Options.RequestTimeoutMs });
            }
            catch (TimeoutException)
            {
                logger.LogDebug("LinkedIn About page detail section did not render in time for {Url}", page.Url);
            }

            var industry = await ReadFieldByLabelAsync(page, "Industry");
            var companySize = await ReadFieldByLabelAsync(page, "Company size");
            var headquarters = await ReadFieldByLabelAsync(page, "Headquarters");
            var website = await ReadWebsiteAsync(page);
            var description = await ReadDescriptionAsync(page);

            return new LinkedInCompanyResult(
                Industry: industry,
                EmployeeCount: ParseEmployeeCountLowerBound(companySize),
                Description: description,
                LinkedInUrl: NormalizeToCompanyUrl(page.Url),
                Website: website,
                HeadquartersCity: headquarters);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not ProviderException)
        {
            await ScrapeFailureLogger.CaptureAsync(page, "linkedin-company", ex, context.Options, logger, ct);
            throw;
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    /// <summary>
    /// A lead's LinkedIn field may already hold a company page URL (found by
    /// the website crawler) — a direct link beats re-searching by name.
    /// </summary>
    private static async Task<string?> ResolveCompanyAboutUrlAsync(
        IPage page, string name, string? existingLinkedInUrl, Guid userId, LinkedInSessionManager session, ProviderContext context, CancellationToken ct)
    {
        var existingSlug = existingLinkedInUrl is null ? null : CompanySlugRegex().Match(existingLinkedInUrl);

        if (existingSlug is { Success: true })
        {
            return $"https://www.linkedin.com/company/{existingSlug.Groups[1].Value}/about/";
        }

        var slug = await ResolveCompanySlugByNameAsync(page, name, userId, session, context, ct);
        return slug is null ? null : $"https://www.linkedin.com/company/{slug}/about/";
    }

    /// <summary>
    /// Searches LinkedIn's own company search by name and returns the first
    /// match's slug — shared with <see cref="PlaywrightLinkedInPeopleService"/>
    /// (via <see cref="LinkedInPeopleSearchRunner"/>) rather than duplicated,
    /// since both need exactly this "I only have a name, find the company"
    /// step.
    /// </summary>
    public static async Task<string?> ResolveCompanySlugByNameAsync(
        IPage page, string companyName, Guid userId, LinkedInSessionManager session, ProviderContext context, CancellationToken ct)
    {
        var searchUrl = $"https://www.linkedin.com/search/results/companies/?keywords={Uri.EscapeDataString(companyName)}";

        await context.RateLimiter.WaitAsync(ct);
        await page.GotoAsync(searchUrl, new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
            Timeout = context.Options.RequestTimeoutMs,
        });

        await EnsureNotRestrictedAsync(page, session, userId, ct);

        await PlaywrightBrowserManager.RandomDelayAsync(
            context.Options.LinkedInMinDelayMs, context.Options.LinkedInMaxDelayMs, ct);

        var links = await page.EvalOnSelectorAllAsync<string[]>(
            "a[href*='/company/']", "els => els.map(e => e.href)");

        foreach (var link in links)
        {
            var match = CompanySlugRegex().Match(link);
            if (match.Success) return match.Groups[1].Value;
        }

        return null;
    }

    /// <summary>
    /// LinkedIn's About page renders company fields as a label followed by its
    /// value in the next element — matching by the exact label text is what
    /// survives LinkedIn's obfuscated, auto-generated class names, the same
    /// principle the Google Maps extractor uses for <c>aria-label</c>.
    /// </summary>
    private static async Task<string?> ReadFieldByLabelAsync(IPage page, string label)
    {
        try
        {
            var labelLocator = page.GetByText(label, new PageGetByTextOptions { Exact = true }).First;
            if (await labelLocator.CountAsync() == 0) return null;

            var value = await labelLocator.EvaluateAsync<string?>(
                """
                el => {
                    const dt = el.closest('dt');
                    if (dt && dt.nextElementSibling) return dt.nextElementSibling.textContent;
                    const parent = el.parentElement;
                    const sibling = parent ? parent.nextElementSibling : null;
                    return sibling ? sibling.textContent : null;
                }
                """);

            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
        catch
        {
            return null;
        }
    }

    private static async Task<string?> ReadWebsiteAsync(IPage page)
    {
        try
        {
            var labelLocator = page.GetByText("Website", new PageGetByTextOptions { Exact = true }).First;
            if (await labelLocator.CountAsync() == 0) return null;

            var href = await labelLocator.EvaluateAsync<string?>(
                """
                el => {
                    const dt = el.closest('dt');
                    const container = dt ? dt.nextElementSibling : el.parentElement?.nextElementSibling;
                    const link = container ? container.querySelector('a[href]') : null;
                    return link ? link.getAttribute('href') : null;
                }
                """);

            return string.IsNullOrWhiteSpace(href) ? null : ProviderHelpers.NormalizeUrl(href);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Best-effort: LinkedIn shows an editorial "Overview" description near the
    /// top of the About page, but this app has no verified selector for it —
    /// falls back to null rather than risking capturing unrelated page text.
    /// </summary>
    private static async Task<string?> ReadDescriptionAsync(IPage page)
    {
        try
        {
            var overview = page.GetByText("Overview", new PageGetByTextOptions { Exact = true }).First;
            if (await overview.CountAsync() == 0) return null;

            var text = await overview.EvaluateAsync<string?>(
                "el => { const p = el.closest('section'); const para = p ? p.querySelector('p') : null; return para ? para.textContent : null; }");

            return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// "51-200 employees" / "10,001+ employees" → the lower bound, since
    /// scraping the public page only ever gets LinkedIn's own display range,
    /// never a precise count the way a paid data source might.
    /// </summary>
    private static int? ParseEmployeeCountLowerBound(string? companySize)
    {
        if (string.IsNullOrWhiteSpace(companySize)) return null;

        var match = EmployeeCountRegex().Match(companySize.Replace(",", string.Empty));
        return match.Success ? int.Parse(match.Groups[1].Value) : null;
    }

    private static string NormalizeToCompanyUrl(string url)
    {
        var match = CompanySlugRegex().Match(url);
        return match.Success ? $"https://www.linkedin.com/company/{match.Groups[1].Value}/" : url;
    }

    /// <summary>
    /// Throws a clear, fatal <see cref="ProviderException"/> on either a dead
    /// session (logged out) or an active restriction warning — tripping
    /// <see cref="LinkedInSessionManager.ReportRestrictionAsync"/> for the
    /// latter so every other LinkedIn call for this same user backs off too,
    /// not just this one. Shared by every navigation in this class and in
    /// <see cref="PlaywrightLinkedInPeopleService"/> rather than duplicated.
    /// </summary>
    internal static async Task EnsureNotRestrictedAsync(IPage page, LinkedInSessionManager session, Guid userId, CancellationToken ct)
    {
        if (LinkedInSessionManager.IsLoggedOutUrl(page.Url))
        {
            throw new ProviderException(
                "Your LinkedIn session expired — reconnect your LinkedIn account from Settings.",
                ProviderFailure.MissingApiKey);
        }

        if (await LinkedInSessionManager.IsRestrictedContentAsync(page))
        {
            await session.ReportRestrictionAsync(userId, $"a restriction warning at {page.Url}", ct);

            throw new ProviderException(
                "LinkedIn flagged your account's traffic with a restriction warning. Your LinkedIn automation " +
                "is now paused as a precaution — see the readiness message for how long.",
                ProviderFailure.MissingApiKey);
        }
    }

    [GeneratedRegex(@"linkedin\.com/company/([^/?]+)", RegexOptions.IgnoreCase)]
    private static partial Regex CompanySlugRegex();

    [GeneratedRegex(@"(\d+)")]
    private static partial Regex EmployeeCountRegex();
}
