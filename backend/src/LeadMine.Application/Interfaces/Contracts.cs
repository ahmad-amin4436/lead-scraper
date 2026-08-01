using System.Security.Claims;
using LeadMine.Application.Common;
using LeadMine.Application.DTOs;
using LeadMine.Domain.Identity;

namespace LeadMine.Application.Interfaces;

/// <summary>The caller behind the current request.</summary>
public interface ICurrentUser
{
    Guid? UserId { get; }

    string? Email { get; }

    bool IsAuthenticated { get; }

    string? IpAddress { get; }

    string? UserAgent { get; }

    IReadOnlyList<string> Permissions { get; }

    bool HasPermission(string permission);
}

/// <summary>Issues and validates access/refresh tokens.</summary>
public interface ITokenService
{
    /// <summary>
    /// Signs a JWT carrying the user's roles and effective permissions.
    /// </summary>
    string CreateAccessToken(ApplicationUser user, IEnumerable<string> roles, IEnumerable<string> permissions);

    /// <summary>
    /// Returns the raw refresh token (given to the client once) and its hash
    /// (all that is persisted).
    /// </summary>
    (string Token, string Hash) CreateRefreshToken();

    string HashToken(string token);

    DateTimeOffset GetAccessTokenExpiry();

    DateTimeOffset GetRefreshTokenExpiry();

    /// <summary>Reads an expired token's claims, for refresh flows.</summary>
    ClaimsPrincipal? GetPrincipalFromExpiredToken(string token);
}

/// <summary>Resolves the rights a user actually has.</summary>
public interface IPermissionService
{
    /// <summary>
    /// Effective permissions: everything granted by the user's roles, plus
    /// per-user grants, minus per-user denies (deny always wins).
    /// </summary>
    Task<IReadOnlyList<string>> GetEffectivePermissionsAsync(Guid userId, CancellationToken ct = default);

    Task<bool> HasPermissionAsync(Guid userId, string permission, CancellationToken ct = default);

    Task<IReadOnlyList<PermissionDto>> GetAllAsync(CancellationToken ct = default);
}

public interface IAuthService
{
    Task<Result<AuthResponse>> RegisterAsync(RegisterRequest request, CancellationToken ct = default);

    Task<Result<AuthResponse>> LoginAsync(LoginRequest request, CancellationToken ct = default);

    Task<Result<AuthResponse>> RefreshAsync(string refreshToken, CancellationToken ct = default);

    Task<Result> RevokeAsync(string refreshToken, CancellationToken ct = default);

    Task<Result> ChangePasswordAsync(Guid userId, ChangePasswordRequest request, CancellationToken ct = default);

    Task<Result<UserDto>> GetCurrentUserAsync(Guid userId, CancellationToken ct = default);
}

public interface IUserService
{
    Task<PagedResult<UserDto>> GetAllAsync(int page, int pageSize, string? search, CancellationToken ct = default);

    Task<Result<UserDto>> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<Result<UserDto>> CreateAsync(CreateUserRequest request, CancellationToken ct = default);

    Task<Result<UserDto>> UpdateAsync(Guid id, UpdateUserRequest request, CancellationToken ct = default);

    Task<Result> DeleteAsync(Guid id, CancellationToken ct = default);

    Task<Result<UserDto>> SetRolesAsync(Guid id, IReadOnlyList<string> roles, CancellationToken ct = default);

    Task<Result<UserDto>> SetPermissionOverrideAsync(Guid id, string permission, bool isGranted, CancellationToken ct = default);

    Task<Result<UserDto>> RemovePermissionOverrideAsync(Guid id, string permission, CancellationToken ct = default);

    Task<Result> ResetPasswordAsync(Guid id, string newPassword, CancellationToken ct = default);
}

public interface IRoleService
{
    Task<IReadOnlyList<RoleDto>> GetAllAsync(CancellationToken ct = default);

    Task<Result<RoleDto>> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<Result<RoleDto>> CreateAsync(CreateRoleRequest request, CancellationToken ct = default);

    Task<Result<RoleDto>> UpdateAsync(Guid id, UpdateRoleRequest request, CancellationToken ct = default);

    Task<Result> DeleteAsync(Guid id, CancellationToken ct = default);

    Task<Result<RoleDto>> SetPermissionsAsync(Guid id, IReadOnlyList<string> permissions, CancellationToken ct = default);
}

public interface IBusinessService
{
    Task<PagedResult<BusinessDto>> QueryAsync(BusinessQueryRequest request, CancellationToken ct = default);

    Task<Result<BusinessDto>> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<Result<BusinessDto>> CreateAsync(CreateBusinessRequest request, CancellationToken ct = default);

    Task<Result<BusinessDto>> UpdateAsync(Guid id, UpdateBusinessRequest request, CancellationToken ct = default);

    Task<Result<int>> DeleteAsync(IReadOnlyList<Guid> ids, CancellationToken ct = default);

    Task<BusinessStatsDto> GetStatsAsync(CancellationToken ct = default);
}

/// <summary>Writes the append-only security audit trail.</summary>
public interface IAuditService
{
    Task LogAsync(
        string action,
        string entityType,
        string? entityId = null,
        bool succeeded = true,
        object? details = null,
        CancellationToken ct = default);
}
