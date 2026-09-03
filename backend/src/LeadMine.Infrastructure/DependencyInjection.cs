using System.Net;
using System.Security.Cryptography;
using System.Text;
using LeadMine.Application.Interfaces;
using LeadMine.Domain.Identity;
using LeadMine.Infrastructure.Authorization;
using LeadMine.Infrastructure.Email;
using LeadMine.Infrastructure.Identity;
using LeadMine.Infrastructure.Persistence;
using LeadMine.Infrastructure.Scraping;
using LeadMine.Infrastructure.Scraping.Enrichment;
using LeadMine.Infrastructure.Scraping.Providers;
using LeadMine.Infrastructure.Scraping.Verification;
using LeadMine.Infrastructure.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace LeadMine.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection is not configured.");

        services.AddDbContext<LeadMineDbContext>(options =>
        {
            options.UseSqlServer(connectionString, sql =>
            {
                sql.MigrationsAssembly(typeof(LeadMineDbContext).Assembly.FullName);
                // Survive transient network/failover blips without the caller
                // having to implement retry logic.
                sql.EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(10), errorNumbersToAdd: null);
            });
        });

        services.AddIdentityCore<ApplicationUser>(options =>
            {
                options.Password.RequiredLength = 8;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = true;

                options.User.RequireUniqueEmail = true;

                // Throttle brute-force attempts.
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.Lockout.AllowedForNewUsers = true;
            })
            .AddRoles<ApplicationRole>()
            .AddEntityFrameworkStores<LeadMineDbContext>()
            .AddSignInManager()
            .AddDefaultTokenProviders();

        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .ValidateDataAnnotations();
            // Deliberately no .ValidateOnStart(): the rest of the API (search
            // jobs, leads, the scraper worker) has nothing to do with login and
            // must still come up if Jwt:Key is missing, blank, or mid-rotation.
            // TokenService reads these same options when it actually signs a
            // token, so a bad key still surfaces immediately — at the first
            // login attempt, as a clean error, instead of at boot as a crash.

        // A second, tolerant read purely to wire up the bearer-auth middleware
        // below, which needs a concrete signing key *now* (at service
        // registration) rather than lazily. A missing/short key must not throw
        // here — see above — so it falls back to a key nothing was ever signed
        // with. Every real request still gets checked against it; there just
        // won't be a valid token to present until Jwt:Key is actually set, which
        // is the correct fail-closed behaviour (auth off, not auth-crashes-app).
        var jwtSection = configuration.GetSection(JwtOptions.SectionName);
        var trimmedKey = jwtSection[nameof(JwtOptions.Key)]?.Trim();
        var jwtIssuer = jwtSection[nameof(JwtOptions.Issuer)] is { Length: > 0 } issuer ? issuer : "LeadMine.Api";
        var jwtAudience = jwtSection[nameof(JwtOptions.Audience)] is { Length: > 0 } audience ? audience : "LeadMine.Client";

        var signingKeyBytes = trimmedKey is { Length: >= 32 }
            ? Encoding.UTF8.GetBytes(trimmedKey)
            : RandomNumberGenerator.GetBytes(32);

        if (trimmedKey is not { Length: >= 32 })
        {
            // No ILogger yet at this point in startup (the service provider
            // isn't built), and this project has no Serilog dependency of its
            // own — Console is the one sink guaranteed to reach every hosting
            // environment's captured output.
            var problem = string.IsNullOrEmpty(trimmedKey) ? "not configured" : "shorter than 32 characters";

            Console.Error.WriteLine(
                $"WARNING: Jwt:Key is {problem} — the API is starting anyway, but login and every " +
                "authenticated request will fail until a real 32+ character key is set " +
                "(Jwt__Key env var or user-secrets).");
        }

        services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(options =>
            {
                options.SaveToken = true;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwtIssuer,
                    ValidateAudience = true,
                    ValidAudience = jwtAudience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(signingKeyBytes),
                    ValidateLifetime = true,
                    // No grace period on expiry; the default 5 minutes would keep
                    // a revoked token alive well past its stated lifetime.
                    ClockSkew = TimeSpan.Zero,
                };

                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = async context =>
                    {
                        // Re-check the user on every request so deactivation and
                        // permission changes take effect immediately, rather than
                        // whenever the access token happens to expire.
                        var users = context.HttpContext.RequestServices
                            .GetRequiredService<UserManager<ApplicationUser>>();

                        var subject = context.Principal?.FindFirst("sub")?.Value
                                      ?? context.Principal?.FindFirst(
                                          System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

                        if (subject is null || !Guid.TryParse(subject, out var userId))
                        {
                            context.Fail("Token is missing a valid subject.");
                            return;
                        }

                        var user = await users.FindByIdAsync(userId.ToString());

                        if (user is null || !user.IsActive)
                        {
                            context.Fail("This account is no longer active.");
                            return;
                        }

                        var version = context.Principal?.FindFirst(LeadMineClaims.SecurityVersion)?.Value;
                        if (!int.TryParse(version, out var tokenVersion) || tokenVersion != user.SecurityVersion)
                        {
                            context.Fail("This session is no longer valid. Please sign in again.");
                        }
                    },
                };
            });

        // Turns [HasPermission("x")] into a policy without registering each by hand.
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();
        services.AddAuthorization();

        services.AddHttpContextAccessor();

        services.AddScoped<ICurrentUser, CurrentUserService>();
        services.AddScoped<ITokenService, TokenService>();
        services.AddScoped<IPermissionService, PermissionService>();
        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IRoleService, RoleService>();
        services.AddScoped<IBusinessService, BusinessService>();
        services.AddScoped<IPersonService, PersonService>();

        services.AddOptions<SmtpOptions>()
            .Bind(configuration.GetSection(SmtpOptions.SectionName));

        services.AddScoped<IEmailSender, SmtpEmailSender>();
        services.AddScoped<IEmailTemplateService, EmailTemplateService>();
        services.AddScoped<IEmailService, EmailService>();
        services.AddScoped<IWhatsAppTemplateService, WhatsAppTemplateService>();
        services.AddScoped<IWhatsAppService, WhatsAppService>();
        services.AddScoped<ILeadIngestService, LeadIngestService>();
        services.AddScoped<ISearchJobService, SearchJobService>();

        // Recovers jobs whose worker died. Hosted in the API because that is the
        // process guaranteed to be running — in a separate worker it would die
        // with the very failure it exists to recover from.
        services.AddHostedService<StaleJobReaperService>();

        services.AddScraping(configuration);

        return services;
    }

    /// <summary>
    /// Registers the scraping engine and the in-process worker that runs it.
    /// <para>
    /// The scraper lives inside the API because the deployment target is a .NET
    /// application host — there is no second process to put it in. Everything
    /// here is therefore sized to share the machine with request handling.
    /// </para>
    /// </summary>
    private static IServiceCollection AddScraping(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<ScraperOptions>()
            .Bind(configuration.GetSection(ScraperOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        var scraper = configuration.GetSection(ScraperOptions.SectionName).Get<ScraperOptions>()
            ?? new ScraperOptions();

        // Calls to search providers. The timeout is sized for the slowest of
        // them — a loaded Overpass mirror — because HttpClient.Timeout is a hard
        // per-client ceiling. Quicker providers narrow it per request.
        services.AddHttpClient(ScraperHttpClients.Provider, client =>
            {
                client.Timeout = ScraperHttpClients.ProviderClientTimeout;
                client.DefaultRequestHeaders.UserAgent.ParseAdd(scraper.UserAgent);
                client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                ConnectCallback = ScraperConnect.ConnectAsync,
                AutomaticDecompression = DecompressionMethods.All,
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            });

        // Crawling business websites. Redirects are capped and compression is on:
        // this client follows links found in untrusted pages.
        services.AddHttpClient(ScraperHttpClients.Crawler, client =>
            {
                client.Timeout = TimeSpan.FromMilliseconds(scraper.RequestTimeoutMs);
                client.DefaultRequestHeaders.UserAgent.ParseAdd(scraper.UserAgent);
                client.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml");
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                ConnectCallback = ScraperConnect.ConnectAsync,
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = 4,
                AutomaticDecompression = DecompressionMethods.All,
                // Business sites are frequently misconfigured. A bad certificate
                // is a reason to skip a lead, not to trust it — so validation
                // stays on and the fetch simply fails.
                UseCookies = false,
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            });

        services.AddSingleton<GooglePlacesProvider>();
        services.AddSingleton<OpenStreetMapProvider>();
        services.AddSingleton<ProviderRegistry>();
        services.AddSingleton<GeocodingService>();

        services.AddSingleton<RobotsCache>();
        services.AddSingleton<EnrichmentService>();
        services.AddSingleton<EmailVerifier>();
        services.AddSingleton<VerificationService>();

        // Email validation pipeline. Bound but not .ValidateOnStart() — like
        // SmtpOptions, the bounce-check mailbox credentials are optional (that
        // pass stays off until explicitly enabled and configured), so a blank
        // value here must not crash the app.
        services.AddOptions<EmailValidationOptions>()
            .Bind(configuration.GetSection(EmailValidationOptions.SectionName))
            .ValidateDataAnnotations();

        services.AddSingleton<SmtpProbe>();
        services.AddSingleton<SmtpProbeDomainRateLimiter>();
        services.AddSingleton<EmailValidationPipeline>();
        services.AddHostedService<EmailValidationWorkerService>();
        services.AddHostedService<EmailSmtpProbeWorkerService>();
        services.AddHostedService<EmailBounceCheckWorkerService>();
        services.AddHostedService<EmailBounceProcessorService>();

        // Owns the one shared Chromium process for every Playwright-backed
        // provider. Registered as its own interface-free singleton (not
        // AddHostedService) because it has no background work of its own to
        // run — only a browser to close, via DI's own singleton disposal on
        // shutdown, since IAsyncDisposable is honoured automatically.
        services.AddSingleton<Scraping.Browser.PlaywrightBrowserManager>();
        services.AddSingleton<PlaywrightGoogleMapsProvider>();
        services.AddSingleton<PlaywrightMapsEnrichmentService>();
        services.AddSingleton<Scraping.Browser.LinkedInSessionManager>();
        services.AddSingleton<PlaywrightLinkedInCompanyService>();
        services.AddSingleton<PlaywrightLinkedInPeopleService>();

        // Scoped: the runner opens its own short-lived scopes for database work,
        // so it must not outlive the scope the worker resolves it from.
        services.AddScoped<SearchRunner>();
        services.AddScoped<LinkedInEnrichmentRunner>();
        services.AddScoped<LinkedInPeopleSearchRunner>();
        services.AddScoped<Email.EmailSendRunner>();

        services.AddHostedService<ScraperWorkerService>();

        return services;
    }
}
