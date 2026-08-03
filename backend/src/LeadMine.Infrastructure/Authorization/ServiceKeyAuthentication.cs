using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LeadMine.Infrastructure.Authorization;

/// <summary>
/// Restricts an endpoint to the trusted scraper worker.
/// <para>
/// The worker has no user session — it runs as a background process — so it
/// cannot present a JWT. It authenticates with a shared secret in
/// <c>X-Service-Key</c> instead, which is why endpoints carrying this attribute
/// may set the lead owner explicitly while user-facing ones never can.
/// </para>
/// <para>
/// Fails closed: with no key configured the endpoint 404s rather than accepting
/// anonymous writes.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequireServiceKeyAttribute : Attribute, IAsyncAuthorizationFilter
{
    public const string HeaderName = "X-Service-Key";

    private const string ConfigKey = "ServiceKey";

    public Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var configuration = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
        var logger = context.HttpContext.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger(nameof(RequireServiceKeyAttribute));

        var expected = configuration[ConfigKey];

        if (string.IsNullOrWhiteSpace(expected))
        {
            logger.LogWarning(
                "ServiceKey is not configured, so the ingest endpoint is disabled. " +
                "Set ServiceKey in configuration to enable the scraper worker.");

            context.Result = new NotFoundResult();
            return Task.CompletedTask;
        }

        var provided = context.HttpContext.Request.Headers[HeaderName].FirstOrDefault();

        if (string.IsNullOrWhiteSpace(provided) || !FixedTimeEquals(provided, expected))
        {
            context.Result = new UnauthorizedObjectResult(new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "Unauthorized",
                Detail = "A valid service key is required for this endpoint.",
            });
        }

        return Task.CompletedTask;
    }

    /// <summary>Constant-time, so response timing reveals nothing about the key.</summary>
    private static bool FixedTimeEquals(string left, string right) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(left),
            Encoding.UTF8.GetBytes(right));
}
