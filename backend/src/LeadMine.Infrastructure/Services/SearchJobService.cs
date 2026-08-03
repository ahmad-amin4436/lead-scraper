using System.Text.Json;
using LeadMine.Application.Authorization;
using LeadMine.Application.Common;
using LeadMine.Application.DTOs;
using LeadMine.Application.Interfaces;
using LeadMine.Domain.Entities;
using LeadMine.Domain.Enums;
using LeadMine.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LeadMine.Infrastructure.Services;

public interface ISearchJobService
{
    Task<Result<SearchJobDto>> CreateAsync(CreateSearchJobRequest request, CancellationToken ct = default);

    Task<IReadOnlyList<SearchJobDto>> GetActiveAsync(CancellationToken ct = default);

    Task<PagedResult<SearchJobDto>> GetHistoryAsync(int page, int pageSize, CancellationToken ct = default);

    Task<Result<SearchJobDto>> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<Result<SearchJobDto>> RequestStopAsync(Guid id, CancellationToken ct = default);

    /// <summary>Removes one finished run from the caller's history.</summary>
    Task<Result> DeleteAsync(Guid id, CancellationToken ct = default);

    /// <summary>Clears the caller's finished runs. Returns how many were removed.</summary>
    Task<Result<int>> ClearHistoryAsync(CancellationToken ct = default);

    // --- worker protocol ---

    Task<ClaimedJobDto?> ClaimNextAsync(ClaimJobRequest request, CancellationToken ct = default);

    Task<JobHeartbeatResponse> HeartbeatAsync(Guid id, JobHeartbeatRequest request, CancellationToken ct = default);

    Task<Result> CompleteAsync(Guid id, CompleteJobRequest request, CancellationToken ct = default);

    /// <summary>Releases jobs whose worker died. Returns how many were recovered.</summary>
    Task<int> ReleaseExpiredLeasesAsync(CancellationToken ct = default);
}

