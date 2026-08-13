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
/// file: each user connects their own account by submitting their own
/// LinkedIn email/password to <c>session/login</c>, which this API uses to
/// drive a real, server-side Playwright browser through LinkedIn's own login
/// page — rather than the app sharing one dedicated account — see
/// <see cref="LinkedInSessionManager"/>'s remarks for why. A checkpoint
/// (verification code) mid-login round-trips through <c>session/login/verify</c>;
/// <c>session</c> itself remains available for a status check/removal.
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
    /// Starts a fresh LinkedIn login for the caller's own account: the API
    /// itself drives a server-side Playwright browser through LinkedIn's login
    /// page with the submitted email/password. Never persisted — used only to
    /// fill that one form, then discarded. On an outright accept, the captured
    /// session is saved immediately (also clearing any active restriction
    /// cooldown — a real, successful login is a real confirmation the account
    /// is fine). On a checkpoint, the response asks the caller to follow up
    /// with <see cref="SubmitLoginVerification"/>.
    /// </summary>
    [HttpPost("session/login")]
    [ProducesResponseType(typeof(LinkedInLoginResult), StatusCodes.Status200OK)]
    public async Task<ActionResult<LinkedInLoginResult>> Login(LinkedInLoginRequest request, CancellationToken ct)
    {
        if (currentUser.UserId is not { } userId) return Unauthorized();

        var result = await linkedInSession.BeginCredentialLoginAsync(userId, request.LinkedInEmail, request.LinkedInPassword, ct);
        return Ok(result);
    }

    /// <summary>
    /// Submits a verification code into the checkpoint a prior
    /// <see cref="Login"/> call left pending for the caller. May itself come
    /// back asking for another code — LinkedIn sometimes chains checkpoints.
    /// </summary>
    [HttpPost("session/login/verify")]
    [ProducesResponseType(typeof(LinkedInLoginResult), StatusCodes.Status200OK)]
    public async Task<ActionResult<LinkedInLoginResult>> SubmitLoginVerification(LinkedInLoginVerifyRequest request, CancellationToken ct)
    {
        if (currentUser.UserId is not { } userId) return Unauthorized();

        var result = await linkedInSession.SubmitLoginVerificationAsync(userId, request.Code, ct);
        return Ok(result);
    }

    /// <summary>
    /// Abandons the caller's own pending login (e.g. they closed the "connect
    /// LinkedIn" dialog mid-checkpoint) so its browser context and gate are
    /// released immediately rather than sitting open until it expires.
    /// </summary>
    [HttpPost("session/login/cancel")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> CancelLogin(CancellationToken ct)
    {
        if (currentUser.UserId is not { } userId) return Unauthorized();

        await linkedInSession.CancelLoginAsync(userId, ct);
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
