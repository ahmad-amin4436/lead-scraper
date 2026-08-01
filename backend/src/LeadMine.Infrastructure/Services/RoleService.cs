using LeadMine.Application.Authorization;
using LeadMine.Application.Common;
using LeadMine.Application.DTOs;
using LeadMine.Application.Interfaces;
using LeadMine.Domain.Identity;
using LeadMine.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace LeadMine.Infrastructure.Services;

public sealed class RoleService(
    RoleManager<ApplicationRole> roleManager,
    LeadMineDbContext db,
    IAuditService audit,
    ICurrentUser currentUser) : IRoleService
{
    public async Task<IReadOnlyList<RoleDto>> GetAllAsync(CancellationToken ct = default)
    {
        var roles = await roleManager.Roles
            .AsNoTracking()
            .Include(r => r.RolePermissions).ThenInclude(rp => rp.Permission)
            .OrderBy(r => r.Name)
            .ToListAsync(ct);

        // One grouped count instead of a query per role.
        var counts = await db.UserRoles
            .GroupBy(ur => ur.RoleId)
            .Select(g => new { RoleId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.RoleId, x => x.Count, ct);

        return roles.Select(r => Map(r, counts.GetValueOrDefault(r.Id))).ToList();
    }

    public async Task<Result<RoleDto>> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var role = await roleManager.Roles
            .AsNoTracking()
            .Include(r => r.RolePermissions).ThenInclude(rp => rp.Permission)
            .FirstOrDefaultAsync(r => r.Id == id, ct);

        if (role is null) return Result<RoleDto>.NotFound("Role not found.");

        var count = await db.UserRoles.CountAsync(ur => ur.RoleId == id, ct);
        return Result<RoleDto>.Success(Map(role, count));
    }

    public async Task<Result<RoleDto>> CreateAsync(CreateRoleRequest request, CancellationToken ct = default)
    {
        var name = request.Name.Trim();

        if (await roleManager.RoleExistsAsync(name))
        {
            return Result<RoleDto>.Conflict($"A role named '{name}' already exists.");
        }

        var role = new ApplicationRole
        {
            Name = name,
            Description = request.Description.Trim(),
            IsSystemRole = false,
        };

        var created = await roleManager.CreateAsync(role);
        if (!created.Succeeded)
        {
            return Result<RoleDto>.Failure(created.Errors.Select(e => e.Description).ToList());
        }

        if (request.Permissions.Count > 0)
        {
            var assign = await AssignPermissionsAsync(role.Id, request.Permissions, ct);
            if (!assign.Succeeded) return Result<RoleDto>.Failure(assign.Errors);
        }

        await audit.LogAsync("Role.Created", nameof(ApplicationRole), role.Id.ToString(), true,
            new { name, permissions = request.Permissions }, ct);

        return await GetByIdAsync(role.Id, ct);
    }

    public async Task<Result<RoleDto>> UpdateAsync(Guid id, UpdateRoleRequest request, CancellationToken ct = default)
    {
        var role = await roleManager.FindByIdAsync(id.ToString());
        if (role is null) return Result<RoleDto>.NotFound("Role not found.");

        // Renaming a system role would break the constants that reference it.
        if (role.IsSystemRole && request.Name is not null && request.Name.Trim() != role.Name)
        {
            return Result<RoleDto>.Failure("Built-in roles cannot be renamed.");
        }

        if (request.Name is not null) role.Name = request.Name.Trim();
        if (request.Description is not null) role.Description = request.Description.Trim();

        var updated = await roleManager.UpdateAsync(role);
        if (!updated.Succeeded)
        {
            return Result<RoleDto>.Failure(updated.Errors.Select(e => e.Description).ToList());
        }

        await audit.LogAsync("Role.Updated", nameof(ApplicationRole), id.ToString(), true, request, ct);
        return await GetByIdAsync(id, ct);
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var role = await roleManager.FindByIdAsync(id.ToString());
        if (role is null) return Result.NotFound("Role not found.");

        if (role.IsSystemRole)
        {
            return Result.Failure("Built-in roles cannot be deleted.");
        }

        // Deleting an assigned role would silently strip access from its members.
        var assigned = await db.UserRoles.CountAsync(ur => ur.RoleId == id, ct);
        if (assigned > 0)
        {
            return Result.Conflict(
                $"This role is assigned to {assigned} user(s). Reassign them before deleting it.");
        }

        var deleted = await roleManager.DeleteAsync(role);
        if (!deleted.Succeeded)
        {
            return Result.Failure(deleted.Errors.Select(e => e.Description).ToList());
        }

        await audit.LogAsync("Role.Deleted", nameof(ApplicationRole), id.ToString(), true,
            new { name = role.Name }, ct);

        return Result.Success();
    }

    public async Task<Result<RoleDto>> SetPermissionsAsync(
        Guid id,
        IReadOnlyList<string> permissions,
        CancellationToken ct = default)
    {
        var role = await roleManager.FindByIdAsync(id.ToString());
        if (role is null) return Result<RoleDto>.NotFound("Role not found.");

        var result = await AssignPermissionsAsync(id, permissions, ct);
        if (!result.Succeeded) return Result<RoleDto>.Failure(result.Errors);

        await audit.LogAsync("Role.PermissionsChanged", nameof(ApplicationRole), id.ToString(), true,
            new { permissions }, ct);

        return await GetByIdAsync(id, ct);
    }

    /// <summary>
    /// Replaces a role's permissions wholesale, and bumps the security version of
    /// every member so their existing tokens (which embed the old rights) stop
    /// being trusted.
    /// </summary>
    private async Task<Result> AssignPermissionsAsync(
        Guid roleId,
        IReadOnlyList<string> permissionNames,
        CancellationToken ct)
    {
        var known = await db.Permissions
            .Where(p => permissionNames.Contains(p.Name))
            .ToListAsync(ct);

        var unknown = permissionNames
            .Except(known.Select(p => p.Name), StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (unknown.Count > 0)
        {
            return Result.Failure($"Unknown permission(s): {string.Join(", ", unknown)}");
        }

        var existing = await db.RolePermissions.Where(rp => rp.RoleId == roleId).ToListAsync(ct);
        db.RolePermissions.RemoveRange(existing);

        foreach (var permission in known)
        {
            db.RolePermissions.Add(new RolePermission
            {
                RoleId = roleId,
                PermissionId = permission.Id,
                GrantedBy = currentUser.UserId,
            });
        }

        var members = await db.UserRoles
            .Where(ur => ur.RoleId == roleId)
            .Select(ur => ur.UserId)
            .ToListAsync(ct);

        if (members.Count > 0)
        {
            await db.Users
                .Where(u => members.Contains(u.Id))
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.SecurityVersion, u => u.SecurityVersion + 1), ct);
        }

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    private static RoleDto Map(ApplicationRole role, int userCount) => new()
    {
        Id = role.Id,
        Name = role.Name ?? string.Empty,
        Description = role.Description,
        IsSystemRole = role.IsSystemRole,
        UserCount = userCount,
        Permissions = role.RolePermissions
            .Select(rp => rp.Permission.Name)
            .OrderBy(n => n)
            .ToList(),
    };
}
