using System.Text.Json.Serialization;
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

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "LeadMine API",
        Version = "v1",
        Description = "Lead generation backend with role- and permission-based access control.",
    });

    // Lets Swagger UI send the bearer token, so the whole API is testable there.
    var scheme = new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Description = "Paste the JWT access token. The 'Bearer ' prefix is added for you.",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" },
    };

    options.AddSecurityDefinition("Bearer", scheme);
    options.AddSecurityRequirement(new OpenApiSecurityRequirement { [scheme] = Array.Empty<string>() });
});

const string CorsPolicy = "LeadMineCors";
builder.Services.AddCors(options =>
{
    var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
                  ?? ["http://localhost:3000"];

    options.AddPolicy(CorsPolicy, policy => policy
        .WithOrigins(origins)
        .AllowAnyHeader()
        .AllowAnyMethod()
        // Required for credentialed requests from the Next.js front end.
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

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "LeadMine API v1");
        options.DocumentTitle = "LeadMine API";
    });
}
else
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseCors(CorsPolicy);

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

await app.RunAsync();

/// <summary>Exposed so integration tests can reference the host.</summary>
public partial class Program;
