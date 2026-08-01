using LeadMine.Domain.Common;
using LeadMine.Domain.Enums;

namespace LeadMine.Domain.Entities;

/// <summary>
/// A search run and its live progress.
/// <para>
/// State lives in the database rather than process memory so any instance can
/// report progress, and <see cref="HeartbeatAt"/> lets a supervisor tell a slow
/// run from a dead one — a worker that crashes would otherwise leave the job
/// non-terminal forever and block every subsequent search.
/// </para>
/// </summary>
public class SearchJob : AuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public SearchJobStatus Status { get; set; } = SearchJobStatus.Queued;

    /// <summary>The original request, stored as JSON for replay/rerun.</summary>
    public string RequestJson { get; set; } = "{}";

    public int TotalTasks { get; set; }

    public int CompletedTasks { get; set; }

    public int Found { get; set; }

    public int Saved { get; set; }

    public int Duplicates { get; set; }

    public int Enriched { get; set; }

    public int EnrichmentFailed { get; set; }

    public int EmailsVerified { get; set; }

    public int WhatsAppReachable { get; set; }

    public int Skipped { get; set; }

    public int Failed { get; set; }

    public string CurrentTask { get; set; } = "Preparing";

    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? FinishedAt { get; set; }

    /// <summary>Last write by the worker; stale ⇒ the worker died.</summary>
    public DateTimeOffset? HeartbeatAt { get; set; }

    /// <summary>Cooperative stop flag, observed by the worker at checkpoints.</summary>
    public bool StopRequested { get; set; }

    public string? Error { get; set; }

    public Guid? RequestedByUserId { get; set; }

    public ICollection<Business> Businesses { get; set; } = new List<Business>();
}
