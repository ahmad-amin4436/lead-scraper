using System.Reflection;
using System.Text.Json.Serialization;
using LeadMine.Api.Configuration;
using LeadMine.Api.Middleware;
using LeadMine.Infrastructure;
using LeadMine.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console());

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        // Enums travel as their names, so clients aren't coupled to numeric
        // values that would shift if the enum is ever reordered.
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    });

// Return a consistent problem-details body for model-binding failures too.
builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var errors = context.ModelState
            .Where(e => e.Value?.Errors.Count > 0)
            .ToDictionary(
                e => e.Key,
                e => e.Value!.Errors.Select(x => x.ErrorMessage).ToArray());

        return new BadRequestObjectResult(new ValidationProblemDetails(errors)
        {
            Title = "Invalid request",
            Status = StatusCodes.Status400BadRequest,
        });
    };
});

builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddOptions<SwaggerOptions>()
    .Bind(builder.Configuration.GetSection(SwaggerOptions.SectionName));

var swaggerOptions = builder.Configuration.GetSection(SwaggerOptions.SectionName).Get<SwaggerOptions>()
                     ?? new SwaggerOptions();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "LeadMine API",
        Version = "v1",
        Description = "Lead generation backend with role- and permission-based access control.",
    });

    // Advertise the public base URL(s). Behind a reverse proxy the request URL
    // the app sees is not the one the browser used, so without this "Try it out"
    // would post to the wrong host.
    foreach (var server in swaggerOptions.Servers.Where(s => !string.IsNullOrWhiteSpace(s.Url)))
    {
        options.AddServer(new OpenApiServer { Url = server.Url, Description = server.Description });
    }

    // Lets Swagger UI send the bearer token, so the whole API is testable there.
    var scheme = new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Description = "Paste the JWT access token from /api/auth/login. The 'Bearer ' prefix is added for you.",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" },
    };

    options.AddSecurityDefinition("Bearer", scheme);
    options.AddSecurityRequirement(new OpenApiSecurityRequirement { [scheme] = Array.Empty<string>() });

    // Surface the /// comments on controllers and DTOs in the document.
    var xmlFiles = new[]
    {
        $"{Assembly.GetExecutingAssembly().GetName().Name}.xml",
        "LeadMine.Application.xml",
        "LeadMine.Domain.xml",
    };

    foreach (var xmlFile in xmlFiles)
    {
        var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
        if (File.Exists(xmlPath)) options.IncludeXmlComments(xmlPath, includeControllerXmlComments: true);
    }
});

const string CorsPolicy = "LeadMineCors";
builder.Services.AddCors(options =>
{
    options.AddPolicy(CorsPolicy, policy => policy
        // Reflects back whatever Origin the caller sent rather than a literal
        // "*", so every origin is allowed without tripping the CORS spec's ban
        // on combining a wildcard origin with credentialed requests (ASP.NET
        // Core throws at runtime if AllowAnyOrigin() is paired with
        // AllowCredentials()). Fine here because the API authenticates with a
        // bearer token, not a cookie the browser attaches automatically — an
        // arbitrary site reflecting this origin still can't forge a caller's
        // token, which is the actual thing credentialed CORS normally guards.
        .SetIsOriginAllowed(_ => true)
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials());
});

var app = builder.Build();

// First in the pipeline so it catches everything downstream.
app.UseMiddleware<ExceptionHandlingMiddleware>();

app.UseSerilogRequestLogging();

// Migrate and seed on startup so a fresh clone is usable with no manual steps.
using (var scope = app.Services.CreateScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    try
    {
        var db = scope.ServiceProvider.GetRequiredService<LeadMineDbContext>();
        await db.Database.MigrateAsync();
        await DbSeeder.SeedAsync(scope.ServiceProvider);
        logger.LogInformation("Database migrated and seeded");
    }
    catch (Exception ex)
    {
        // Starting up with an unmigrated database would fail confusingly on the
        // first request instead of here, where the cause is obvious.
        logger.LogCritical(ex, "Database initialisation failed");
        throw;
    }
}

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

// Swagger runs in every environment — a deployed API is where an explorable
// contract is most useful. Outside Development the middleware below demands
// HTTP Basic credentials first, and fails closed if none are configured.
if (swaggerOptions.Enabled)
{
    var prefix = swaggerOptions.RoutePrefix.Trim('/');

    app.UseMiddleware<SwaggerAuthenticationMiddleware>();

    app.UseSwagger(options => options.RouteTemplate = $"{prefix}/{{documentName}}/swagger.json");

    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint($"/{prefix}/v1/swagger.json", "LeadMine API v1");
        options.RoutePrefix = prefix;
        options.DocumentTitle = "LeadMine API";
        // Collapsed by default: the full expansion is unreadable at this size.
        options.DocExpansion(Swashbuckle.AspNetCore.SwaggerUI.DocExpansion.List);
        options.DefaultModelsExpandDepth(0);
        options.DisplayRequestDuration();
        // Keeps the pasted bearer token across page reloads.
        options.EnablePersistAuthorization();
    });

    app.Logger.LogInformation(
        "Swagger UI at /{Prefix} (protected: {Protected})",
        prefix,
        !swaggerOptions.AllowsAnonymousAccess(app.Environment.IsDevelopment()));
}

app.UseCors(CorsPolicy);

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

await app.RunAsync();

/// <summary>Exposed so integration tests can reference the host.</summary>
public partial class Program;
