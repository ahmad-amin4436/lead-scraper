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

    /// <summary>
    /// What this row runs — a category × city sweep or a LinkedIn enrichment
    /// batch — so one queue/lease/checkpoint mechanism serves both instead of
    /// duplicating it. Defaults to <see cref="JobKind.Search"/> so every row
    /// created before this field existed is read the same way it always ran.
    /// </summary>
    public JobKind Kind { get; set; } = JobKind.Search;

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

    // --- lease / durability -------------------------------------------------

    /// <summary>
    /// Identifier of the worker currently holding this job.
    /// <para>
    /// A job is claimed by exactly one worker at a time. Combined with
    /// <see cref="LeaseExpiresAt"/> this is what lets several workers run
    /// concurrently without two of them scraping the same run.
    /// </para>
    /// </summary>
    public string? LeaseOwner { get; set; }

    /// <summary>
    /// When the current claim lapses. A worker extends it on every heartbeat; if
    /// the process dies the lease simply expires and the job becomes claimable
    /// again, which is what makes a crash recoverable without human action.
    /// </summary>
    public DateTimeOffset? LeaseExpiresAt { get; set; }

    /// <summary>
    /// How many times this job has been claimed. A run that keeps dying is
    /// eventually failed rather than retried forever — a poison job must not
    /// occupy the queue indefinitely.
    /// </summary>
    public int Attempts { get; set; }

    /// <summary>
    /// JSON array of the `city|category` task keys already finished.
    /// <para>
    /// Checkpointing at task granularity is what makes a resumed job continue
    /// rather than restart: the worker skips these, so an interrupted 200-task
    /// sweep does not redo the 180 it had already completed.
    /// </para>
    /// </summary>
    public string CompletedTaskKeysJson { get; set; } = "[]";

    /// <summary>Live results for the UI, capped. JSON, newest first.</summary>
    public string RecentResultsJson { get; set; } = "[]";

    /// <summary>True when no worker holds a live lease on this job.</summary>
    public bool IsLeaseExpired(DateTimeOffset now) =>
        LeaseExpiresAt is null || LeaseExpiresAt <= now;
}
