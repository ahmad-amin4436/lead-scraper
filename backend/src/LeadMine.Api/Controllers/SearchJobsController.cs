using LeadMine.Application.Authorization;
using LeadMine.Application.Common;
using LeadMine.Application.DTOs;
using LeadMine.Infrastructure.Authorization;
using LeadMine.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LeadMine.Api.Controllers;

/// <summary>
/// Search runs, as the UI sees them.
/// <para>
/// Queuing a run only writes a row: a worker picks it up. That indirection is
/// what lets the API restart, or the browser close, without affecting a sweep in
/// progress.
/// </para>
/// </summary>
[Authorize]
[Route("api/searches")]
public sealed class SearchJobsController(ISearchJobService jobs) : ApiControllerBase
{
    /// <summary>Queues a run. Returns immediately — a worker executes it.</summary>
    [HttpPost]
    [HasPermission(Permissions.Searches.Create)]
    [ProducesResponseType(typeof(SearchJobDto), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<SearchJobDto>> Create(
        CreateSearchJobRequest request,
        CancellationToken ct)
    {
        var result = await jobs.CreateAsync(request, ct);
        if (!result.Succeeded) return Problem(result);

        return Accepted(result.Value);
    }

    /// <summary>Runs that are queued, running or stopping.</summary>
    [HttpGet("active")]
    [HasPermission(Permissions.Searches.View)]
    [ProducesResponseType(typeof(IReadOnlyList<SearchJobDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<SearchJobDto>>> Active(CancellationToken ct)
        => Ok(await jobs.GetActiveAsync(ct));

    [HttpGet]
    [HasPermission(Permissions.Searches.View)]
    [ProducesResponseType(typeof(PagedResult<SearchJobDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<SearchJobDto>>> History(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken ct = default)
        => Ok(await jobs.GetHistoryAsync(page, Math.Clamp(pageSize, 1, 100), ct));

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.Searches.View)]
    [ProducesResponseType(typeof(SearchJobDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SearchJobDto>> GetById(Guid id, CancellationToken ct)
        => FromResult(await jobs.GetByIdAsync(id, ct));

    /// <summary>
    /// Asks the run to stop. A live worker notices at its next checkpoint; if no
    /// worker holds it, the run is closed out immediately rather than left
    /// showing "Stopping" forever.
    /// </summary>
    [HttpPost("{id:guid}/stop")]
    [HasPermission(Permissions.Searches.Stop)]
    [ProducesResponseType(typeof(SearchJobDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<SearchJobDto>> Stop(Guid id, CancellationToken ct)
        => FromResult(await jobs.RequestStopAsync(id, ct));

    /// <summary>Removes one finished run from history. Active runs must be stopped first.</summary>
    [HttpDelete("{id:guid}")]
    [HasPermission(Permissions.Searches.Delete)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var result = await jobs.DeleteAsync(id, ct);
        return result.Succeeded ? NoContent() : Problem(result);
    }

    /// <summary>Clears every finished run the caller can see.</summary>
    [HttpDelete]
    [HasPermission(Permissions.Searches.Delete)]
    [ProducesResponseType(typeof(int), StatusCodes.Status200OK)]
    public async Task<ActionResult<int>> Clear(CancellationToken ct)
        => FromResult(await jobs.ClearHistoryAsync(ct));
}

/// <summary>
/// The worker protocol.
/// <para>
/// Service-key authenticated: workers are background processes with no user
/// session. Kept separate from the user-facing controller so the lease
/// operations can never be reached from a browser.
/// </para>
/// </summary>
[AllowAnonymous]
[RequireServiceKey]
[Route("api/ingest/jobs")]
public sealed class JobWorkerController(ISearchJobService jobs) : ApiControllerBase
{
    /// <summary>
    /// Claims the next runnable job. Returns 204 when the queue is empty, which
    /// is the worker's signal to back off and poll again.
    /// </summary>
    [HttpPost("claim")]
    [ProducesResponseType(typeof(ClaimedJobDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<ActionResult<ClaimedJobDto>> Claim(ClaimJobRequest request, CancellationToken ct)
    {
        var claimed = await jobs.ClaimNextAsync(request, ct);
        return claimed is null ? NoContent() : Ok(claimed);
    }

    /// <summary>
    /// Reports progress and extends the lease. The response carries the stop
    /// flag, so the worker learns about a cancellation without a second call.
    /// </summary>
    [HttpPost("{id:guid}/heartbeat")]
    [ProducesResponseType(typeof(JobHeartbeatResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<JobHeartbeatResponse>> Heartbeat(
        Guid id,
        JobHeartbeatRequest request,
        CancellationToken ct)
        => Ok(await jobs.HeartbeatAsync(id, request, ct));

    [HttpPost("{id:guid}/complete")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Complete(Guid id, CompleteJobRequest request, CancellationToken ct)
    {
        var result = await jobs.CompleteAsync(id, request, ct);
        return result.Succeeded ? NoContent() : Problem(result);
    }
}
