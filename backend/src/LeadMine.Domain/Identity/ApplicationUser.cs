using Microsoft.AspNetCore.Identity;

namespace LeadMine.Domain.Identity;

/// <summary>
/// Application user, keyed by <see cref="Guid"/> rather than string so ids are
/// non-guessable and stable across environments.
/// </summary>
public class ApplicationUser : IdentityUser<Guid>
{
    public string FirstName { get; set; } = string.Empty;

    public string LastName { get; set; } = string.Empty;

    public string FullName => $"{FirstName} {LastName}".Trim();

    /// <summary>
    /// Disables sign-in without deleting the account, so audit history and
    /// ownership of past records survive.
    /// </summary>
    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? LastLoginAt { get; set; }

    /// <summary>
    /// Invalidates every previously issued access token when bumped. Changing a
    /// password or revoking a role raises it, which is the only way to expire an
    /// already-signed JWT before its natural lifetime.
    /// </summary>
    public int SecurityVersion { get; set; }

    public ICollection<ApplicationUserRole> UserRoles { get; set; } = new List<ApplicationUserRole>();

    public ICollection<UserPermission> UserPermissions { get; set; } = new List<UserPermission>();

    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
}
