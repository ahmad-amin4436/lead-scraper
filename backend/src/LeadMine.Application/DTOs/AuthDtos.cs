using System.ComponentModel.DataAnnotations;

namespace LeadMine.Application.DTOs;

public sealed class RegisterRequest
{
    [Required, EmailAddress, MaxLength(256)]
    public string Email { get; set; } = string.Empty;

    [Required, MinLength(8), MaxLength(128)]
    public string Password { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string FirstName { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string LastName { get; set; } = string.Empty;
}

public sealed class LoginRequest
{
    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string Password { get; set; } = string.Empty;
}

public sealed class RefreshTokenRequest
{
    [Required]
    public string RefreshToken { get; set; } = string.Empty;
}

public sealed class ChangePasswordRequest
{
    [Required]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required, MinLength(8), MaxLength(128)]
    public string NewPassword { get; set; } = string.Empty;
}

public sealed class ResetPasswordRequest
{
    [Required]
    public Guid UserId { get; set; }

    [Required, MinLength(8), MaxLength(128)]
    public string NewPassword { get; set; } = string.Empty;
}

/// <summary>
/// Issued token pair. The refresh token is returned exactly once, in this
/// response — only its hash is stored server-side.
/// </summary>
public sealed class AuthResponse
{
    public string AccessToken { get; set; } = string.Empty;

    public string RefreshToken { get; set; } = string.Empty;

    public DateTimeOffset ExpiresAt { get; set; }

    public string TokenType { get; set; } = "Bearer";

    public UserDto User { get; set; } = new();
}

public sealed class UserDto
{
    public Guid Id { get; set; }

    public string Email { get; set; } = string.Empty;

    public string FirstName { get; set; } = string.Empty;

    public string LastName { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;

    public bool IsActive { get; set; }

    public bool EmailConfirmed { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? LastLoginAt { get; set; }

    public IReadOnlyList<string> Roles { get; set; } = Array.Empty<string>();

    /// <summary>Effective rights after role grants and per-user overrides.</summary>
    public IReadOnlyList<string> Permissions { get; set; } = Array.Empty<string>();
}

public sealed class CreateUserRequest
{
    [Required, EmailAddress, MaxLength(256)]
    public string Email { get; set; } = string.Empty;

    [Required, MinLength(8), MaxLength(128)]
    public string Password { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string FirstName { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string LastName { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public List<string> Roles { get; set; } = new();
}

public sealed class UpdateUserRequest
{
    [MaxLength(100)]
    public string? FirstName { get; set; }

    [MaxLength(100)]
    public string? LastName { get; set; }

    public bool? IsActive { get; set; }
}

public sealed class AssignRolesRequest
{
    /// <summary>The complete set of roles the user should end up with.</summary>
    public List<string> Roles { get; set; } = new();
}

public sealed class UserPermissionOverrideRequest
{
    [Required]
    public string Permission { get; set; } = string.Empty;

    /// <summary>False records an explicit DENY, which overrides role grants.</summary>
    public bool IsGranted { get; set; } = true;
}
