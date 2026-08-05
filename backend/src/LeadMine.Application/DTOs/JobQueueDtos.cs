using System.ComponentModel.DataAnnotations;
using LeadMine.Domain.Enums;

namespace LeadMine.Application.DTOs;

/// <summary>A search run as the UI sees it.</summary>
public sealed class SearchJobDto
{
    public Guid Id { get; set; }
    public JobKind Kind { get; set; }
    public SearchJobStatus Status { get; set; }
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
    public string CurrentTask { get; set; } = string.Empty;
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public DateTimeOffset? HeartbeatAt { get; set; }
    public bool StopRequested { get; set; }
    public string? Error { get; set; }
    public Guid? RequestedByUserId { get; set; }
    public int Attempts { get; set; }

    /// <summary>Live results for the progress panel, newest first.</summary>
    public string RecentResultsJson { get; set; } = "[]";

    /// <summary>True while a worker holds a live lease.</summary>
    public bool IsLeased { get; set; }

    public double PercentComplete =>
        TotalTasks <= 0 ? 0 : Math.Min(100, Math.Round(CompletedTasks / (double)TotalTasks * 100, 1));
}

public sealed class CreateSearchJobRequest
{
    /// <summary>Defaults to a search sweep — the only kind that existed before <see cref="JobKind.LinkedInEnrichment"/>.</summary>
    public JobKind Kind { get; set; } = JobKind.Search;

    /// <summary>The full request, stored verbatim for the worker (and rerun, for a search).</summary>
    [Required]
    public string RequestJson { get; set; } = "{}";

    /// <summary>
    /// Total units of work — (city × category) pairs for a search, lead count
    /// for a LinkedIn enrichment batch — so progress means something immediately.
    /// </summary>
    [Range(0, 100000)]
    public int TotalTasks { get; set; }
}

// --- worker protocol --------------------------------------------------------

public sealed class ClaimJobRequest
{
    /// <summary>Stable identity of the worker process, for lease ownership.</summary>
    [Required, MaxLength(128)]
    public string WorkerId { get; set; } = string.Empty;

    /// <summary>How long the claim should hold without a heartbeat.</summary>
    [Range(30, 3600)]
    public int LeaseSeconds { get; set; } = 120;
}

/// <summary>A claimed job plus everything needed to resume it.</summary>
public sealed class ClaimedJobDto
{
    public Guid Id { get; set; }
    public JobKind Kind { get; set; }
    public string RequestJson { get; set; } = "{}";
    public Guid? OwnerUserId { get; set; }
    public int Attempts { get; set; }
    public bool StopRequested { get; set; }

    /// <summary>Task keys already done, so the worker skips them.</summary>
    public IReadOnlyList<string> CompletedTaskKeys { get; set; } = Array.Empty<string>();

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
}

/// <summary>
/// Progress written by the worker. Doubles as the lease extension, so a job that
/// is making progress can never be reaped out from under itself.
/// </summary>
public sealed class JobHeartbeatRequest
{
    [Required, MaxLength(128)]
    public string WorkerId { get; set; } = string.Empty;

    [Range(30, 3600)]
    public int LeaseSeconds { get; set; } = 120;

    [MaxLength(256)]
    public string? CurrentTask { get; set; }

    /// <summary>Task key just finished, appended to the resume checkpoint.</summary>
    [MaxLength(256)]
    public string? CompletedTaskKey { get; set; }

    public int? TotalTasks { get; set; }
    public int? Found { get; set; }
    public int? Saved { get; set; }
    public int? Duplicates { get; set; }
    public int? Enriched { get; set; }
    public int? EnrichmentFailed { get; set; }
    public int? EmailsVerified { get; set; }
    public int? WhatsAppReachable { get; set; }
    public int? Skipped { get; set; }
    public int? Failed { get; set; }

    /// <summary>Live results for the UI, newest first. Replaces the stored list.</summary>
    public string? RecentResultsJson { get; set; }
}

public sealed class JobHeartbeatResponse
{
    /// <summary>The worker aborts when this flips, without polling separately.</summary>
    public bool StopRequested { get; set; }

    /// <summary>False when the lease was lost; the worker must stop immediately.</summary>
    public bool LeaseValid { get; set; } = true;
}

public sealed class CompleteJobRequest
{
    [Required, MaxLength(128)]
    public string WorkerId { get; set; } = string.Empty;

    [Required]
    public SearchJobStatus Status { get; set; }

    [MaxLength(2000)]
    public string? Error { get; set; }
}
