using LeadMine.Application.Authorization;
using LeadMine.Application.Common;
using LeadMine.Application.DTOs;
using LeadMine.Application.Interfaces;
using LeadMine.Domain.Identity;
using LeadMine.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LeadMine.Infrastructure.Services;

public sealed class AuthService(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    LeadMineDbContext db,
    ITokenService tokens,
    IPermissionService permissions,
    IAuditService audit,
    ICurrentUser currentUser,
    ILogger<AuthService> logger) : IAuthService
{
    /// <summary>Self-service signup. New accounts get the least-privileged role.</summary>
    public async Task<Result<AuthResponse>> RegisterAsync(RegisterRequest request, CancellationToken ct = default)
    {
        var email = request.Email.Trim();

        if (await userManager.FindByEmailAsync(email) is not null)
        {
            // Deliberately the same wording a caller would see for any duplicate,
            // and the audit trail records the attempt.
            await audit.LogAsync("Auth.RegisterFailed", nameof(ApplicationUser), null, false, new { email }, ct);
            return Result<AuthResponse>.Conflict("An account with that email already exists.");
        }

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            IsActive = true,
            EmailConfirmed = true,
        };

        var created = await userManager.CreateAsync(user, request.Password);
        if (!created.Succeeded)
        {
            return Result<AuthResponse>.Failure(created.Errors.Select(e => e.Description).ToList());
        }

        await userManager.AddToRoleAsync(user, RoleNames.Viewer);
        await audit.LogAsync("Auth.Registered", nameof(ApplicationUser), user.Id.ToString(), true, new { email }, ct);

        return Result<AuthResponse>.Success(await IssueTokensAsync(user, ct));
    }

    public async Task<Result<AuthResponse>> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        var user = await userManager.FindByEmailAsync(request.Email.Trim());

        // Identical failure for "no such user" and "wrong password" so the
        // response cannot be used to enumerate which emails have accounts.
        const string invalid = "Invalid email or password.";

        if (user is null)
        {
            await audit.LogAsync("Auth.LoginFailed", nameof(ApplicationUser), null, false,
                new { email = request.Email, reason = "unknown-user" }, ct);
            return Result<AuthResponse>.Unauthorized(invalid);
        }

        if (!user.IsActive)
        {
            await audit.LogAsync("Auth.LoginFailed", nameof(ApplicationUser), user.Id.ToString(), false,
                new { reason = "inactive" }, ct);
            return Result<AuthResponse>.Unauthorized("This account has been deactivated.");
        }

        // lockoutOnFailure throttles brute-force attempts at the Identity layer.
        var result = await signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);

        if (result.IsLockedOut)
        {
            await audit.LogAsync("Auth.LoginLockedOut", nameof(ApplicationUser), user.Id.ToString(), false, null, ct);
            return Result<AuthResponse>.Unauthorized("Account locked after too many failed attempts. Try again later.");
        }

        if (!result.Succeeded)
        {
            await audit.LogAsync("Auth.LoginFailed", nameof(ApplicationUser), user.Id.ToString(), false,
                new { reason = "bad-password" }, ct);
            return Result<AuthResponse>.Unauthorized(invalid);
        }

        user.LastLoginAt = DateTimeOffset.UtcNow;
        await userManager.UpdateAsync(user);

        await audit.LogAsync("Auth.LoggedIn", nameof(ApplicationUser), user.Id.ToString(), true, null, ct);

        return Result<AuthResponse>.Success(await IssueTokensAsync(user, ct));
    }

    /// <summary>
    /// Exchanges a refresh token for a new pair, rotating the old one.
    /// <para>
    /// If a token that was already replaced is presented, it was captured and
    /// replayed — the entire family is revoked rather than just that token, which
    /// logs out the attacker and the legitimate user, whose next refresh fails
    /// and forces a fresh login.
    /// </para>
    /// </summary>
    public async Task<Result<AuthResponse>> RefreshAsync(string refreshToken, CancellationToken ct = default)
    {
        var hash = tokens.HashToken(refreshToken);

        var stored = await db.RefreshTokens
            .Include(rt => rt.User)
            .FirstOrDefaultAsync(rt => rt.TokenHash == hash, ct);

        if (stored is null)
        {
            return Result<AuthResponse>.Unauthorized("Invalid refresh token.");
        }

        if (stored.IsRevoked)
        {
            await RevokeFamilyAsync(stored.UserId, "Reuse of a revoked refresh token detected", ct);
            logger.LogWarning("Refresh token reuse detected for user {UserId}", stored.UserId);
            await audit.LogAsync("Auth.RefreshReuseDetected", nameof(RefreshToken), stored.Id.ToString(), false, null, ct);

            return Result<AuthResponse>.Unauthorized("This session is no longer valid. Please sign in again.");
        }

        if (stored.IsExpired)
        {
            return Result<AuthResponse>.Unauthorized("Refresh token has expired.");
        }

        if (!stored.User.IsActive)
        {
            return Result<AuthResponse>.Unauthorized("This account has been deactivated.");
        }

        var response = await IssueTokensAsync(stored.User, ct);

        stored.RevokedAt = DateTimeOffset.UtcNow;
        stored.RevokedByIp = currentUser.IpAddress;
        stored.RevokedReason = "Rotated";
        stored.ReplacedByTokenHash = tokens.HashToken(response.RefreshToken);
        await db.SaveChangesAsync(ct);

        return Result<AuthResponse>.Success(response);
    }

    public async Task<Result> RevokeAsync(string refreshToken, CancellationToken ct = default)
    {
        var hash = tokens.HashToken(refreshToken);
        var stored = await db.RefreshTokens.FirstOrDefaultAsync(rt => rt.TokenHash == hash, ct);

        // Revoking an unknown or already-dead token is a no-op success: the
        // caller's intent (this token must not work) is already satisfied.
        if (stored is null || !stored.IsActive) return Result.Success();

        stored.RevokedAt = DateTimeOffset.UtcNow;
        stored.RevokedByIp = currentUser.IpAddress;
        stored.RevokedReason = "Signed out";
        await db.SaveChangesAsync(ct);

        await audit.LogAsync("Auth.LoggedOut", nameof(ApplicationUser), stored.UserId.ToString(), true, null, ct);
        return Result.Success();
    }

    public async Task<Result> ChangePasswordAsync(
        Guid userId,
        ChangePasswordRequest request,
        CancellationToken ct = default)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null) return Result.NotFound("User not found.");

        var result = await userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!result.Succeeded)
        {
            return Result.Failure(result.Errors.Select(e => e.Description).ToList());
        }

        // Invalidate every outstanding session: a password change usually means
        // the old one may be compromised.
        user.SecurityVersion++;
        await userManager.UpdateAsync(user);
        await RevokeFamilyAsync(user.Id, "Password changed", ct);

        await audit.LogAsync("Auth.PasswordChanged", nameof(ApplicationUser), user.Id.ToString(), true, null, ct);
        return Result.Success();
    }

    public async Task<Result<UserDto>> GetCurrentUserAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null) return Result<UserDto>.NotFound("User not found.");

        return Result<UserDto>.Success(await MapAsync(user, ct));
    }

    private async Task<AuthResponse> IssueTokensAsync(ApplicationUser user, CancellationToken ct)
    {
        var roles = await userManager.GetRolesAsync(user);
        var rights = await permissions.GetEffectivePermissionsAsync(user.Id, ct);

        var accessToken = tokens.CreateAccessToken(user, roles, rights);
        var (refreshToken, refreshHash) = tokens.CreateRefreshToken();

        db.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = refreshHash,
            ExpiresAt = tokens.GetRefreshTokenExpiry(),
            CreatedByIp = currentUser.IpAddress,
        });

        await db.SaveChangesAsync(ct);

        return new AuthResponse
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            ExpiresAt = tokens.GetAccessTokenExpiry(),
            User = await MapAsync(user, ct, roles, rights),
        };
    }

    /// <summary>Revokes every live refresh token for a user.</summary>
    private async Task RevokeFamilyAsync(Guid userId, string reason, CancellationToken ct)
    {
        var active = await db.RefreshTokens
            .Where(rt => rt.UserId == userId && rt.RevokedAt == null)
            .ToListAsync(ct);

        foreach (var token in active)
        {
            token.RevokedAt = DateTimeOffset.UtcNow;
            token.RevokedByIp = currentUser.IpAddress;
            token.RevokedReason = reason;
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task<UserDto> MapAsync(
        ApplicationUser user,
        CancellationToken ct,
        IList<string>? roles = null,
        IReadOnlyList<string>? rights = null)
    {
        roles ??= await userManager.GetRolesAsync(user);
        rights ??= await permissions.GetEffectivePermissionsAsync(user.Id, ct);

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
