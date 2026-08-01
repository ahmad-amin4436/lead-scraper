using LeadMine.Application.Authorization;
using LeadMine.Application.DTOs;
using LeadMine.Application.Interfaces;
using LeadMine.Infrastructure.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LeadMine.Api.Controllers;

[Authorize]
public sealed class RolesController(IRoleService roles) : ApiControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.Roles.View)]
    [ProducesResponseType(typeof(IReadOnlyList<RoleDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<RoleDto>>> GetAll(CancellationToken ct)
        => Ok(await roles.GetAllAsync(ct));

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.Roles.View)]
    [ProducesResponseType(typeof(RoleDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RoleDto>> GetById(Guid id, CancellationToken ct)
        => FromResult(await roles.GetByIdAsync(id, ct));

    [HttpPost]
    [HasPermission(Permissions.Roles.Create)]
    [ProducesResponseType(typeof(RoleDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RoleDto>> Create(CreateRoleRequest request, CancellationToken ct)
    {
        var result = await roles.CreateAsync(request, ct);
        if (!result.Succeeded) return Problem(result);

        return CreatedAtAction(nameof(GetById), new { id = result.Value!.Id }, result.Value);
    }

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.Roles.Update)]
    [ProducesResponseType(typeof(RoleDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<RoleDto>> Update(Guid id, UpdateRoleRequest request, CancellationToken ct)
        => FromResult(await roles.UpdateAsync(id, request, ct));

    [HttpDelete("{id:guid}")]
    [HasPermission(Permissions.Roles.Delete)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var result = await roles.DeleteAsync(id, ct);
        return result.Succeeded ? NoContent() : Problem(result);
    }

    /// <summary>
    /// Replaces the role's permissions. Members' existing tokens are invalidated,
    /// so the change takes effect on their next request.
    /// </summary>
    [HttpPut("{id:guid}/permissions")]
    [HasPermission(Permissions.Roles.ManagePermissions)]
    [ProducesResponseType(typeof(RoleDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<RoleDto>> SetPermissions(
        Guid id,
        SetRolePermissionsRequest request,
        CancellationToken ct)
        => FromResult(await roles.SetPermissionsAsync(id, request.Permissions, ct));
}

[Authorize]
public sealed class PermissionsController(IPermissionService permissions) : ApiControllerBase
{
    /// <summary>Every right the system defines, grouped for an admin UI.</summary>
    [HttpGet]
    [HasPermission(Permissions.Roles.View)]
    [ProducesResponseType(typeof(IReadOnlyList<PermissionDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<PermissionDto>>> GetAll(CancellationToken ct)
        => Ok(await permissions.GetAllAsync(ct));

    /// <summary>The caller's own effective permissions.</summary>
    [HttpGet("mine")]
    [ProducesResponseType(typeof(IReadOnlyList<string>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<string>>> Mine(CancellationToken ct)
    {
        if (CurrentUserId is not { } userId) return Unauthorized();
        return Ok(await permissions.GetEffectivePermissionsAsync(userId, ct));
    }
}
