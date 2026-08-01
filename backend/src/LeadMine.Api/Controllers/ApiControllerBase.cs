using System.Security.Claims;
using LeadMine.Application.Common;
using Microsoft.AspNetCore.Mvc;

namespace LeadMine.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public abstract class ApiControllerBase : ControllerBase
{
    /// <summary>The authenticated caller's id, or null for anonymous requests.</summary>
    protected Guid? CurrentUserId
    {
        get
        {
            var value = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
            return Guid.TryParse(value, out var id) ? id : null;
        }
    }

    /// <summary>
    /// Maps a <see cref="Result"/> failure onto the matching status code, so
    /// every controller reports the same shape for the same class of error.
    /// </summary>
    protected ActionResult Problem(Result result)
    {
        var detail = result.Errors.Count > 1
            ? string.Join(" ", result.Errors)
            : result.Error ?? "Request failed.";

        return result.ErrorType switch
        {
            ResultErrorType.NotFound => NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound, Title = "Not found", Detail = detail,
            }),
            ResultErrorType.Conflict => Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict, Title = "Conflict", Detail = detail,
            }),
            ResultErrorType.Forbidden => StatusCode(StatusCodes.Status403Forbidden, new ProblemDetails
            {
                Status = StatusCodes.Status403Forbidden, Title = "Forbidden", Detail = detail,
            }),
            ResultErrorType.Unauthorized => Unauthorized(new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized, Title = "Unauthorized", Detail = detail,
            }),
            _ => BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest, Title = "Invalid request", Detail = detail,
            }),
        };
    }

    /// <summary>Returns the value on success, or the mapped failure.</summary>
    protected ActionResult<T> FromResult<T>(Result<T> result) =>
        result.Succeeded ? Ok(result.Value) : Problem(result);
}
