using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using LeadMine.Application.Interfaces;
using LeadMine.Domain.Identity;
using LeadMine.Infrastructure.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace LeadMine.Infrastructure.Identity;

public sealed class TokenService(IOptions<JwtOptions> options) : ITokenService
{
    private readonly JwtOptions _options = options.Value;

    public string CreateAccessToken(
        ApplicationUser user,
        IEnumerable<string> roles,
        IEnumerable<string> permissions)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),
            new(ClaimTypes.Name, user.UserName ?? user.Email ?? string.Empty),
            new("firstName", user.FirstName),
            new("lastName", user.LastName),
            new(LeadMineClaims.SecurityVersion, user.SecurityVersion.ToString()),
            // Unique per token so a specific token can be identified in logs.
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };

        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));

        // Embedding rights means authorization needs no database round-trip.
        claims.AddRange(permissions.Distinct().Select(p => new Claim(LeadMineClaims.Permission, p)));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Key));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: GetAccessTokenExpiry().UtcDateTime,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public (string Token, string Hash) CreateRefreshToken()
    {
        // 256 bits from a CSPRNG — not Guid, which is not guaranteed unpredictable.
        var bytes = RandomNumberGenerator.GetBytes(32);
        var token = Convert.ToBase64String(bytes);
        return (token, HashToken(token));
    }

    /// <summary>
    /// SHA-256, unsalted by design: the token is already high-entropy random, so
    /// there is nothing to brute-force, and a deterministic hash is what makes
    /// the O(1) indexed lookup on refresh possible.
    /// </summary>
    public string HashToken(string token)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(hash);
    }

    public DateTimeOffset GetAccessTokenExpiry() =>
        DateTimeOffset.UtcNow.AddMinutes(_options.AccessTokenMinutes);

    public DateTimeOffset GetRefreshTokenExpiry() =>
        DateTimeOffset.UtcNow.AddDays(_options.RefreshTokenDays);

    public ClaimsPrincipal? GetPrincipalFromExpiredToken(string token)
    {
        var parameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = _options.Issuer,
            ValidateAudience = true,
            ValidAudience = _options.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Key)),
            // The whole point is to read a token that has already expired.
            ValidateLifetime = false,
            ClockSkew = TimeSpan.Zero,
        };

        try
        {
            var principal = new JwtSecurityTokenHandler()
                .ValidateToken(token, parameters, out var validated);

            // Reject a token signed with anything other than the expected algorithm.
            if (validated is not JwtSecurityToken jwt ||
                !jwt.Header.Alg.Equals(SecurityAlgorithms.HmacSha256, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return principal;
        }
        catch
        {
            return null;
        }
    }
}
