namespace LeadMine.Api.Configuration;

/// <summary>
/// Controls the OpenAPI document and Swagger UI.
/// <para>
/// Swagger runs in every environment, because a deployed API is exactly where an
/// explorable contract is most useful. Outside Development it is protected by
/// HTTP Basic credentials by default: the document lists every endpoint, payload
/// shape and permission name, which is a map of the attack surface for anyone
/// who finds the URL.
/// </para>
/// </summary>
public sealed class SwaggerOptions
{
    public const string SectionName = "Swagger";

    /// <summary>Master switch. Set false to remove Swagger entirely.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Require HTTP Basic credentials for the UI and the JSON document outside
    /// Development. Setting this to false publishes the contract anonymously —
    /// only do that for an API whose surface is meant to be public.
    /// </summary>
    public bool RequireAuthentication { get; set; } = true;

    public string? Username { get; set; }

    public string? Password { get; set; }

    /// <summary>Path the UI is served from, without slashes.</summary>
    public string RoutePrefix { get; set; } = "swagger";

    /// <summary>
    /// Public base URLs advertised in the document. Needed behind a reverse
    /// proxy or load balancer, where the request URL the app sees is not the URL
    /// the browser used — without it "Try it out" posts to the wrong host.
    /// </summary>
    public List<SwaggerServer> Servers { get; set; } = new();

    /// <summary>
    /// True when the UI should be reachable without credentials: either in
    /// Development, or when protection was explicitly switched off.
    /// </summary>
    public bool AllowsAnonymousAccess(bool isDevelopment) =>
        isDevelopment || !RequireAuthentication;

    /// <summary>
    /// Credentials must exist before Basic auth can guard anything. Missing
    /// values are treated as a misconfiguration and fail closed rather than
    /// silently publishing the document.
    /// </summary>
    public bool HasCredentials =>
        !string.IsNullOrWhiteSpace(Username) && !string.IsNullOrWhiteSpace(Password);
}

public sealed class SwaggerServer
{
    public string Url { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;
}
