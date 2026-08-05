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
/// <b>Unverified against a live session as written.</b> Unlike the Google Maps
/// providers (confirmed against the real site), LinkedIn requires a login this
/// environment has no way to obtain — every unauthenticated request to a
/// company page redirects straight to <c>/uas/login</c> (confirmed). The
/// selectors below are a best first attempt based on LinkedIn's known page
/// structure; treat the first real run (after <c>backend/tools/LinkedInLogin</c>)
/// as the actual test, and expect to adjust <see cref="ReadFieldByLabelAsync"/>
/// and <see cref="ReadDescriptionAsync"/> against what that run's
/// <see cref="ScrapeFailureLogger"/> captures turn up.
/// </para>
/// </summary>
public sealed partial class PlaywrightLinkedInCompanyService(
    LinkedInSessionManager session,
    ILogger<PlaywrightLinkedInCompanyService> logger)
{
    public string? Readiness() => session.Readiness();

    /// <summary>
    /// Null when nothing on LinkedIn matched. Takes plain fields rather than a
    /// <see cref="Domain.Entities.Business"/> entity because the caller
    /// (<c>SearchRunner</c>) runs this before a discovered lead has been saved.
    /// </summary>
    public async Task<LinkedInCompanyResult?> EnrichAsync(
        string name,
        string? existingLinkedInUrl,
        ProviderContext context,
        CancellationToken ct)
    {
        var notReady = Readiness();
        if (notReady is not null) throw new ProviderException(notReady, ProviderFailure.MissingApiKey);

        await using var lease = await session.AcquireContextAsync(ct);
        var page = await lease.Context.NewPageAsync();

        try
        {
            var aboutUrl = await ResolveCompanyAboutUrlAsync(page, name, existingLinkedInUrl, context, ct);
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

            if (LinkedInSessionManager.IsLoggedOutUrl(page.Url))
            {
                throw new ProviderException(
                    "LinkedIn session expired — re-run backend/tools/LinkedInLogin and re-upload storageState.json.",
                    ProviderFailure.MissingApiKey);
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
        IPage page, string name, string? existingLinkedInUrl, ProviderContext context, CancellationToken ct)
    {
        var existingSlug = existingLinkedInUrl is null ? null : CompanySlugRegex().Match(existingLinkedInUrl);

        if (existingSlug is { Success: true })
        {
            return $"https://www.linkedin.com/company/{existingSlug.Groups[1].Value}/about/";
        }

        var searchUrl = $"https://www.linkedin.com/search/results/companies/?keywords={Uri.EscapeDataString(name)}";

        await context.RateLimiter.WaitAsync(ct);
        await page.GotoAsync(searchUrl, new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
            Timeout = context.Options.RequestTimeoutMs,
        });

        if (LinkedInSessionManager.IsLoggedOutUrl(page.Url)) return page.Url;

        var links = await page.EvalOnSelectorAllAsync<string[]>(
            "a[href*='/company/']", "els => els.map(e => e.href)");

        foreach (var link in links)
        {
            var match = CompanySlugRegex().Match(link);
            if (match.Success) return $"https://www.linkedin.com/company/{match.Groups[1].Value}/about/";
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

    [GeneratedRegex(@"linkedin\.com/company/([^/?]+)", RegexOptions.IgnoreCase)]
    private static partial Regex CompanySlugRegex();

    [GeneratedRegex(@"(\d+)")]
    private static partial Regex EmployeeCountRegex();
}
