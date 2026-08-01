using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace LeadMine.Api.Middleware;

/// <summary>
/// Converts unhandled exceptions into RFC 7807 problem responses.
/// <para>
/// Detail is only included outside production: a stack trace or raw SQL error
/// reaching a client is an information-disclosure bug, so production callers get
/// a generic message while the full exception goes to the log.
/// </para>
/// </summary>
public sealed class ExceptionHandlingMiddleware(
    RequestDelegate next,
    ILogger<ExceptionHandlingMiddleware> logger,
    IHostEnvironment environment)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception on {Method} {Path}", context.Request.Method, context.Request.Path);

            if (context.Response.HasStarted)
            {
                // Too late to replace the response; let it fail as-is.
                throw;
            }

            var (status, title) = ex switch
            {
                UnauthorizedAccessException => (HttpStatusCode.Forbidden, "Forbidden"),
                KeyNotFoundException => (HttpStatusCode.NotFound, "Not found"),
                ArgumentException => (HttpStatusCode.BadRequest, "Invalid request"),
                _ => (HttpStatusCode.InternalServerError, "An unexpected error occurred"),
            };

            var problem = new ProblemDetails
            {
                Status = (int)status,
                Title = title,
                Instance = context.Request.Path,
                Detail = environment.IsDevelopment() ? ex.ToString() : null,
            };

            context.Response.Clear();
            context.Response.StatusCode = problem.Status.Value;
            context.Response.ContentType = "application/problem+json";

            await context.Response.WriteAsync(JsonSerializer.Serialize(problem, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            }));
        }
    }
}
