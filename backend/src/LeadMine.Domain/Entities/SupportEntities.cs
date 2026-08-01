using LeadMine.Domain.Common;
using LeadMine.Domain.Enums;

namespace LeadMine.Domain.Entities;

/// <summary>A generated export file.</summary>
public class ExportRecord : AuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string FileName { get; set; } = string.Empty;

    /// <summary>"xlsx" or "csv".</summary>
    public string Format { get; set; } = "xlsx";

    /// <summary>Column preset: leadmine, hubspot, salesforce.</summary>
    public string Template { get; set; } = "leadmine";

    public int RowCount { get; set; }

    public long ByteSize { get; set; }

    /// <summary>Filters applied, as JSON, so an export is reproducible.</summary>
    public string FiltersJson { get; set; } = "{}";

    /// <summary>Where the bytes live (path or object-storage key).</summary>
    public string? StorageKey { get; set; }

    public Guid? RequestedByUserId { get; set; }
}

/// <summary>Structured activity log, queryable from the admin UI.</summary>
public class ActivityLog
{
    public long Id { get; set; }

    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

    public ActivityLogLevel Level { get; set; } = ActivityLogLevel.Information;

    /// <summary>Dotted event name, e.g. <c>search.completed</c>.</summary>
    public string Event { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public Guid? SearchJobId { get; set; }

    public Guid? UserId { get; set; }

    public long? ElapsedMs { get; set; }

    /// <summary>Arbitrary structured context, as JSON.</summary>
    public string ContextJson { get; set; } = "{}";
}

/// <summary>
/// Key/value application settings.
/// <para>
/// <see cref="IsSecret"/> values (API keys) are never returned to clients in
/// full — the API masks them, so a compromised browser session cannot exfiltrate
/// a provider key.
/// </para>
/// </summary>
public class AppSetting : AuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Key { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public bool IsSecret { get; set; }
}

/// <summary>
/// Security-relevant audit trail: logins, permission changes, deletions.
/// Append-only — nothing in the API updates or deletes these rows.
/// </summary>
public class AuditEntry
{
    public long Id { get; set; }

    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

    public Guid? UserId { get; set; }

    public string UserName { get; set; } = string.Empty;

    /// <summary>e.g. <c>User.RoleAssigned</c>, <c>Auth.LoginFailed</c>.</summary>
    public string Action { get; set; } = string.Empty;

    public string EntityType { get; set; } = string.Empty;

    public string? EntityId { get; set; }

    public string? IpAddress { get; set; }

    public string? UserAgent { get; set; }

    public bool Succeeded { get; set; } = true;

    public string DetailsJson { get; set; } = "{}";
}
