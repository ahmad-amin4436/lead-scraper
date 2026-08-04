using LeadMine.Application.Authorization;
using LeadMine.Application.DTOs;
using LeadMine.Application.Interfaces;
using LeadMine.Infrastructure.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LeadMine.Api.Controllers;

/// <summary>
/// Enriches already-saved leads with LinkedIn data on demand, independent of
/// running a new search — the dedicated "LinkedIn Enrichment" page's backend.
/// </summary>
[Authorize]
[Route("api/linkedin")]
public sealed class LinkedInController(ILinkedInEnrichmentService enrichment) : ApiControllerBase
{
    /// <summary>
    /// Enriches up to 25 leads at once: company detail, then a decision-maker
    /// search. Synchronous — each lead is one or two slow Apify actor runs, so
    /// the batch is capped rather than queued as a background job.
    /// </summary>
    [HttpPost("enrich")]
    [HasPermission(Permissions.People.Manage)]
    [ProducesResponseType(typeof(EnrichLeadsWithLinkedInResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<EnrichLeadsWithLinkedInResultDto>> Enrich(
        EnrichLeadsWithLinkedInRequest request,
        CancellationToken ct)
        => FromResult(await enrichment.EnrichAsync(request, ct));
}
