using LeadMine.Application.Authorization;
using LeadMine.Infrastructure.Authorization;
using LeadMine.Infrastructure.Scraping.Browser;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LeadMine.Api.Controllers;

/// <summary>
/// Phase 0 feasibility check for browser automation: can this host actually
/// launch a headless Chromium process at all?
/// <para>
/// Every existing data source here (Google Places, Apify) is an HTTP API call
/// precisely because that question was never answered for this shared host —
/// see <see cref="PlaywrightBrowserManager"/>. Hit this once after each deploy
/// before trusting any Playwright-backed provider in production.
/// </para>
/// </summary>
[Authorize]
[Route("api/admin/playwright")]
public sealed class PlaywrightDiagnosticsController(PlaywrightBrowserManager browser) : ApiControllerBase
{
    [HttpGet("diagnostics")]
    [HasPermission(Permissions.System.RunDiagnostics)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult> Diagnostics(CancellationToken ct)
    {
        var error = await browser.TryLaunchAsync(ct);

        return error is null
            ? Ok(new { ok = true, message = "Chromium launched successfully on this host." })
            : StatusCode(StatusCodes.Status500InternalServerError, new { ok = false, error });
    }
}
