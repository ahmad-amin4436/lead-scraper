namespace LeadMine.Domain.Entities;

/// <summary>
/// One app user's own LinkedIn session — cookies and local storage from a
/// real, manual login (<c>backend/tools/LinkedInLogin</c>), never a password.
/// <para>
/// Each user brings their own LinkedIn account for LinkedIn-backed features,
/// rather than every user in the app sharing one dedicated automation
/// account. That spreads traffic across real, individually-owned accounts
/// instead of concentrating all of it on one thin account with no network of
/// its own — and it means each user accepts LinkedIn's terms-of-service risk
/// for their own account, not the operator's, on their own behalf.
/// </para>
/// <para>
/// One row per user: uploading a new session replaces the previous one for
/// that user rather than accumulating history, matching how the old
/// single-file session was simply overwritten on re-login. Deliberately not
/// an <see cref="Common.AuditableEntity"/> — this is live, replaceable
/// session material, not something that benefits from a soft-delete audit
/// trail, and soft-delete would fight the unique index on
/// <see cref="UserId"/> the moment a removed session's row stuck around.
/// Removing a session is a real delete.
/// </para>
/// </summary>
public class LinkedInAccountSession
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The app user this session belongs to and runs LinkedIn work as.</summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Raw Playwright storage state (cookies + local storage) as produced by
    /// <c>backend/tools/LinkedInLogin</c>. Handed to Playwright's
    /// <c>BrowserNewContextOptions.StorageState</c> directly — never written
    /// to a shared file, and never a credential of any kind.
    /// </summary>
    public string StorageStateJson { get; set; } = string.Empty;

    public DateTimeOffset UploadedAt { get; set; }

    // --- per-account throttle / circuit-breaker state -----------------------
    // Formerly one shared, file-based counter for the whole app; now one row
    // per user, since each user's own LinkedIn account needs its own daily
    // budget and its own restriction cooldown — one user's account being
    // flagged has nothing to do with another user's.

    public DateOnly BudgetDate { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow);

    public int SearchesToday { get; set; }

    /// <summary>Set the moment a restriction warning is seen for this account. Null when never flagged (or cleared by a fresh upload — see <c>LinkedInSessionManager</c>).</summary>
    public DateTimeOffset? RestrictedAt { get; set; }

    public string? RestrictedReason { get; set; }
}
