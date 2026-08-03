using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using LeadMine.Api.Configuration;
using Microsoft.Extensions.Options;

namespace LeadMine.Api.Middleware;

/// <summary>
/// Guards the Swagger UI and the OpenAPI document with HTTP Basic credentials.
/// <para>
/// This deliberately does not reuse the JWT scheme: the browser needs to load the
/// UI before it can obtain a token, so bearer auth would be circular. Basic is
/// the right primitive for a static documentation endpoint — over HTTPS it is
/// adequate, and it keeps the contract off the open internet.
/// </para>
/// </summary>
public sealed class SwaggerAuthenticationMiddleware(
    RequestDelegate next,
    IOptions<SwaggerOptions> options,
    IHostEnvironment environment,
    ILogger<SwaggerAuthenticationMiddleware> logger)
{
    private readonly SwaggerOptions _options = options.Value;

    public async Task InvokeAsync(HttpContext context)
    {
        if (!IsSwaggerRequest(context.Request.Path) ||
            _options.AllowsAnonymousAccess(environment.IsDevelopment()))
        {
            await next(context);
            return;
        }

        if (!_options.HasCredentials)
        {
            // Fail closed. Serving the document because someone forgot to set a
            // password would be the exact failure this guard exists to prevent.
            logger.LogWarning(
                "Swagger is protected but Swagger:Username/Password are not configured; refusing the request.");

            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        if (TryValidate(context.Request.Headers.Authorization))
        {
            await next(context);
            return;
        }

        // The realm prompts the browser for credentials rather than showing a
        // bare 401 body.
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.Headers.WWWAuthenticate = "Basic realm=\"LeadMine API docs\", charset=\"UTF-8\"";
    }

    private bool IsSwaggerRequest(PathString path)
    {
        if (!path.HasValue) return false;

        var prefix = _options.RoutePrefix.Trim('/');

        // Covers the UI ("/swagger"), its assets, and the generated JSON/YAML.
        return path.StartsWithSegments($"/{prefix}", StringComparison.OrdinalIgnoreCase);
    }

    private bool TryValidate(string? headerValue)
    {
        if (string.IsNullOrWhiteSpace(headerValue)) return false;

        if (!AuthenticationHeaderValue.TryParse(headerValue, out var header) ||
            !"Basic".Equals(header.Scheme, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(header.Parameter))
        {
            return false;
        }

        string decoded;
        try
        {
            decoded = Encoding.UTF8.GetString(Convert.FromBase64String(header.Parameter));
        }
        catch (FormatException)
        {
            return false;
        }

        var separator = decoded.IndexOf(':');
        if (separator < 0) return false;

        var user = decoded[..separator];
        var password = decoded[(separator + 1)..];

        // Both comparisons always run and are constant-time, so response timing
        // reveals neither which field was wrong nor how much of it matched.
        var userOk = FixedTimeEquals(user, _options.Username!);
        var passwordOk = FixedTimeEquals(password, _options.Password!);

        return userOk && passwordOk;
    }

    private static bool FixedTimeEquals(string left, string right) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(left),
            Encoding.UTF8.GetBytes(right));
}
