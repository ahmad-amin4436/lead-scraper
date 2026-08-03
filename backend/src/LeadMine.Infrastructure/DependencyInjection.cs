using System.Text;
using LeadMine.Application.Interfaces;
using LeadMine.Domain.Identity;
using LeadMine.Infrastructure.Authorization;
using LeadMine.Infrastructure.Email;
using LeadMine.Infrastructure.Identity;
using LeadMine.Infrastructure.Persistence;
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
        services.AddScoped<ILeadIngestService, LeadIngestService>();
        services.AddScoped<ISearchJobService, SearchJobService>();

        // Recovers jobs whose worker died. Hosted in the API because that is the
        // process guaranteed to be running — in the worker it would die with the
        // very failure it exists to recover from.
        services.AddHostedService<StaleJobReaperService>();

        return services;
    }
}
