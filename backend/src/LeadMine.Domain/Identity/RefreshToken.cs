namespace LeadMine.Domain.Identity;

/// <summary>
/// A refresh token in a rotating family.
/// <para>
/// Each use issues a new token and marks the old one replaced. If a token that
/// was already replaced is presented again, it was stolen — the whole family is
/// revoked rather than just that token. Tokens are stored hashed, so a database
/// leak cannot be replayed.
/// </para>
/// </summary>
public class RefreshToken
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }

    public ApplicationUser User { get; set; } = null!;

    /// <summary>SHA-256 of the token; the raw value is only ever sent to the client.</summary>
    public string TokenHash { get; set; } = string.Empty;

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public string? CreatedByIp { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    public string? RevokedByIp { get; set; }

    public string? RevokedReason { get; set; }

    /// <summary>Hash of the token that superseded this one, for reuse detection.</summary>
    public string? ReplacedByTokenHash { get; set; }

    public bool IsExpired => DateTimeOffset.UtcNow >= ExpiresAt;

    public bool IsRevoked => RevokedAt is not null;

    public bool IsActive => !IsRevoked && !IsExpired;
}