public sealed class SearchJobService(
    LeadMineDbContext db,
    ICurrentUser currentUser,
    ILogger<SearchJobService> logger) : ISearchJobService
{
    /// <summary>Live results kept per job, to bound the row size.</summary>
    private const int MaxRecentResults = 200;

    /// <summary>
    /// A job reclaimed this many times is treated as poison and failed. Without
    /// a cap, a run that reliably crashes would occupy a worker forever.
    /// </summary>
    private const int MaxAttempts = 5;

    public async Task<Result<SearchJobDto>> CreateAsync(
        CreateSearchJobRequest request,
        CancellationToken ct = default)
    {
        if (currentUser.UserId is not { } userId) return Result<SearchJobDto>.Unauthorized();

        // One active run per user: concurrent sweeps by the same person would
        // multiply provider rate-limit pressure for no benefit.
        var existing = await db.SearchJobs
            .Where(j => j.RequestedByUserId == userId)
            .Where(j => j.Status == SearchJobStatus.Queued ||
                        j.Status == SearchJobStatus.Running ||
                        j.Status == SearchJobStatus.Stopping)
            .FirstOrDefaultAsync(ct);

        if (existing is not null)
        {
            return Result<SearchJobDto>.Conflict(
                "You already have a search running. Stop it before starting another.");
        }

        var job = new SearchJob
        {
            Status = SearchJobStatus.Queued,
            RequestJson = request.RequestJson,
            TotalTasks = request.TotalTasks,
            RequestedByUserId = userId,
            StartedAt = DateTimeOffset.UtcNow,
            CurrentTask = "Queued",
        };

        db.SearchJobs.Add(job);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Queued search job {JobId} for {UserId}", job.Id, userId);
        return Result<SearchJobDto>.Success(Map(job));
    }

    public async Task<IReadOnlyList<SearchJobDto>> GetActiveAsync(CancellationToken ct = default)
    {
        var query = ScopeToCaller(db.SearchJobs.AsNoTracking())
            .Where(j => j.Status == SearchJobStatus.Queued ||
                        j.Status == SearchJobStatus.Running ||
                        j.Status == SearchJobStatus.Stopping);

        var jobs = await query.OrderByDescending(j => j.StartedAt).ToListAsync(ct);
        return jobs.Select(Map).ToList();
    }

    public async Task<PagedResult<SearchJobDto>> GetHistoryAsync(
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        var query = ScopeToCaller(db.SearchJobs.AsNoTracking());

        var total = await query.CountAsync(ct);

        var jobs = await query
            .OrderByDescending(j => j.StartedAt)
            .Skip((Math.Max(1, page) - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return PagedResult<SearchJobDto>.Create(jobs.Select(Map).ToList(), total, page, pageSize);
    }

    public async Task<Result<SearchJobDto>> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var job = await ScopeToCaller(db.SearchJobs.AsNoTracking())
            .FirstOrDefaultAsync(j => j.Id == id, ct);

        return job is null
            ? Result<SearchJobDto>.NotFound("Search run not found.")
            : Result<SearchJobDto>.Success(Map(job));
    }

    public async Task<Result<SearchJobDto>> RequestStopAsync(Guid id, CancellationToken ct = default)
    {
        var job = await ScopeToCaller(db.SearchJobs).FirstOrDefaultAsync(j => j.Id == id, ct);
        if (job is null) return Result<SearchJobDto>.NotFound("Search run not found.");

        if (IsTerminal(job.Status))
        {
            return Result<SearchJobDto>.Conflict($"That run has already {job.Status.ToString().ToLower()}.");
        }

        job.StopRequested = true;

        var now = DateTimeOffset.UtcNow;

        // With no live worker there is nobody to observe the flag, so close it
        // out here instead of leaving the user staring at "Stopping" forever.
        if (job.IsLeaseExpired(now))
        {
            job.Status = SearchJobStatus.Stopped;
            job.FinishedAt = now;
            job.CurrentTask = "Stopped";
            job.LeaseOwner = null;
            job.LeaseExpiresAt = null;
        }
        else
        {
            job.Status = SearchJobStatus.Stopping;
        }

        await db.SaveChangesAsync(ct);
        return Result<SearchJobDto>.Success(Map(job));
    }

    /// <summary>
    /// Removes a finished run from history.
    /// <para>
    /// Only finished runs: deleting a live one would leave a worker heartbeating
    /// against a row that has vanished from every query, and its leads would
    /// still be arriving. Stop it first.
    /// </para>
    /// </summary>
    public async Task<Result> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var job = await ScopeToCaller(db.SearchJobs).FirstOrDefaultAsync(j => j.Id == id, ct);
        if (job is null) return Result.NotFound("Search run not found.");

        if (!IsTerminal(job.Status))
        {
            return Result.Conflict("That run is still active. Stop it before deleting it.");
        }

        // Soft delete: SaveChanges turns Remove into IsDeleted = true, so the
        // leads that reference this run keep a valid foreign key.
        db.SearchJobs.Remove(job);
        await db.SaveChangesAsync(ct);

        return Result.Success();
    }

    public async Task<Result<int>> ClearHistoryAsync(CancellationToken ct = default)
    {
        var finished = await ScopeToCaller(db.SearchJobs)
            .Where(j => j.Status == SearchJobStatus.Completed ||
                        j.Status == SearchJobStatus.Failed ||
                        j.Status == SearchJobStatus.Stopped)
            .ToListAsync(ct);

        if (finished.Count == 0) return Result<int>.Success(0);

        db.SearchJobs.RemoveRange(finished);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Cleared {Count} finished run(s) from history", finished.Count);
        return Result<int>.Success(finished.Count);
    }

    /// <summary>
    /// Atomically claims the oldest job that is queued, or whose lease expired.
    /// <para>
    /// The update is guarded by the row's original lease values, so if two
    /// workers race, exactly one write succeeds and the loser simply tries the
    /// next row. That guard is what makes running several workers safe.
    /// </para>
    /// </summary>
    public async Task<ClaimedJobDto?> ClaimNextAsync(
        ClaimJobRequest request,
        CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var leaseUntil = now.AddSeconds(request.LeaseSeconds);

        // A few attempts, because a lost race means someone else took that row.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var candidate = await db.SearchJobs
                .Where(j => j.Status == SearchJobStatus.Queued ||
                            j.Status == SearchJobStatus.Running ||
                            j.Status == SearchJobStatus.Stopping)
                .Where(j => j.LeaseExpiresAt == null || j.LeaseExpiresAt <= now)
                .OrderBy(j => j.StartedAt)
                .FirstOrDefaultAsync(ct);

            if (candidate is null) return null;

            // Give up on a job that keeps dying rather than looping on it.
            if (candidate.Attempts >= MaxAttempts)
            {
                candidate.Status = SearchJobStatus.Failed;
                candidate.FinishedAt = now;
                candidate.Error =
                    $"Abandoned after {candidate.Attempts} attempts — the run kept failing before it could finish.";
                candidate.LeaseOwner = null;
                candidate.LeaseExpiresAt = null;

                await db.SaveChangesAsync(ct);
                logger.LogError("Search job {JobId} abandoned after {Attempts} attempts", candidate.Id, candidate.Attempts);
                continue;
            }

            candidate.LeaseOwner = request.WorkerId;
            candidate.LeaseExpiresAt = leaseUntil;
            candidate.Attempts += 1;
            candidate.HeartbeatAt = now;
            if (candidate.Status == SearchJobStatus.Queued) candidate.Status = SearchJobStatus.Running;

            try
            {
                // Guard on the lease still being free. Deliberately expressed as
                // "unleased or expired" rather than "unchanged since I read it":
                // comparing to a null parameter emits `= @p` in SQL, which is
                // never true for NULL, so a released job could never be
                // re-claimed. This form is equally atomic — the winner's update
                // pushes LeaseExpiresAt into the future, so the loser matches
                // nothing.
                var affected = await db.SearchJobs
                    .Where(j => j.Id == candidate.Id)
                    .Where(j => j.LeaseExpiresAt == null || j.LeaseExpiresAt <= now)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(j => j.LeaseOwner, request.WorkerId)
                        .SetProperty(j => j.LeaseExpiresAt, leaseUntil)
                        .SetProperty(j => j.Attempts, candidate.Attempts)
                        .SetProperty(j => j.HeartbeatAt, now)
                        .SetProperty(j => j.Status, candidate.Status), ct);

                if (affected == 0)
                {
                    // Another worker won; drop the local edits and look again.
                    db.Entry(candidate).State = EntityState.Detached;
                    continue;
                }
            }
            catch (DbUpdateException ex)
            {
                logger.LogWarning(ex, "Lost the race to claim job {JobId}", candidate.Id);
                db.Entry(candidate).State = EntityState.Detached;
                continue;
            }

            logger.LogInformation(
                "Worker {WorkerId} claimed job {JobId} (attempt {Attempt})",
                request.WorkerId, candidate.Id, candidate.Attempts);

            return new ClaimedJobDto
            {
                Id = candidate.Id,
                RequestJson = candidate.RequestJson,
                OwnerUserId = candidate.RequestedByUserId,
                Attempts = candidate.Attempts,
                StopRequested = candidate.StopRequested,
                CompletedTaskKeys = DeserializeKeys(candidate.CompletedTaskKeysJson),
                CompletedTasks = candidate.CompletedTasks,
                Found = candidate.Found,
                Saved = candidate.Saved,
                Duplicates = candidate.Duplicates,
                Enriched = candidate.Enriched,
                EnrichmentFailed = candidate.EnrichmentFailed,
                EmailsVerified = candidate.EmailsVerified,
                WhatsAppReachable = candidate.WhatsAppReachable,
                Skipped = candidate.Skipped,
                Failed = candidate.Failed,
            };
        }

        return null;
    }

    public async Task<JobHeartbeatResponse> HeartbeatAsync(
        Guid id,
        JobHeartbeatRequest request,
        CancellationToken ct = default)
    {
        var job = await db.SearchJobs.FirstOrDefaultAsync(j => j.Id == id, ct);

        if (job is null) return new JobHeartbeatResponse { LeaseValid = false };

        // Someone else owns it now — the worker must stop, or two processes
        // would write progress for the same run.
        if (job.LeaseOwner != request.WorkerId)
        {
            logger.LogWarning(
                "Worker {WorkerId} heartbeat rejected for job {JobId}; lease held by {Owner}",
                request.WorkerId, id, job.LeaseOwner ?? "nobody");

            return new JobHeartbeatResponse { LeaseValid = false, StopRequested = true };
        }

        var now = DateTimeOffset.UtcNow;

        job.HeartbeatAt = now;
        job.LeaseExpiresAt = now.AddSeconds(request.LeaseSeconds);

        if (request.CurrentTask is not null) job.CurrentTask = request.CurrentTask;
        if (request.TotalTasks.HasValue) job.TotalTasks = request.TotalTasks.Value;
        if (request.Found.HasValue) job.Found = request.Found.Value;
        if (request.Saved.HasValue) job.Saved = request.Saved.Value;
        if (request.Duplicates.HasValue) job.Duplicates = request.Duplicates.Value;
        if (request.Enriched.HasValue) job.Enriched = request.Enriched.Value;
        if (request.EnrichmentFailed.HasValue) job.EnrichmentFailed = request.EnrichmentFailed.Value;
        if (request.EmailsVerified.HasValue) job.EmailsVerified = request.EmailsVerified.Value;
        if (request.WhatsAppReachable.HasValue) job.WhatsAppReachable = request.WhatsAppReachable.Value;
        if (request.Skipped.HasValue) job.Skipped = request.Skipped.Value;
        if (request.Failed.HasValue) job.Failed = request.Failed.Value;
        if (request.RecentResultsJson is not null) job.RecentResultsJson = Cap(request.RecentResultsJson);

        // The checkpoint that makes a resume continue instead of restart.
        if (!string.IsNullOrWhiteSpace(request.CompletedTaskKey))
        {
            var keys = DeserializeKeys(job.CompletedTaskKeysJson).ToList();

            if (!keys.Contains(request.CompletedTaskKey))
            {
                keys.Add(request.CompletedTaskKey);
                job.CompletedTaskKeysJson = JsonSerializer.Serialize(keys);
                job.CompletedTasks = keys.Count;
            }
        }

        await db.SaveChangesAsync(ct);

        return new JobHeartbeatResponse
        {
            LeaseValid = true,
            StopRequested = job.StopRequested,
        };
    }

    public async Task<Result> CompleteAsync(
        Guid id,
        CompleteJobRequest request,
        CancellationToken ct = default)
    {
        var job = await db.SearchJobs.FirstOrDefaultAsync(j => j.Id == id, ct);
        if (job is null) return Result.NotFound("Search run not found.");

        // Only the current lease holder may finish a run.
        //
        // A null owner is rejected too: it means the lease was released because
        // this worker stopped responding, and the job has been handed back to
        // the queue. A straggler that wakes up afterwards must not mark it
        // finished — that was silently truncating runs that were about to be
        // resumed.
        if (job.LeaseOwner != request.WorkerId)
        {
            logger.LogWarning(
                "Worker {WorkerId} tried to complete job {JobId} without the lease (held by {Owner})",
                request.WorkerId, id, job.LeaseOwner ?? "nobody");

            return Result.Forbidden("This run is no longer held by that worker.");
        }

        job.Status = request.Status;
        job.FinishedAt = DateTimeOffset.UtcNow;
        job.Error = request.Error;
        job.LeaseOwner = null;
        job.LeaseExpiresAt = null;
        job.CurrentTask = request.Status switch
        {
            SearchJobStatus.Completed => "Finished",
            SearchJobStatus.Stopped => "Stopped",
            _ => "Failed",
        };

        await db.SaveChangesAsync(ct);

        logger.LogInformation("Job {JobId} finished as {Status}", id, request.Status);
        return Result.Success();
    }

    /// <summary>
    /// Frees jobs whose worker stopped heartbeating.
    /// <para>
    /// This is the recovery path: the job stays non-terminal and simply becomes
    /// claimable again, so another worker picks it up and continues from the
    /// last checkpoint. Nothing is lost and no human has to intervene.
    /// </para>
    /// </summary>
    public async Task<int> ReleaseExpiredLeasesAsync(CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;

        var released = await db.SearchJobs
            .Where(j => j.LeaseOwner != null && j.LeaseExpiresAt != null && j.LeaseExpiresAt <= now)
            .Where(j => j.Status == SearchJobStatus.Running || j.Status == SearchJobStatus.Stopping)
            .ExecuteUpdateAsync(s => s
                .SetProperty(j => j.LeaseOwner, (string?)null)
                .SetProperty(j => j.LeaseExpiresAt, (DateTimeOffset?)null)
                .SetProperty(j => j.Status, SearchJobStatus.Queued)
                .SetProperty(j => j.CurrentTask, "Waiting to resume"), ct);

        if (released > 0)
        {
            logger.LogWarning("Released {Count} job(s) whose worker stopped responding", released);
        }

        return released;
    }

    /// <summary>Users see their own runs; `leads.view-all` sees everyone's.</summary>
    private IQueryable<SearchJob> ScopeToCaller(IQueryable<SearchJob> query)
    {
        if (currentUser.HasPermission(Permissions.Leads.ViewAll)) return query;

        var me = currentUser.UserId;
        return query.Where(j => j.RequestedByUserId == me);
    }

    private static bool IsTerminal(SearchJobStatus status) =>
        status is SearchJobStatus.Completed or SearchJobStatus.Failed or SearchJobStatus.Stopped;

    private static IReadOnlyList<string> DeserializeKeys(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch
        {
            // A corrupt checkpoint costs a redo, not the whole run.
            return [];
        }
    }

    /// <summary>Trims the live-results list so the row cannot grow unbounded.</summary>
    private static string Cap(string json)
    {
        try
        {
            var items = JsonSerializer.Deserialize<List<JsonElement>>(json);
            if (items is null || items.Count <= MaxRecentResults) return json;

            return JsonSerializer.Serialize(items.Take(MaxRecentResults));
        }
        catch
        {
            return "[]";
        }
    }

    private static SearchJobDto Map(SearchJob job) => new()
    {
        Id = job.Id,
        Status = job.Status,
        RequestJson = job.RequestJson,
        TotalTasks = job.TotalTasks,
        CompletedTasks = job.CompletedTasks,
        Found = job.Found,
        Saved = job.Saved,
        Duplicates = job.Duplicates,
        Enriched = job.Enriched,
        EnrichmentFailed = job.EnrichmentFailed,
        EmailsVerified = job.EmailsVerified,
        WhatsAppReachable = job.WhatsAppReachable,
        Skipped = job.Skipped,
        Failed = job.Failed,
        CurrentTask = job.CurrentTask,
        StartedAt = job.StartedAt,
        FinishedAt = job.FinishedAt,
        HeartbeatAt = job.HeartbeatAt,
        StopRequested = job.StopRequested,
        Error = job.Error,
        RequestedByUserId = job.RequestedByUserId,
        Attempts = job.Attempts,
        RecentResultsJson = job.RecentResultsJson,
        IsLeased = !job.IsLeaseExpired(DateTimeOffset.UtcNow),
    };
}
