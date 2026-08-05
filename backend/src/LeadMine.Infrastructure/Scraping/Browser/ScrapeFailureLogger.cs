using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace LeadMine.Infrastructure.Scraping.Browser;

/// <summary>
/// Captures a screenshot and the rendered HTML when a selector-based extraction
/// fails, so a broken selector is something to look at and fix rather than just
/// a stack trace.
/// <para>
/// This is the maintenance cost of scraping real pages instead of calling a
/// stable API: Google and LinkedIn change their markup on their own schedule,
/// with no changelog. Never fails the run itself — a failure to capture
/// diagnostics is logged and swallowed, the same best-effort spirit as every
/// other step in the pipeline this feeds into.
/// </para>
/// </summary>
public static class ScrapeFailureLogger
{
    /// <summary>Oldest files beyond this count (per kind) are pruned on each write.</summary>
    private const int MaxFilesPerKind = 50;

    public static async Task CaptureAsync(
        IPage page,
        string context,
        Exception failure,
        ScraperOptions options,
        ILogger logger,
        CancellationToken ct)
    {
        try
        {
            var directory = Path.GetFullPath(options.ScrapeFailureLogPath);
            Directory.CreateDirectory(directory);

            var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss-fff");
            var safeContext = string.Concat(context.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_')).PadRight(1, '_');
            var baseName = $"{stamp}-{safeContext}";

            var screenshotPath = Path.Combine(directory, $"{baseName}.png");
            var htmlPath = Path.Combine(directory, $"{baseName}.html");
            var errorPath = Path.Combine(directory, $"{baseName}.txt");

            await page.ScreenshotAsync(new PageScreenshotOptions { Path = screenshotPath });
            var html = await page.ContentAsync();
            await File.WriteAllTextAsync(htmlPath, html, ct);
            await File.WriteAllTextAsync(
                errorPath,
                $"{failure.GetType().Name}: {failure.Message}\nURL: {page.Url}\n\n{failure}",
                ct);

            Prune(directory, "*.png");
            Prune(directory, "*.html");
            Prune(directory, "*.txt");
        }
        catch (Exception captureEx)
        {
            logger.LogDebug(captureEx, "Could not capture scrape-failure diagnostics for {Context}", context);
        }
    }

    private static void Prune(string directory, string pattern)
    {
        var files = new DirectoryInfo(directory).GetFiles(pattern);
        if (files.Length <= MaxFilesPerKind) return;

        foreach (var file in files.OrderByDescending(f => f.LastWriteTimeUtc).Skip(MaxFilesPerKind))
        {
            try
            {
                file.Delete();
            }
            catch
            {
                // Best-effort cleanup; a locked file is not worth failing over.
            }
        }
    }
}
