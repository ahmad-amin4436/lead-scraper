using Microsoft.AspNetCore.Identity;

namespace LeadMine.Domain.Identity;

public class ApplicationRole : IdentityRole<Guid>
{
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// System roles (e.g. Administrator) cannot be renamed or deleted, so the
    /// application can never be locked out of its own admin surface.
    /// </summary>
    public bool IsSystemRole { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<ApplicationUserRole> UserRoles { get; set; } = new List<ApplicationUserRole>();

    public ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();
}

/// <summary>
/// Join entity for the user/role many-to-many. Declared explicitly (rather than
/// relying on the default <c>IdentityUserRole</c>) so both navigations exist and
/// permissions can be resolved in a single query.
/// </summary>
public class ApplicationUserRole : IdentityUserRole<Guid>
{
    public ApplicationUser User { get; set; } = null!;

    public ApplicationRole Role { get; set; } = null!;

    public DateTimeOffset AssignedAt { get; set; } = DateTimeOffset.UtcNow;

    public Guid? AssignedBy { get; set; }
}
