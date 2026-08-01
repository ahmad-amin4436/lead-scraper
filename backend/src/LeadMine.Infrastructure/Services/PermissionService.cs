using LeadMine.Application.Authorization;
using LeadMine.Application.DTOs;
using LeadMine.Application.Interfaces;
using LeadMine.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LeadMine.Infrastructure.Services;

public sealed class PermissionService(LeadMineDbContext db) : IPermissionService
{
    /// <summary>
    /// Resolves a user's effective rights.
    /// <para>
    /// Order matters: role grants form the base set, per-user grants add to it,
    /// and per-user denies are subtracted last so a DENY always wins. Revoking a
    /// sensitive right from one person must not depend on auditing every role
    /// they happen to hold.
    /// </para>
    /// </summary>
    public async Task<IReadOnlyList<string>> GetEffectivePermissionsAsync(
        Guid userId,
        CancellationToken ct = default)
    {
        var fromRoles = await db.UserRoles
            .Where(ur => ur.UserId == userId)
            .Join(db.RolePermissions, ur => ur.RoleId, rp => rp.RoleId, (_, rp) => rp.PermissionId)
            .Join(db.Permissions, id => id, p => p.Id, (_, p) => p.Name)
            .Distinct()
            .ToListAsync(ct);

        var overrides = await db.UserPermissions
            .Where(up => up.UserId == userId)
            .Select(up => new { up.Permission.Name, up.IsGranted })
            .ToListAsync(ct);

        var effective = new HashSet<string>(fromRoles, StringComparer.OrdinalIgnoreCase);

        foreach (var granted in overrides.Where(o => o.IsGranted))
        {
            effective.Add(granted.Name);
        }

        foreach (var denied in overrides.Where(o => !o.IsGranted))
        {
            effective.Remove(denied.Name);
        }

        return effective.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task<bool> HasPermissionAsync(Guid userId, string permission, CancellationToken ct = default)
    {
        // An explicit deny short-circuits before any role lookup.
        var deny = await db.UserPermissions
            .AnyAsync(up => up.UserId == userId && !up.IsGranted && up.Permission.Name == permission, ct);

        if (deny) return false;

        var grant = await db.UserPermissions
            .AnyAsync(up => up.UserId == userId && up.IsGranted && up.Permission.Name == permission, ct);

        if (grant) return true;

        return await db.UserRoles
            .Where(ur => ur.UserId == userId)
            .Join(db.RolePermissions, ur => ur.RoleId, rp => rp.RoleId, (_, rp) => rp.PermissionId)
            .Join(db.Permissions, id => id, p => p.Id, (_, p) => p.Name)
            .AnyAsync(name => name == permission, ct);
    }

    public async Task<IReadOnlyList<PermissionDto>> GetAllAsync(CancellationToken ct = default)
    {
        var stored = await db.Permissions
            .OrderBy(p => p.Group).ThenBy(p => p.Name)
            .Select(p => new PermissionDto
            {
                Name = p.Name,
                Group = p.Group,
                Description = p.Description,
            })
            .ToListAsync(ct);

        // Fall back to the compiled catalogue if the seeder has not run yet.
        return stored.Count > 0
            ? stored
            : Permissions.All
                .Select(p => new PermissionDto { Name = p.Name, Group = p.Group, Description = p.Description })
                .ToList();
    }
}
