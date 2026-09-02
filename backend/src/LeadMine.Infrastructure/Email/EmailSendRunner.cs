using System.Text.Json;
using LeadMine.Application.Authorization;
using LeadMine.Application.DTOs;
using LeadMine.Application.Interfaces;
using LeadMine.Domain.Enums;
using LeadMine.Infrastructure.Scraping;
using LeadMine.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LeadMine.Infrastructure.Email;

/// <summary>
/// Executes one claimed email-send job: a preset delivered to a fixed list of
/// already-saved leads.
/// <para>
/// Exists for the same reason as <c>LinkedInEnrichmentRunner</c> — see
/// <see cref="LeadMine.Domain.Enums.JobKind.EmailSend"/>'s remarks. Same
/// lease/claim/heartbeat/checkpoint mechanism as every other job kind;
/// dispatched here by <c>ScraperWorkerService</c>.
/// </para>
/// <para>
/// One recipient per checkpoint (task key = the business id), sent via
/// <see cref="IEmailService.SendAsBackgroundJobAsync"/> rather than the
/// ambient-<c>ICurrentUser</c> <see cref="IEmailService.SendAsync"/> overload
/// — this runs with no HTTP request behind it, so the job's owner is
/// resolved once up front and threaded through explicitly instead.
/// </para>
/// </summary>
public sealed class EmailSendRunner(
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<SmtpOptions> smtpOptionsMonitor,
    IOptionsMonitor<ScraperOptions> scraperOptionsMonitor,
    ILogger<EmailSendRunner> logger)
{
    private const int MaxRecentResults = 200;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task RunAsync(ClaimedJobDto job, string workerId, CancellationToken stoppingToken)
    {
        var request = ParseRequest(job.RequestJson);

        if (request is null || request.BusinessIds.Count == 0)
        {
            await CompleteAsync(job.Id, workerId, SearchJobStatus.Failed,
                "The stored send request could not be read.", stoppingToken);
            return;
        }

        if (job.OwnerUserId is not { } ownerUserId)
        {
            await CompleteAsync(job.Id, workerId, SearchJobStatus.Failed,
                "This run has no owner, so nothing could be sent as them.", stoppingToken);
            return;
        }

        string? senderEmail;
        bool canViewAllLeads;
        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<IUserService>();
            var user = await users.GetByIdAsync(ownerUserId, stoppingToken);
            senderEmail = user.Succeeded ? user.Value?.Email : null;

            canViewAllLeads = await scope.ServiceProvider.GetRequiredService<IPermissionService>()
                .HasPermissionAsync(ownerUserId, Permissions.Leads.ViewAll, stoppingToken);
        }

        using var abort = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var state = new RunState(
            job, workerId, request, ownerUserId, senderEmail, canViewAllLeads, scraperOptionsMonitor.CurrentValue.LeaseSeconds);

        try
        {
            await ExecuteAsync(state, abort, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host shutdown. Leave the job leased and non-terminal: the lease
            // lapses, the reaper requeues it, and the next instance resumes
            // from the checkpoint — same recovery path as every other job kind.
            logger.LogInformation("Host stopping mid-run; email send job {JobId} will be resumed", job.Id);
        }
        catch (OperationCanceledException)
        {
            // A stop the user asked for.
            await CompleteAsync(state.JobId, workerId, SearchJobStatus.Stopped, null, stoppingToken, state.LeaseLost);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Email send job {JobId} failed", job.Id);
            await CompleteAsync(state.JobId, workerId, SearchJobStatus.Failed, ex.Message, stoppingToken, state.LeaseLost);
        }
    }

    private async Task ExecuteAsync(RunState state, CancellationTokenSource abort, CancellationToken stoppingToken)
    {
        var ct = abort.Token;
        var smtp = smtpOptionsMonitor.CurrentValue;
        var scraper = scraperOptionsMonitor.CurrentValue;

        var done = new HashSet<Guid>(
            state.CompletedTaskKeys.Select(k => Guid.TryParse(k, out var id) ? id : Guid.Empty));

        var remaining = state.Request.BusinessIds.Where(id => !done.Contains(id)).ToList();

        foreach (var businessId in remaining)
        {
            if (ct.IsCancellationRequested) break;

            await using var scope = scopeFactory.CreateAsyncScope();
            var email = scope.ServiceProvider.GetRequiredService<IEmailService>();

            state.CurrentTask = "Sending";
            if (!await BeatAsync(state, abort, null, stoppingToken)) break;

            // One recipient per call — SendAsBackgroundJobAsync still runs the
            // full template/quota/scope logic SendAsync does, just for a
            // single-item batch, so nothing about the actual send behaves
            // any differently than the interactive path did.
            var oneRecipient = new SendEmailRequest
            {
                TemplateId = state.Request.TemplateId,
                BusinessIds = [businessId],
                SignatureId = state.Request.SignatureId,
                Cc = state.Request.Cc,
                Bcc = state.Request.Bcc,
                DryRun = state.Request.DryRun,
            };

            var result = await email.SendAsBackgroundJobAsync(
                state.OwnerUserId, state.SenderEmail, state.CanViewAllLeads, oneRecipient, ct);

            if (!result.Succeeded)
            {
                // A batch-level failure (quota hit, preset deactivated, SMTP
                // unreachable) applies identically to every remaining
                // recipient — stop now rather than repeating the same failure
                // down the whole list.
                await CompleteAsync(state.JobId, state.WorkerId, SearchJobStatus.Failed,
                    result.Error ?? "The send failed.", stoppingToken, state.LeaseLost);
                return;
            }

            var outcome = result.Value!.Outcomes.FirstOrDefault();

            if (outcome is null || outcome.Status == "skipped")
            {
                state.Skipped++;
            }
            else if (outcome.Status == "failed")
            {
                state.Failed++;
            }
            else
            {
                state.Sent++;
            }

            state.PushOutcome(outcome ?? new SendEmailOutcome(businessId, string.Empty, "skipped", "Lead not found"));

            if (!await BeatAsync(state, abort, businessId.ToString(), stoppingToken)) break;

            // SendAsBackgroundJobAsync already paces internally between the
            // recipients *within* one call, but that's only ever one recipient
            // here — pace between jobs' own calls too, for the same reason.
            if (smtp.DelayBetweenSendsMs > 0 && !ct.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(smtp.DelayBetweenSendsMs, ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        if (ct.IsCancellationRequested)
        {
            await CompleteAsync(state.JobId, state.WorkerId, SearchJobStatus.Stopped, null, stoppingToken, state.LeaseLost);
            return;
        }

        await CompleteAsync(state.JobId, state.WorkerId, SearchJobStatus.Completed, null, stoppingToken, state.LeaseLost);
    }

    /// <summary>Pushes progress and picks up a stop request. Returns false when the run must end.</summary>
    private async Task<bool> BeatAsync(
        RunState state,
        CancellationTokenSource abort,
        string? completedTaskKey,
        CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var jobs = scope.ServiceProvider.GetRequiredService<ISearchJobService>();

            var response = await jobs.HeartbeatAsync(state.JobId, state.BuildHeartbeat(completedTaskKey), stoppingToken);

            if (!response.LeaseValid)
            {
                logger.LogWarning("Lost the lease on email send job {JobId}; another worker has it now", state.JobId);
                state.LeaseLost = true;
                await abort.CancelAsync();
                return false;
            }

            if (response.StopRequested)
            {
                await abort.CancelAsync();
                return false;
            }

            return true;
        }
        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Heartbeat failed for email send job {JobId}", state.JobId);
            return true;
        }
    }

    private async Task CompleteAsync(
        Guid jobId,
        string workerId,
        SearchJobStatus status,
        string? error,
        CancellationToken stoppingToken,
        bool leaseLost = false)
    {
        if (leaseLost) return;

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var jobs = scope.ServiceProvider.GetRequiredService<ISearchJobService>();

            await jobs.CompleteAsync(jobId, new CompleteJobRequest
            {
                WorkerId = workerId,
                Status = status,
                Error = Truncate(error, 2000),
            }, stoppingToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not finish email send job {JobId}", jobId);
        }
    }

    private static string? Truncate(string? value, int max) =>
        value is null || value.Length <= max ? value : value[..max];

    private static SendEmailRequest? ParseRequest(string json)
    {
        try
        {
            var parsed = JsonSerializer.Deserialize<SendEmailRequest>(json, Json);
            return parsed is { BusinessIds.Count: > 0 } ? parsed : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed class RunState(
        ClaimedJobDto job,
        string workerId,
        SendEmailRequest request,
        Guid ownerUserId,
        string? senderEmail,
        bool canViewAllLeads,
        int leaseSeconds)
    {
        public Guid JobId { get; } = job.Id;
        public string WorkerId { get; } = workerId;
        public SendEmailRequest Request { get; } = request;
        public Guid OwnerUserId { get; } = ownerUserId;
        public string? SenderEmail { get; } = senderEmail;
        public bool CanViewAllLeads { get; } = canViewAllLeads;
        public int LeaseSeconds { get; } = leaseSeconds;
        public IReadOnlyList<string> CompletedTaskKeys { get; } = job.CompletedTaskKeys;

        public string CurrentTask { get; set; } = "Preparing";
        public bool LeaseLost { get; set; }

        public int Sent { get; set; } = job.Saved;
        public int Failed { get; set; } = job.Failed;
        public int Skipped { get; set; } = job.Skipped;

        private readonly List<SendEmailOutcome> _recent = [];

        public void PushOutcome(SendEmailOutcome outcome)
        {
            _recent.Insert(0, outcome);
            if (_recent.Count > MaxRecentResults) _recent.RemoveRange(MaxRecentResults, _recent.Count - MaxRecentResults);
        }

        public JobHeartbeatRequest BuildHeartbeat(string? completedTaskKey) => new()
        {
            WorkerId = WorkerId,
            LeaseSeconds = LeaseSeconds,
            CurrentTask = CurrentTask,
            CompletedTaskKey = completedTaskKey,
            // Repurposed: SearchJobDto's counters are generic, not fixed to
            // the search shape — "Saved" carries sent count here.
            Saved = Sent,
            Failed = Failed,
            Skipped = Skipped,
            RecentResultsJson = JsonSerializer.Serialize(_recent, Json),
        };
    }
}
