using System.Text.Json;
using LeadMine.Application.Authorization;
using LeadMine.Application.Common;
using LeadMine.Application.DTOs;
using LeadMine.Application.Interfaces;
using LeadMine.Domain.Enums;
using LeadMine.Infrastructure.Authorization;
using LeadMine.Infrastructure.Scraping.Browser;
using LeadMine.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LeadMine.Api.Controllers;

/// <summary>
/// Queues and tracks LinkedIn enrichment batches for already-saved leads — the
/// dedicated "LinkedIn Enrichment" page's backend.
/// <para>
/// Queued rather than run inline: enriching a lead means one or two real
/// browser page loads against a logged-in LinkedIn session, and a batch of up
/// to 25 leads run synchronously in one HTTP request does not reliably finish
/// before the request pipeline in front of this API times out. Same fix
/// <c>SearchJobsController</c> already uses for the same class of problem —
/// this shares its job table, lease/claim/heartbeat machinery, and worker pool
/// (<c>ScraperWorkerService</c>), just tagged <see cref="JobKind.LinkedInEnrichment"/>
/// and run by <c>LinkedInEnrichmentRunner</c> instead of <c>SearchRunner</c>.
/// </para>
/// <para>
/// Also owns the self-service LinkedIn session endpoints at the bottom of this
/// file: each user uploads their own <c>storageState.json</c> (from running
/// <c>backend/tools/LinkedInLogin</c> on their own machine, logged into their
/// own LinkedIn account) rather than the app sharing one dedicated account —
/// see <see cref="LinkedInSessionManager"/>'s remarks for why.
/// </para>
/// </summary>
[Authorize]
[Route("api/linkedin")]
public sealed class LinkedInController(
    ISearchJobService jobs,
    ICurrentUser currentUser,
    LinkedInSessionManager linkedInSession) : ApiControllerBase
{
    /// <summary>Queues a batch. Returns immediately — a worker executes it.</summary>
    [HttpPost("enrich-jobs")]
    [HasPermission(Permissions.LinkedIn.RunEnrichment)]
    [ProducesResponseType(typeof(SearchJobDto), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<SearchJobDto>> CreateEnrichJob(
        EnrichLeadsWithLinkedInRequest request,
        CancellationToken ct)
    {
        if (currentUser.UserId is null) return Unauthorized();

        var result = await jobs.CreateAsync(new CreateSearchJobRequest
        {
            Kind = JobKind.LinkedInEnrichment,
            RequestJson = JsonSerializer.Serialize(request),
            TotalTasks = request.BusinessIds.Count,
        }, ct);

        if (!result.Succeeded) return Problem(result);

        return Accepted(result.Value);
    }

    /// <summary>The live snapshot the LinkedIn Enrichment page polls.</summary>
    [HttpGet("enrich-jobs/{id:guid}")]
    [HasPermission(Permissions.LinkedIn.RunEnrichment)]
    [ProducesResponseType(typeof(SearchJobDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SearchJobDto>> GetEnrichJob(Guid id, CancellationToken ct)
        => FromResult(await jobs.GetByIdAsync(id, ct));

    /// <summary>Runs that are queued, running or stopping — the caller's own.</summary>
    [HttpGet("enrich-jobs/active")]
    [HasPermission(Permissions.LinkedIn.RunEnrichment)]
    [ProducesResponseType(typeof(IReadOnlyList<SearchJobDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<SearchJobDto>>> GetActiveEnrichJobs(CancellationToken ct)
        => Ok(await jobs.GetActiveAsync(JobKind.LinkedInEnrichment, ct));

    /// <summary>Asks a batch to stop. A live worker notices at its next checkpoint.</summary>
    [HttpPost("enrich-jobs/{id:guid}/stop")]
    [HasPermission(Permissions.LinkedIn.RunEnrichment)]
    [ProducesResponseType(typeof(SearchJobDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<SearchJobDto>> StopEnrichJob(Guid id, CancellationToken ct)
        => FromResult(await jobs.RequestStopAsync(id, ct));

    // --- standalone People Search (scoped to one named company) ------------

    /// <summary>
    /// Queues a company-scoped LinkedIn people search: the company's name is
    /// folded into the search keywords (see <c>PlaywrightLinkedInPeopleService</c>'s
    /// class remarks for why — the company's own People tab no longer lists
    /// individual profiles at all, confirmed live). Results still come back
    /// blurred as anonymous "LinkedIn Member" entries with no profile link for
    /// an account without much of a network of its own; that's a LinkedIn-side
    /// restriction this scoping does not change.
    /// </summary>
    [HttpPost("people-search-jobs")]
    [HasPermission(Permissions.LinkedIn.RunPeopleSearch)]
    [ProducesResponseType(typeof(SearchJobDto), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<SearchJobDto>> CreatePeopleSearchJob(
        LinkedInPeopleSearchRequest request,
        CancellationToken ct)
    {
        if (currentUser.UserId is null) return Unauthorized();

        var result = await jobs.CreateAsync(new CreateSearchJobRequest
        {
            Kind = JobKind.LinkedInPeopleSearch,
            RequestJson = JsonSerializer.Serialize(request),
            TotalTasks = request.MaxResults,
        }, ct);

        if (!result.Succeeded) return Problem(result);

        return Accepted(result.Value);
    }

    [HttpGet("people-search-jobs/{id:guid}")]
    [HasPermission(Permissions.LinkedIn.RunPeopleSearch)]
    [ProducesResponseType(typeof(SearchJobDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SearchJobDto>> GetPeopleSearchJob(Guid id, CancellationToken ct)
        => FromResult(await jobs.GetByIdAsync(id, ct));

    [HttpGet("people-search-jobs/active")]
    [HasPermission(Permissions.LinkedIn.RunPeopleSearch)]
    [ProducesResponseType(typeof(IReadOnlyList<SearchJobDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<SearchJobDto>>> GetActivePeopleSearchJobs(CancellationToken ct)
        => Ok(await jobs.GetActiveAsync(JobKind.LinkedInPeopleSearch, ct));

    [HttpPost("people-search-jobs/{id:guid}/stop")]
    [HasPermission(Permissions.LinkedIn.RunPeopleSearch)]
    [ProducesResponseType(typeof(SearchJobDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<SearchJobDto>> StopPeopleSearchJob(Guid id, CancellationToken ct)
        => FromResult(await jobs.RequestStopAsync(id, ct));

    // --- self-service LinkedIn session (bring your own account) ------------
    //
    // No [HasPermission] beyond [Authorize] on these three: they only ever act
    // on the caller's own row (keyed by their own user id), so there is
    // nothing here for a permission to additionally gate — the same reasoning
    // Send Email's signature upload already follows for "manage your own
    // thing." Using LinkedIn features at all still requires
    // Permissions.LinkedIn.RunEnrichment / RunPeopleSearch, same as before.

    /// <summary>
    /// Uploads or replaces the caller's own LinkedIn session: the
    /// storageState.json produced by running backend/tools/LinkedInLogin on
    /// their own machine, logged into their own LinkedIn account. Never a
    /// password — only the already-authenticated session, the same thing that
    /// tool always produced. A fresh upload also clears any active restriction
    /// cooldown on their account (see LinkedInSessionManager's remarks): a
    /// real, manual login is a real confirmation the account is fine.
    /// </summary>
    [HttpPost("session")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UploadSession(IFormFile file, CancellationToken ct)
    {
        if (currentUser.UserId is not { } userId) return Unauthorized();

        if (file.Length == 0 || file.Length > 1_000_000)
        {
            return BadRequest("storageState.json should be a small file, well under 1MB — this doesn't look right.");
        }

        using var reader = new StreamReader(file.OpenReadStream());
        var content = await reader.ReadToEndAsync(ct);

        // A cheap sanity check, not schema validation: catches an obviously
        // wrong upload (a screenshot, a random document) without pretending to
        // verify it is actually a valid Playwright storage state.
        try
        {
            using var _ = JsonDocument.Parse(content);
        }
        catch (JsonException)
        {
            return BadRequest("That doesn't look like a valid storageState.json file.");
        }

        await linkedInSession.UploadSessionAsync(userId, content, ct);
        return NoContent();
    }

    /// <summary>Status for the caller's own session — never the session content itself.</summary>
    [HttpGet("session")]
    [ProducesResponseType(typeof(LinkedInSessionStatus), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<LinkedInSessionStatus>> GetSessionStatus(CancellationToken ct)
    {
        if (currentUser.UserId is not { } userId) return Unauthorized();

        var status = await linkedInSession.GetStatusAsync(userId, ct);
        return status is null ? NotFound() : Ok(status);
    }

    /// <summary>Removes the caller's own LinkedIn session.</summary>
    [HttpDelete("session")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RemoveSession(CancellationToken ct)
    {
        if (currentUser.UserId is not { } userId) return Unauthorized();

        await linkedInSession.RemoveSessionAsync(userId, ct);
        return NoContent();
    }
}
