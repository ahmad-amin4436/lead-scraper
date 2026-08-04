using LeadMine.Application.Authorization;
using LeadMine.Application.Common;
using LeadMine.Application.DTOs;
using LeadMine.Application.Interfaces;
using LeadMine.Infrastructure.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LeadMine.Api.Controllers;

/// <summary>Decision-makers found via LinkedIn people search, usually tied to a lead.</summary>
[Authorize]
[Route("api/people")]
public sealed class PeopleController(IPersonService people) : ApiControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.People.View)]
    [ProducesResponseType(typeof(PagedResult<PersonDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<PersonDto>>> GetAll(
        [FromQuery] PersonQueryRequest request,
        CancellationToken ct)
        => Ok(await people.QueryAsync(request, ct));

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.People.View)]
    [ProducesResponseType(typeof(PersonDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PersonDto>> GetById(Guid id, CancellationToken ct)
        => FromResult(await people.GetByIdAsync(id, ct));

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.People.Manage)]
    [ProducesResponseType(typeof(PersonDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<PersonDto>> Update(Guid id, UpdatePersonRequest request, CancellationToken ct)
        => FromResult(await people.UpdateAsync(id, request, ct));

    [HttpDelete("{id:guid}")]
    [HasPermission(Permissions.People.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var result = await people.DeleteAsync(id, ct);
        return result.Succeeded ? NoContent() : Problem(result);
    }
}
