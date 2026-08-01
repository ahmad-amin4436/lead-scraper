namespace LeadMine.Domain.Identity;

/// <summary>
/// A single right, named <c>group.action</c> (e.g. <c>leads.delete</c>).
/// <para>
/// Authorization checks permissions, never role names. Roles are just bundles of
/// permissions, so a deployment can re-shape roles without touching a single
/// <c>[HasPermission]</c> attribute.
/// </para>
/// </summary>
public class Permission
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Canonical name, e.g. <c>leads.export</c>. Unique.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Grouping for the admin UI, e.g. <c>Leads</c>.</summary>
    public string Group { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();

    public ICollection<UserPermission> UserPermissions { get; set; } = new List<UserPermission>();
}

/// <summary>Grants a permission to every member of a role.</summary>
public class RolePermission
{
    public Guid RoleId { get; set; }

    public ApplicationRole Role { get; set; } = null!;

    public Guid PermissionId { get; set; }

    public Permission Permission { get; set; } = null!;

    public DateTimeOffset GrantedAt { get; set; } = DateTimeOffset.UtcNow;

    public Guid? GrantedBy { get; set; }
}

/// <summary>
/// Per-user override on top of whatever their roles grant.
/// <para>
/// <see cref="IsGranted"/> false is an explicit DENY and always wins over a role
/// grant — that asymmetry is deliberate, so revoking a sensitive right from one
/// person never depends on auditing every role they hold.
/// </para>
/// </summary>
public class UserPermission
{
    public Guid UserId { get; set; }

    public ApplicationUser User { get; set; } = null!;

    public Guid PermissionId { get; set; }

    public Permission Permission { get; set; } = null!;

    public bool IsGranted { get; set; } = true;

    public DateTimeOffset GrantedAt { get; set; } = DateTimeOffset.UtcNow;

    public Guid? GrantedBy { get; set; }
}
