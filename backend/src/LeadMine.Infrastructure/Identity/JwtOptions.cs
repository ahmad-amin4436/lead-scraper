using System.ComponentModel.DataAnnotations;

namespace LeadMine.Infrastructure.Identity;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    [Required, MinLength(32, ErrorMessage = "Jwt:Key must be at least 32 characters (256 bits) for HS256.")]
    public string Key { get; set; } = string.Empty;

    [Required]
    public string Issuer { get; set; } = "LeadMine.Api";

    [Required]
    public string Audience { get; set; } = "LeadMine.Client";

    /// <summary>
    /// Deliberately short. A leaked access token cannot be revoked before it
    /// expires, so the window is kept small and clients use the refresh token.
    /// </summary>
    [Range(1, 1440)]
    public int AccessTokenMinutes { get; set; } = 15;

    [Range(1, 365)]
    public int RefreshTokenDays { get; set; } = 7;
}
