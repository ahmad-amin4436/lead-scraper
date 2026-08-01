using LeadMine.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LeadMine.Infrastructure.Authorization;

/// <summary>Claim type carrying a single granted right inside the JWT.</summary>
public static class LeadMineClaims
{
    public const string Permission = "permission";

    /// <summary>Mirrors <c>ApplicationUser.SecurityVersion</c> for token invalidation.</summary>
    public const string SecurityVersion = "sec_ver";
}

/// <summary>
/// Requires a named permission. Used via <see cref="HasPermissionAttribute"/>
/// rather than constructed directly.
/// </summary>
public sealed class PermissionRequirement(string permission) : IAuthorizationRequirement
{
    public string Permission { get; } = permission;
}

/// <summary>
/// Declares the permission an endpoint needs:
/// <code>[HasPermission(Permissions.Leads.Delete)]</code>
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class HasPermissionAttribute(string permission)
    : AuthorizeAttribute($"{PermissionPolicyProvider.Prefix}{permission}")
{
    public string Permission { get; } = permission;
}

/// <summary>
/// Manufactures a policy per permission on demand.
/// <para>
/// Without this, every one of the ~25 permissions would need registering by hand
/// in <c>AddAuthorization</c>, and adding a right would mean editing two places.
/// </para>
/// </summary>
public sealed class PermissionPolicyProvider : IAuthorizationPolicyProvider
{
    public const string Prefix = "perm:";

    private readonly DefaultAuthorizationPolicyProvider _fallback;

    public PermissionPolicyProvider(IOptions<AuthorizationOptions> options)
    {
        _fallback = new DefaultAuthorizationPolicyProvider(options);
    }

    public Task<AuthorizationPolicy> GetDefaultPolicyAsync() => _fallback.GetDefaultPolicyAsync();

    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() => _fallback.GetFallbackPolicyAsync();

    public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (!policyName.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            return _fallback.GetPolicyAsync(policyName);
        }

        var permission = policyName[Prefix.Length..];

        var policy = new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme)
            .RequireAuthenticatedUser()
            .AddRequirements(new PermissionRequirement(permission))
            .Build();

        return Task.FromResult<AuthorizationPolicy?>(policy);
    }
}

/// <summary>
/// Decides whether the caller holds a permission.
/// <para>
/// The JWT already carries the user's rights, so the common path is a pure claim
/// check with no database round-trip. It falls back to a live lookup only when
/// the token has no permission claims at all, which keeps a token minted before
/// a permission change from silently failing closed forever.
/// </para>
/// </summary>
public sealed class PermissionAuthorizationHandler(IServiceScopeFactory scopeFactory)
    : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true) return;

        var claims = context.User.FindAll(LeadMineClaims.Permission).Select(c => c.Value).ToList();

        if (claims.Count > 0)
        {
            if (claims.Contains(requirement.Permission, StringComparer.OrdinalIgnoreCase))
            {
                context.Succeed(requirement);
            }

            return;
        }

        var subject = context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)
                      ?? context.User.FindFirst("sub");

        if (subject is null || !Guid.TryParse(subject.Value, out var userId)) return;

        using var scope = scopeFactory.CreateScope();
        var permissions = scope.ServiceProvider.GetRequiredService<IPermissionService>();

        if (await permissions.HasPermissionAsync(userId, requirement.Permission))
        {
            context.Succeed(requirement);
        }
    }
}
