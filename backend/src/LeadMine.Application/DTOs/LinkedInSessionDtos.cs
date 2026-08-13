using System.ComponentModel.DataAnnotations;

namespace LeadMine.Application.DTOs;

/// <summary>
/// The caller's own LinkedIn credentials, used once to drive a server-side
/// Playwright login and never persisted — see
/// <c>LinkedInSessionManager.BeginCredentialLoginAsync</c>.
/// </summary>
public sealed class LinkedInLoginRequest
{
    [Required]
    public string LinkedInEmail { get; set; } = string.Empty;

    [Required]
    public string LinkedInPassword { get; set; } = string.Empty;
}

/// <summary>A verification code for a LinkedIn checkpoint raised mid-login.</summary>
public sealed class LinkedInLoginVerifyRequest
{
    [Required]
    public string Code { get; set; } = string.Empty;
}

/// <summary>Outcome of a credential login attempt or verification-code submission.</summary>
public enum LinkedInLoginStatus
{
    /// <summary>Session captured and saved — the caller's account is connected.</summary>
    Success,

    /// <summary>LinkedIn raised a checkpoint with a code field; call the verify endpoint next.</summary>
    VerificationRequired,

    /// <summary>LinkedIn rejected the email/password.</summary>
    InvalidCredentials,

    /// <summary>LinkedIn raised a challenge that can't be completed headlessly (e.g. a puzzle/CAPTCHA).</summary>
    ChallengeUnsupported,

    /// <summary>LinkedIn showed a restriction/unusual-activity warning.</summary>
    Restricted,

    /// <summary>Anything else that stopped the login from completing.</summary>
    Failed,
}

/// <summary>Response for the login / verify / cancel endpoints.</summary>
public sealed class LinkedInLoginResult
{
    public LinkedInLoginStatus Status { get; set; }

    public string Message { get; set; } = string.Empty;
}
