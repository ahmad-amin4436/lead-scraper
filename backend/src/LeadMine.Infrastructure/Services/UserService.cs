using LeadMine.Application.Authorization;
using LeadMine.Application.Common;
using LeadMine.Application.DTOs;
using LeadMine.Application.Interfaces;
using LeadMine.Domain.Identity;
using LeadMine.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace LeadMine.Infrastructure.Services;

public sealed class UserService(
    UserManager<ApplicationUser> userManager,
    RoleManager<ApplicationRole> roleManager,
    LeadMineDbContext db,
    IPermissionService permissions,
    IAuditService audit,
    ICurrentUser currentUser) : IUserService
{
    public async Task<PagedResult<UserDto>> GetAllAsync(
        int page,
        int pageSize,
        string? search,
        CancellationToken ct = default)
    {
        var query = userManager.Users.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(u =>
                u.Email!.ToLower().Contains(term) ||
                u.FirstName.ToLower().Contains(term) ||
                u.LastName.ToLower().Contains(term));
        }

        var total = await query.CountAsync(ct);

        var users = await query
            .OrderBy(u => u.Email)
            .Skip((Math.Max(1, page) - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var items = new List<UserDto>(users.Count);
        foreach (var user in users)
        {
            items.Add(await MapAsync(user, ct));
        }

        return PagedResult<UserDto>.Create(items, total, page, pageSize);
    }

    public async Task<Result<UserDto>> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var user = await userManager.FindByIdAsync(id.ToString());
        return user is null
            ? Result<UserDto>.NotFound("User not found.")
            : Result<UserDto>.Success(await MapAsync(user, ct));
    }

    public async Task<Result<UserDto>> CreateAsync(CreateUserRequest request, CancellationToken ct = default)
    {
        var email = request.Email.Trim();

        if (await userManager.FindByEmailAsync(email) is not null)
        {
            return Result<UserDto>.Conflict("An account with that email already exists.");
        }

        var invalidRoles = await FindUnknownRolesAsync(request.Roles, ct);
        if (invalidRoles.Count > 0)
        {
            return Result<UserDto>.Failure($"Unknown role(s): {string.Join(", ", invalidRoles)}");
        }

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            IsActive = request.IsActive,
            EmailConfirmed = true,
        };

        var created = await userManager.CreateAsync(user, request.Password);
        if (!created.Succeeded)
        {
            return Result<UserDto>.Failure(created.Errors.Select(e => e.Description).ToList());
        }

        if (request.Roles.Count > 0)
        {
            await userManager.AddToRolesAsync(user, request.Roles);
        }

        await audit.LogAsync("User.Created", nameof(ApplicationUser), user.Id.ToString(), true,
            new { email, roles = request.Roles }, ct);

        return Result<UserDto>.Success(await MapAsync(user, ct));
    }

    public async Task<Result<UserDto>> UpdateAsync(Guid id, UpdateUserRequest request, CancellationToken ct = default)
    {
        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null) return Result<UserDto>.NotFound("User not found.");

        if (request.FirstName is not null) user.FirstName = request.FirstName.Trim();
        if (request.LastName is not null) user.LastName = request.LastName.Trim();

        if (request.IsActive.HasValue && request.IsActive.Value != user.IsActive)
        {
            // Refuse to let an admin lock themselves out of their own session.
            if (!request.IsActive.Value && user.Id == currentUser.UserId)
            {
                return Result<UserDto>.Failure("You cannot deactivate your own account.");
            }

            user.IsActive = request.IsActive.Value;

            // Deactivation must take effect immediately, not when the access
            // token happens to expire.
            if (!user.IsActive)
            {
                user.SecurityVersion++;
                await RevokeTokensAsync(user.Id, "Account deactivated", ct);
            }
        }

        var updated = await userManager.UpdateAsync(user);
        if (!updated.Succeeded)
        {
            return Result<UserDto>.Failure(updated.Errors.Select(e => e.Description).ToList());
        }

        await audit.LogAsync("User.Updated", nameof(ApplicationUser), user.Id.ToString(), true, request, ct);
        return Result<UserDto>.Success(await MapAsync(user, ct));
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null) return Result.NotFound("User not found.");

        if (user.Id == currentUser.UserId)
        {
            return Result.Failure("You cannot delete your own account.");
        }

        // Protect the last administrator, or the system becomes unmanageable.
        if (await IsLastAdministratorAsync(user, ct))
        {
            return Result.Failure("The last administrator cannot be deleted.");
        }

        var deleted = await userManager.DeleteAsync(user);
        if (!deleted.Succeeded)
        {
            return Result.Failure(deleted.Errors.Select(e => e.Description).ToList());
        }

        await audit.LogAsync("User.Deleted", nameof(ApplicationUser), id.ToString(), true,
            new { email = user.Email }, ct);

        return Result.Success();
    }

    public async Task<Result<UserDto>> SetRolesAsync(
        Guid id,
        IReadOnlyList<string> roles,
        CancellationToken ct = default)
    {
        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null) return Result<UserDto>.NotFound("User not found.");

        var unknown = await FindUnknownRolesAsync(roles, ct);
        if (unknown.Count > 0)
        {
            return Result<UserDto>.Failure($"Unknown role(s): {string.Join(", ", unknown)}");
        }

        var current = await userManager.GetRolesAsync(user);

        // Guard against removing the final admin, including self-demotion.
        if (current.Contains(RoleNames.Administrator) &&
            !roles.Contains(RoleNames.Administrator, StringComparer.OrdinalIgnoreCase) &&
            await IsLastAdministratorAsync(user, ct))
        {
            return Result<UserDto>.Failure("The last administrator cannot lose the Administrator role.");
        }

        var toRemove = current.Except(roles, StringComparer.OrdinalIgnoreCase).ToList();
        var toAdd = roles.Except(current, StringComparer.OrdinalIgnoreCase).ToList();

        if (toRemove.Count > 0) await userManager.RemoveFromRolesAsync(user, toRemove);
        if (toAdd.Count > 0) await userManager.AddToRolesAsync(user, toAdd);

        if (toRemove.Count > 0 || toAdd.Count > 0)
        {
            // Their existing token carries the old permission set.
            user.SecurityVersion++;
            await userManager.UpdateAsync(user);
        }

        await audit.LogAsync("User.RolesChanged", nameof(ApplicationUser), user.Id.ToString(), true,
            new { added = toAdd, removed = toRemove }, ct);

        return Result<UserDto>.Success(await MapAsync(user, ct));
    }

    public async Task<Result<UserDto>> SetPermissionOverrideAsync(
        Guid id,
        string permission,
        bool isGranted,
        CancellationToken ct = default)
    {
        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null) return Result<UserDto>.NotFound("User not found.");

        var target = await db.Permissions.FirstOrDefaultAsync(p => p.Name == permission, ct);
        if (target is null) return Result<UserDto>.Failure($"Unknown permission '{permission}'.");

        var existing = await db.UserPermissions
            .FirstOrDefaultAsync(up => up.UserId == id && up.PermissionId == target.Id, ct);

        if (existing is null)
        {
            db.UserPermissions.Add(new UserPermission
            {
                UserId = id,
                PermissionId = target.Id,
                IsGranted = isGranted,
                GrantedBy = currentUser.UserId,
            });
        }
        else
        {
            existing.IsGranted = isGranted;
            existing.GrantedAt = DateTimeOffset.UtcNow;
            existing.GrantedBy = currentUser.UserId;
        }

        user.SecurityVersion++;
        await userManager.UpdateAsync(user);
        await db.SaveChangesAsync(ct);

        await audit.LogAsync("User.PermissionOverrideSet", nameof(ApplicationUser), id.ToString(), true,
            new { permission, isGranted }, ct);

        return Result<UserDto>.Success(await MapAsync(user, ct));
    }

    public async Task<Result<UserDto>> RemovePermissionOverrideAsync(
        Guid id,
        string permission,
        CancellationToken ct = default)
    {
        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null) return Result<UserDto>.NotFound("User not found.");

        var existing = await db.UserPermissions
            .Include(up => up.Permission)
            .FirstOrDefaultAsync(up => up.UserId == id && up.Permission.Name == permission, ct);

        if (existing is not null)
        {
            db.UserPermissions.Remove(existing);
            user.SecurityVersion++;
            await userManager.UpdateAsync(user);
            await db.SaveChangesAsync(ct);

            await audit.LogAsync("User.PermissionOverrideRemoved", nameof(ApplicationUser), id.ToString(), true,
                new { permission }, ct);
        }

        return Result<UserDto>.Success(await MapAsync(user, ct));
    }

    public async Task<Result> ResetPasswordAsync(Guid id, string newPassword, CancellationToken ct = default)
    {
        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null) return Result.NotFound("User not found.");

        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        var result = await userManager.ResetPasswordAsync(user, token, newPassword);

        if (!result.Succeeded)
        {
            return Result.Failure(result.Errors.Select(e => e.Description).ToList());
        }

        user.SecurityVersion++;
        await userManager.UpdateAsync(user);
        await RevokeTokensAsync(user.Id, "Password reset by administrator", ct);

        await audit.LogAsync("User.PasswordReset", nameof(ApplicationUser), id.ToString(), true, null, ct);
        return Result.Success();
    }

    private async Task<List<string>> FindUnknownRolesAsync(IReadOnlyList<string> roles, CancellationToken ct)
    {
        if (roles.Count == 0) return [];

        var known = await roleManager.Roles.Select(r => r.Name!).ToListAsync(ct);
        return roles.Where(r => !known.Contains(r, StringComparer.OrdinalIgnoreCase)).ToList();
    }

    private async Task<bool> IsLastAdministratorAsync(ApplicationUser user, CancellationToken ct)
    {
        if (!await userManager.IsInRoleAsync(user, RoleNames.Administrator)) return false;

        var admins = await userManager.GetUsersInRoleAsync(RoleNames.Administrator);
        return admins.Count(a => a.IsActive) <= 1;
    }

    private async Task RevokeTokensAsync(Guid userId, string reason, CancellationToken ct)
    {
        var active = await db.RefreshTokens
            .Where(rt => rt.UserId == userId && rt.RevokedAt == null)
            .ToListAsync(ct);

        foreach (var token in active)
        {
            token.RevokedAt = DateTimeOffset.UtcNow;
            token.RevokedReason = reason;
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task<UserDto> MapAsync(ApplicationUser user, CancellationToken ct)
    {
        var roles = await userManager.GetRolesAsync(user);
        var rights = await permissions.GetEffectivePermissionsAsync(user.Id, ct);

        return new UserDto
        {
            Id = user.Id,
            Email = user.Email ?? string.Empty,
            FirstName = user.FirstName,
            LastName = user.LastName,
            FullName = user.FullName,
            IsActive = user.IsActive,
            EmailConfirmed = user.EmailConfirmed,
            CreatedAt = user.CreatedAt,
            LastLoginAt = user.LastLoginAt,
            Roles = roles.ToList(),
            Permissions = rights,
        };
    }
}
