using LeadMine.Application.Authorization;
using LeadMine.Domain.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LeadMine.Infrastructure.Persistence;

/// <summary>
/// Brings the database in line with the compiled permission catalogue and makes
/// sure a usable administrator exists.
/// <para>
/// Idempotent by design: it runs on every startup, adds what is missing, and
/// leaves existing data alone. That keeps a deployed environment in sync when a
/// new permission constant is introduced, without a manual data migration.
/// </para>
/// </summary>
public static class DbSeeder
{
    public static async Task SeedAsync(IServiceProvider services, CancellationToken ct = default)
    {
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(DbSeeder));
        var db = services.GetRequiredService<LeadMineDbContext>();
        var roleManager = services.GetRequiredService<RoleManager<ApplicationRole>>();
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var configuration = services.GetRequiredService<IConfiguration>();

        await SeedPermissionsAsync(db, logger, ct);
        await SeedRolesAsync(db, roleManager, logger, ct);
        await SeedAdminAsync(userManager, configuration, logger);
    }

    private static async Task SeedPermissionsAsync(LeadMineDbContext db, ILogger logger, CancellationToken ct)
    {
        var existing = await db.Permissions.ToDictionaryAsync(p => p.Name, p => p, StringComparer.OrdinalIgnoreCase, ct);
        var added = 0;

        foreach (var definition in Permissions.All)
        {
            if (existing.TryGetValue(definition.Name, out var current))
            {
                // Keep descriptions/groups current when the catalogue is edited.
                if (current.Description != definition.Description || current.Group != definition.Group)
                {
                    current.Description = definition.Description;
                    current.Group = definition.Group;
                }

                continue;
            }

            db.Permissions.Add(new Permission
            {
                Name = definition.Name,
                Group = definition.Group,
                Description = definition.Description,
            });

            added++;
        }

        if (db.ChangeTracker.HasChanges())
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Seeded {Added} new permission(s); {Total} total", added, Permissions.All.Count);
        }
    }

    /// <summary>
    /// Default role/permission matrix. Administrator always receives every
    /// permission — including ones added later — so the role can never fall
    /// behind the catalogue and lock admins out of new features.
    /// </summary>
    private static readonly Dictionary<string, (string Description, string[] Permissions)> RoleMatrix = new()
    {
        [RoleNames.Administrator] = (
            "Full access to every feature, including user and role administration.",
            []),

        [RoleNames.Manager] = (
            "Runs searches and manages the lead database, and can see every user's leads and email history.",
            [
                Permissions.Leads.View, Permissions.Leads.Create, Permissions.Leads.Update,
                Permissions.Leads.Delete, Permissions.Leads.Verify, Permissions.Leads.Export,
                // Team oversight without full user/role administration.
                Permissions.Leads.ViewAll,
                Permissions.Searches.View, Permissions.Searches.Create, Permissions.Searches.Stop,
                Permissions.Searches.Delete,
                Permissions.Settings.View,
                Permissions.System.ViewLogs,
                Permissions.Users.View,
                Permissions.Email.Send, Permissions.Email.ViewTemplates,
                Permissions.Email.ManageTemplates, Permissions.Email.ManageSignatures,
                Permissions.Email.ViewOwnLog, Permissions.Email.ViewAllLogs,
                Permissions.WhatsApp.Send, Permissions.WhatsApp.ViewTemplates,
                Permissions.WhatsApp.ManageTemplates,
                Permissions.People.View, Permissions.People.Manage, Permissions.People.ViewAll,
            ]),

        [RoleNames.Analyst] = (
            "Works their own leads and runs searches. Cannot see other users' data.",
            [
                Permissions.Leads.View, Permissions.Leads.Create, Permissions.Leads.Update,
                Permissions.Leads.Verify, Permissions.Leads.Export,
                Permissions.Searches.View, Permissions.Searches.Create, Permissions.Searches.Stop,
                Permissions.Settings.View,
                Permissions.Email.Send, Permissions.Email.ViewTemplates, Permissions.Email.ViewOwnLog,
                Permissions.WhatsApp.Send, Permissions.WhatsApp.ViewTemplates,
                Permissions.People.View,
            ]),

        [RoleNames.Viewer] = (
            "Read-only access to their own leads and search history.",
            [
                Permissions.Leads.View,
                Permissions.Searches.View,
            ]),
    };

    private static async Task SeedRolesAsync(
        LeadMineDbContext db,
        RoleManager<ApplicationRole> roleManager,
        ILogger logger,
        CancellationToken ct)
    {
        var allPermissions = await db.Permissions.ToListAsync(ct);

        foreach (var (roleName, (description, permissionNames)) in RoleMatrix)
        {
            var role = await roleManager.FindByNameAsync(roleName);

            if (role is null)
            {
                role = new ApplicationRole
                {
                    Name = roleName,
                    Description = description,
                    IsSystemRole = true,
                };

                var created = await roleManager.CreateAsync(role);
                if (!created.Succeeded)
                {
                    logger.LogError("Could not create role {Role}: {Errors}",
                        roleName, string.Join("; ", created.Errors.Select(e => e.Description)));
                    continue;
                }

                logger.LogInformation("Created role {Role}", roleName);
            }

            // Administrator is granted everything, always.
            var target = roleName == RoleNames.Administrator
                ? allPermissions
                : allPermissions.Where(p => permissionNames.Contains(p.Name, StringComparer.OrdinalIgnoreCase)).ToList();

            var current = await db.RolePermissions
                .Where(rp => rp.RoleId == role.Id)
                .Select(rp => rp.PermissionId)
                .ToListAsync(ct);

            // Only ever ADD here: an operator who deliberately narrowed a
            // built-in role should not have that undone on the next restart.
            var missing = target.Where(p => !current.Contains(p.Id)).ToList();

            foreach (var permission in missing)
            {
                db.RolePermissions.Add(new RolePermission
                {
                    RoleId = role.Id,
                    PermissionId = permission.Id,
                });
            }

            if (missing.Count > 0)
            {
                await db.SaveChangesAsync(ct);
                logger.LogInformation("Granted {Count} permission(s) to {Role}", missing.Count, roleName);
            }
        }
    }

    private static async Task SeedAdminAsync(
        UserManager<ApplicationUser> userManager,
        IConfiguration configuration,
        ILogger logger)
    {
        var email = configuration["Seed:AdminEmail"] ?? "admin@leadmine.local";
        var password = configuration["Seed:AdminPassword"];

        // Never invent a password: a silent default would be a published
        // credential. Absent configuration, no admin is created.
        if (string.IsNullOrWhiteSpace(password))
        {
            logger.LogWarning(
                "Seed:AdminPassword is not configured, so no administrator was created. " +
                "Set it in configuration or user-secrets, then restart.");
            return;
        }

        if (await userManager.FindByEmailAsync(email) is not null) return;

        var admin = new ApplicationUser
        {
            UserName = email,
            Email = email,
            FirstName = "System",
            LastName = "Administrator",
            EmailConfirmed = true,
            IsActive = true,
        };

        var created = await userManager.CreateAsync(admin, password);
        if (!created.Succeeded)
        {
            logger.LogError("Could not create the administrator: {Errors}",
                string.Join("; ", created.Errors.Select(e => e.Description)));
            return;
        }

        await userManager.AddToRoleAsync(admin, RoleNames.Administrator);
        logger.LogInformation("Created administrator {Email}", email);
    }
}
