using System.Security.Claims;
using LeadMine.Application.Interfaces;
using LeadMine.Infrastructure.Authorization;
using Microsoft.AspNetCore.Http;

namespace LeadMine.Infrastructure.Services;

public sealed class CurrentUserService(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? User => accessor.HttpContext?.User;

    public Guid? UserId
    {
        get
        {
            var value = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                        ?? User?.FindFirst("sub")?.Value;

            return Guid.TryParse(value, out var id) ? id : null;
        }
    }

    public string? Email =>
        User?.FindFirst(ClaimTypes.Email)?.Value ?? User?.FindFirst("email")?.Value;

    public bool IsAuthenticated => User?.Identity?.IsAuthenticated ?? false;

    /// <summary>
    /// Prefers the proxy-forwarded client address, since behind a load balancer
    /// the socket address is the proxy's and would make every audit row identical.
    /// </summary>
    public string? IpAddress
    {
        get
        {
            var context = accessor.HttpContext;
            if (context is null) return null;

            var forwarded = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(forwarded))
            {
                return forwarded.Split(',')[0].Trim();
            }

            return context.Connection.RemoteIpAddress?.ToString();
        }
    }

    public string? UserAgent =>
        accessor.HttpContext?.Request.Headers.UserAgent.FirstOrDefault();

    public IReadOnlyList<string> Permissions =>
        User?.FindAll(LeadMineClaims.Permission).Select(c => c.Value).ToList() ?? [];

    public bool HasPermission(string permission) =>
        Permissions.Contains(permission, StringComparer.OrdinalIgnoreCase);
}
