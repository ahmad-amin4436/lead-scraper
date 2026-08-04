using System.Net;
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
            .ValidateDataAnnotations()
            // Fail at startup, not on the first login attempt.
            .ValidateOnStart();

        var jwt = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
            ?? throw new InvalidOperationException("The Jwt configuration section is missing.");

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
                    ValidIssuer = jwt.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwt.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
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

        // Apify platform calls (start run, poll status, fetch dataset). Each
        // individual call is quick — the actor run itself is paced out as a
        // poll loop in ApifyGoogleMapsProvider, not held open on this client.
        services.AddHttpClient(ScraperHttpClients.Apify, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(60);
                client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                ConnectCallback = ScraperConnect.ConnectAsync,
                AutomaticDecompression = DecompressionMethods.All,
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            });

        services.AddSingleton<GooglePlacesProvider>();
        services.AddSingleton<OpenStreetMapProvider>();
        services.AddSingleton<ApifyGoogleMapsProvider>();
        services.AddSingleton<ProviderRegistry>();
        services.AddSingleton<GeocodingService>();

        services.AddSingleton<RobotsCache>();
        services.AddSingleton<EnrichmentService>();
        services.AddSingleton<EmailVerifier>();
        services.AddSingleton<VerificationService>();

        // Scoped: the runner opens its own short-lived scopes for database work,
        // so it must not outlive the scope the worker resolves it from.
        services.AddScoped<SearchRunner>();

        services.AddHostedService<ScraperWorkerService>();

        return services;
    }
}
