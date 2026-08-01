using LeadMine.Application.Authorization;
using LeadMine.Application.Common;
using LeadMine.Application.DTOs;
using LeadMine.Application.Interfaces;
using LeadMine.Infrastructure.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LeadMine.Api.Controllers;

[Authorize]
public sealed class UsersController(IUserService users) : ApiControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.Users.View)]
    [ProducesResponseType(typeof(PagedResult<UserDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<UserDto>>> GetAll(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        [FromQuery] string? search = null,
        CancellationToken ct = default)
        => Ok(await users.GetAllAsync(page, Math.Clamp(pageSize, 1, 200), search, ct));

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.Users.View)]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserDto>> GetById(Guid id, CancellationToken ct)
        => FromResult(await users.GetByIdAsync(id, ct));

    [HttpPost]
    [HasPermission(Permissions.Users.Create)]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<UserDto>> Create(CreateUserRequest request, CancellationToken ct)
    {
        var result = await users.CreateAsync(request, ct);
        if (!result.Succeeded) return Problem(result);

        return CreatedAtAction(nameof(GetById), new { id = result.Value!.Id }, result.Value);
    }

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.Users.Update)]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<UserDto>> Update(Guid id, UpdateUserRequest request, CancellationToken ct)
        => FromResult(await users.UpdateAsync(id, request, ct));

    [HttpDelete("{id:guid}")]
    [HasPermission(Permissions.Users.Delete)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var result = await users.DeleteAsync(id, ct);
        return result.Succeeded ? NoContent() : Problem(result);
    }

    /// <summary>Replaces the user's roles with exactly the supplied set.</summary>
    [HttpPut("{id:guid}/roles")]
    [HasPermission(Permissions.Users.ManageRoles)]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<UserDto>> SetRoles(Guid id, AssignRolesRequest request, CancellationToken ct)
        => FromResult(await users.SetRolesAsync(id, request.Roles, ct));

    /// <summary>
    /// Grants or denies a single permission for this user, on top of their roles.
    /// A deny always beats a role grant.
    /// </summary>
    [HttpPut("{id:guid}/permissions")]
    [HasPermission(Permissions.Users.ManagePermissions)]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<UserDto>> SetPermissionOverride(
        Guid id,
        UserPermissionOverrideRequest request,
        CancellationToken ct)
        => FromResult(await users.SetPermissionOverrideAsync(id, request.Permission, request.IsGranted, ct));

    /// <summary>Clears an override so the user falls back to their role grants.</summary>
    [HttpDelete("{id:guid}/permissions/{permission}")]
    [HasPermission(Permissions.Users.ManagePermissions)]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<UserDto>> RemovePermissionOverride(
        Guid id,
        string permission,
        CancellationToken ct)
        => FromResult(await users.RemovePermissionOverrideAsync(id, permission, ct));

    [HttpPost("{id:guid}/reset-password")]
    [HasPermission(Permissions.Users.ResetPassword)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ResetPassword(Guid id, ResetPasswordRequest request, CancellationToken ct)
    {
        var result = await users.ResetPasswordAsync(id, request.NewPassword, ct);
        return result.Succeeded ? NoContent() : Problem(result);
    }
}
