using System.Reflection;

namespace LeadMine.Application.Authorization;

/// <summary>
/// The complete catalogue of rights.
/// <para>
/// These constants are the single source of truth: the seeder reflects over them
/// to populate the Permissions table, and controllers reference them in
/// <c>[HasPermission]</c>. Adding a constant is all it takes to introduce a new
/// right — there is no second list to keep in sync.
/// </para>
/// </summary>
public static class Permissions
{
    [PermissionGroup("Leads")]
    public static class Leads
    {
        [PermissionDescription("View leads and lead statistics")]
        public const string View = "leads.view";

        [PermissionDescription("Create leads manually")]
        public const string Create = "leads.create";

        [PermissionDescription("Edit existing leads")]
        public const string Update = "leads.update";

        [PermissionDescription("Delete leads")]
        public const string Delete = "leads.delete";

        [PermissionDescription("Run email and WhatsApp verification")]
        public const string Verify = "leads.verify";

        [PermissionDescription("Export leads to Excel or CSV")]
        public const string Export = "leads.export";

        /// <summary>
        /// Without this, every lead query is silently scoped to the caller's own
        /// records. It is the single switch that separates a normal user from a
        /// super admin for lead visibility.
        /// </summary>
        [PermissionDescription("See leads scraped by every user, not just your own")]
        public const string ViewAll = "leads.view-all";
    }

    [PermissionGroup("Email")]
    public static class Email
    {
        [PermissionDescription("Send emails to leads using an approved preset")]
        public const string Send = "email.send";

        [PermissionDescription("View email presets available to you")]
        public const string ViewTemplates = "email.view-templates";

        [PermissionDescription("Create, edit and retire email presets")]
        public const string ManageTemplates = "email.manage-templates";

        [PermissionDescription("Manage email signatures")]
        public const string ManageSignatures = "email.manage-signatures";

        [PermissionDescription("View your own sent-email history")]
        public const string ViewOwnLog = "email.view-own-log";

        [PermissionDescription("View every user's sent-email history")]
        public const string ViewAllLogs = "email.view-all-logs";
    }

    [PermissionGroup("Searches")]
    public static class Searches
    {
        [PermissionDescription("View search runs and their history")]
        public const string View = "searches.view";

        [PermissionDescription("Start a new search run")]
        public const string Create = "searches.create";

        [PermissionDescription("Stop a running search")]
        public const string Stop = "searches.stop";

        [PermissionDescription("Delete search history entries")]
        public const string Delete = "searches.delete";
    }

    [PermissionGroup("Users")]
    public static class Users
    {
        [PermissionDescription("View user accounts")]
        public const string View = "users.view";

        [PermissionDescription("Create user accounts")]
        public const string Create = "users.create";

        [PermissionDescription("Edit user accounts")]
        public const string Update = "users.update";

        [PermissionDescription("Deactivate or delete user accounts")]
        public const string Delete = "users.delete";

        [PermissionDescription("Assign or remove roles for a user")]
        public const string ManageRoles = "users.manage-roles";

        [PermissionDescription("Grant or deny individual permissions for a user")]
        public const string ManagePermissions = "users.manage-permissions";

        [PermissionDescription("Reset another user's password")]
        public const string ResetPassword = "users.reset-password";
    }

    [PermissionGroup("Roles")]
    public static class Roles
    {
        [PermissionDescription("View roles and their permissions")]
        public const string View = "roles.view";

        [PermissionDescription("Create roles")]
        public const string Create = "roles.create";

        [PermissionDescription("Edit roles")]
        public const string Update = "roles.update";

        [PermissionDescription("Delete roles")]
        public const string Delete = "roles.delete";

        [PermissionDescription("Change which permissions a role grants")]
        public const string ManagePermissions = "roles.manage-permissions";
    }

    [PermissionGroup("Settings")]
    public static class Settings
    {
        [PermissionDescription("View application settings")]
        public const string View = "settings.view";

        [PermissionDescription("Change application settings, including API keys")]
        public const string Update = "settings.update";
    }

    [PermissionGroup("System")]
    public static class System
    {
        [PermissionDescription("View activity logs")]
        public const string ViewLogs = "system.view-logs";

        [PermissionDescription("View the security audit trail")]
        public const string ViewAudit = "system.view-audit";
    }

    /// <summary>
    /// Reflects the nested classes above into a flat, seedable list.
    /// </summary>
    public static IReadOnlyList<PermissionDefinition> All { get; } = Build();

    private static List<PermissionDefinition> Build()
    {
        var definitions = new List<PermissionDefinition>();

        foreach (var group in typeof(Permissions).GetNestedTypes(BindingFlags.Public | BindingFlags.Static))
        {
            var groupName = group.GetCustomAttribute<PermissionGroupAttribute>()?.Name ?? group.Name;

            var fields = group.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
                .Where(f => f is { IsLiteral: true, IsInitOnly: false } && f.FieldType == typeof(string));

            foreach (var field in fields)
            {
                if (field.GetRawConstantValue() is not string name) continue;

                definitions.Add(new PermissionDefinition(
                    name,
                    groupName,
                    field.GetCustomAttribute<PermissionDescriptionAttribute>()?.Description ?? name));
            }
        }

        return definitions.OrderBy(d => d.Group).ThenBy(d => d.Name).ToList();
    }
}

public sealed record PermissionDefinition(string Name, string Group, string Description);

[AttributeUsage(AttributeTargets.Class)]
public sealed class PermissionGroupAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}

[AttributeUsage(AttributeTargets.Field)]
public sealed class PermissionDescriptionAttribute(string description) : Attribute
{
    public string Description { get; } = description;
}

/// <summary>Built-in role names. Seeded and protected from deletion.</summary>
public static class RoleNames
{
    public const string Administrator = "Administrator";
    public const string Manager = "Manager";
    public const string Analyst = "Analyst";
    public const string Viewer = "Viewer";
}
