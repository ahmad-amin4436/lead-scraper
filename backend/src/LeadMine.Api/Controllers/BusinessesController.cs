using LeadMine.Application.Authorization;
using LeadMine.Application.Common;
using LeadMine.Application.DTOs;
using LeadMine.Application.Interfaces;
using LeadMine.Infrastructure.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LeadMine.Api.Controllers;

[Authorize]
public sealed class BusinessesController(IBusinessService businesses) : ApiControllerBase
{
    /// <summary>Filtered, sorted, paged lead list.</summary>
    [HttpGet]
    [HasPermission(Permissions.Leads.View)]
    [ProducesResponseType(typeof(PagedResult<BusinessDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<BusinessDto>>> Query(
        [FromQuery] BusinessQueryRequest request,
        CancellationToken ct)
        => Ok(await businesses.QueryAsync(request, ct));

    [HttpGet("stats")]
    [HasPermission(Permissions.Leads.View)]
    [ProducesResponseType(typeof(BusinessStatsDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<BusinessStatsDto>> Stats(CancellationToken ct)
        => Ok(await businesses.GetStatsAsync(ct));

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.Leads.View)]
    [ProducesResponseType(typeof(BusinessDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BusinessDto>> GetById(Guid id, CancellationToken ct)
        => FromResult(await businesses.GetByIdAsync(id, ct));

    /// <summary>Adds a lead. Rejected with 409 if it duplicates an existing one.</summary>
    [HttpPost]
    [HasPermission(Permissions.Leads.Create)]
    [ProducesResponseType(typeof(BusinessDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BusinessDto>> Create(CreateBusinessRequest request, CancellationToken ct)
    {
        var result = await businesses.CreateAsync(request, ct);
        if (!result.Succeeded) return Problem(result);

        return CreatedAtAction(nameof(GetById), new { id = result.Value!.Id }, result.Value);
    }

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.Leads.Update)]
    [ProducesResponseType(typeof(BusinessDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<BusinessDto>> Update(Guid id, UpdateBusinessRequest request, CancellationToken ct)
        => FromResult(await businesses.UpdateAsync(id, request, ct));

    [HttpDelete("{id:guid}")]
    [HasPermission(Permissions.Leads.Delete)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var result = await businesses.DeleteAsync([id], ct);
        if (!result.Succeeded) return Problem(result);

        return result.Value == 0 ? NotFound() : NoContent();
    }

    /// <summary>Bulk soft-delete.</summary>
    [HttpPost("bulk-delete")]
    [HasPermission(Permissions.Leads.Delete)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<object>> BulkDelete(BulkDeleteRequest request, CancellationToken ct)
    {
        var result = await businesses.DeleteAsync(request.Ids, ct);
        return result.Succeeded ? Ok(new { removed = result.Value }) : Problem(result);
    }
}

/// <summary>Liveness/readiness probe. Anonymous by design.</summary>
[AllowAnonymous]
public sealed class HealthController : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<object> Get() => Ok(new
    {
        status = "ok",
        service = "LeadMine.Api",
        timestamp = DateTimeOffset.UtcNow,
    });
}
