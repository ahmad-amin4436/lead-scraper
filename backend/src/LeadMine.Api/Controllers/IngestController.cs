using LeadMine.Application.DTOs;
using LeadMine.Infrastructure.Authorization;
using LeadMine.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LeadMine.Api.Controllers;

/// <summary>
/// Entry point for the scraper worker.
/// <para>
/// Authenticated by a service key rather than a user token, because the worker
/// runs as a background process with no session. It is the only endpoint that
/// may set the lead owner explicitly, and it is unreachable without the key.
/// </para>
/// </summary>
[AllowAnonymous]
[RequireServiceKey]
[Route("api/ingest")]
public sealed class IngestController(ILeadIngestService ingest) : ApiControllerBase
{
    /// <summary>Saves a batch of scraped leads against a user.</summary>
    [HttpPost("leads")]
    [ProducesResponseType(typeof(IngestResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IngestResultDto>> Leads(
        IngestLeadsRequest request,
        CancellationToken ct)
        => Ok(await ingest.IngestAsync(request, ct));
}
