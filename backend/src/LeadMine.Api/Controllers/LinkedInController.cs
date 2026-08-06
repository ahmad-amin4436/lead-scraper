using System.Text.Json;
using LeadMine.Application.Authorization;
using LeadMine.Application.Common;
using LeadMine.Application.DTOs;
using LeadMine.Application.Interfaces;
using LeadMine.Domain.Enums;
using LeadMine.Infrastructure.Authorization;
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
/// </summary>
[Authorize]
[Route("api/linkedin")]
public sealed class LinkedInController(ISearchJobService jobs, ICurrentUser currentUser) : ApiControllerBase
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
}
