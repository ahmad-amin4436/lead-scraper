using LeadMine.Application.DTOs;
using LeadMine.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LeadMine.Api.Controllers;

[AllowAnonymous]
public sealed class AuthController(IAuthService auth) : ApiControllerBase
{
    /// <summary>Creates an account and returns a token pair.</summary>
    [HttpPost("register")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AuthResponse>> Register(RegisterRequest request, CancellationToken ct)
        => FromResult(await auth.RegisterAsync(request, ct));

    /// <summary>Exchanges credentials for a token pair.</summary>
    [HttpPost("login")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request, CancellationToken ct)
        => FromResult(await auth.LoginAsync(request, ct));

    /// <summary>
    /// Rotates a refresh token for a new pair. Presenting an already-used token
    /// revokes the whole session family, as that indicates theft.
    /// </summary>
    [HttpPost("refresh")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthResponse>> Refresh(RefreshTokenRequest request, CancellationToken ct)
        => FromResult(await auth.RefreshAsync(request.RefreshToken, ct));

    /// <summary>Revokes a refresh token (sign out).</summary>
    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout(RefreshTokenRequest request, CancellationToken ct)
    {
        var result = await auth.RevokeAsync(request.RefreshToken, ct);
        return result.Succeeded ? NoContent() : Problem(result);
    }

    /// <summary>The signed-in user, including effective roles and permissions.</summary>
    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<UserDto>> Me(CancellationToken ct)
    {
        if (CurrentUserId is not { } userId) return Unauthorized();
        return FromResult(await auth.GetCurrentUserAsync(userId, ct));
    }

    /// <summary>Changes the caller's own password and ends all other sessions.</summary>
    [HttpPost("change-password")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken ct)
    {
        if (CurrentUserId is not { } userId) return Unauthorized();

        var result = await auth.ChangePasswordAsync(userId, request, ct);
        return result.Succeeded ? NoContent() : Problem(result);
    }
}
