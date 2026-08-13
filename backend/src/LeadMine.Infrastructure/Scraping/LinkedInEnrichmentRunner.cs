using System.Text.Json;
using LeadMine.Application.Authorization;
using LeadMine.Application.DTOs;
using LeadMine.Application.Interfaces;
using LeadMine.Domain.Entities;
using LeadMine.Domain.Enums;
using LeadMine.Infrastructure.Persistence;
using LeadMine.Infrastructure.Scraping.Enrichment;
using LeadMine.Infrastructure.Scraping.Providers;
using LeadMine.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LeadMine.Infrastructure.Scraping;

/// <summary>
/// Executes one claimed LinkedIn-enrichment job: company detail plus a
/// decision-maker search for a fixed list of already-saved leads.
/// <para>
/// This exists because the synchronous version — one HTTP request enriching up
/// to 25 leads inline — structurally cannot finish inside
/// <c>app/api/backend/[...path]/route.ts</c>'s proxy timeout (a standard
/// Netlify Function, ~10-26s) once each lead is a real browser session instead
/// of an Apify API call. Same fix <see cref="SearchRunner"/> already uses for
/// the same class of problem: queue a row, claim it under a lease, checkpoint
/// per unit of work, let the caller poll instead of blocking on one request.
/// </para>
/// <para>
/// One business per checkpoint (task key = the business id), processed
/// sequentially — unlike <see cref="SearchRunner"/>'s per-task concurrency,
/// there is no serverless timeout forcing this to hurry, and sequential
/// keeps the single shared <c>PlaywrightBrowserManager</c> browser and the
/// LinkedIn session's daily search budget straightforward to reason about.
/// </para>
/// </summary>
public sealed class LinkedInEnrichmentRunner(
    IServiceScopeFactory scopeFactory,
    PlaywrightLinkedInCompanyService linkedInCompany,
    PlaywrightLinkedInPeopleService linkedInPeople,
    IOptionsMonitor<ScraperOptions> optionsMonitor,
    ILogger<LinkedInEnrichmentRunner> logger)
{
    /// <summary>Live outcomes kept for the progress panel.</summary>
    private const int MaxRecentResults = 200;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task RunAsync(ClaimedJobDto job, string workerId, CancellationToken stoppingToken)
    {
        var options = optionsMonitor.CurrentValue;
        var request = ParseRequest(job.RequestJson);

        if (request is null || request.BusinessIds.Count == 0)
        {
            await CompleteAsync(job.Id, workerId, SearchJobStatus.Failed,
                "The stored enrichment request could not be read.", stoppingToken);
            return;
        }

        if (job.OwnerUserId is not { } ownerUserId)
        {
            await CompleteAsync(job.Id, workerId, SearchJobStatus.Failed,
                "This run has no owner, so its results could not be saved.", stoppingToken);
            return;
        }

        using var abort = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var state = new RunState(job, workerId, request, ownerUserId, options);

        try
        {
            await ExecuteAsync(state, abort, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host shutdown. Leave the job leased and non-terminal: the lease
            // lapses, the reaper requeues it, and the next instance resumes
            // from the checkpoint — same recovery path as SearchRunner.
            logger.LogInformation("Host stopping mid-run; LinkedIn job {JobId} will be resumed", job.Id);
        }
        catch (OperationCanceledException)
        {
            // A stop the user asked for.
            await CompleteAsync(state.JobId, workerId, SearchJobStatus.Stopped, null, stoppingToken, state.LeaseLost);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "LinkedIn enrichment job {JobId} failed", job.Id);
            await CompleteAsync(state.JobId, workerId, SearchJobStatus.Failed, ex.Message, stoppingToken, state.LeaseLost);
        }
    }

    private async Task ExecuteAsync(RunState state, CancellationTokenSource abort, CancellationToken stoppingToken)
    {
        var ct = abort.Token;

        var notReady = await linkedInCompany.ReadinessAsync(state.OwnerUserId, ct);
        if (notReady is not null)
        {
            await CompleteAsync(state.JobId, state.WorkerId, SearchJobStatus.Failed, notReady, stoppingToken);
            return;
        }

        var done = new HashSet<Guid>(
            state.CompletedTaskKeys.Select(k => Guid.TryParse(k, out var id) ? id : Guid.Empty));

        var remaining = state.Request.BusinessIds.Where(id => !done.Contains(id)).ToList();

        var context = new ProviderContext(state.Options, new RateLimiter(state.Options.RateLimitPerMinute), null);

        // Mirrors BusinessService.ScopeToCaller: a caller with leads.view-all
        // can select any lead in the picker, not just their own, so the
        // enrichment lookup below must honour that too — otherwise every
        // lead they don't personally own comes back "not found" even though
        // they could see and select it a moment earlier. Checked once
        // up-front against the job's owner rather than per lead.
        bool canViewAllLeads;
        await using (var permissionScope = scopeFactory.CreateAsyncScope())
        {
            canViewAllLeads = await permissionScope.ServiceProvider.GetRequiredService<IPermissionService>()
                .HasPermissionAsync(state.OwnerUserId, Permissions.Leads.ViewAll, ct);
        }

        foreach (var businessId in remaining)
        {
            if (ct.IsCancellationRequested) break;

            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<LeadMineDbContext>();
            var audit = scope.ServiceProvider.GetRequiredService<IAuditService>();

            var lead = await db.Businesses
                .FirstOrDefaultAsync(b => b.Id == businessId && (canViewAllLeads || b.OwnerUserId == state.OwnerUserId), ct);

            if (lead is null)
            {
                state.Failed++;
                state.PushOutcome(new LinkedInEnrichmentOutcome
                {
                    BusinessId = businessId,
                    BusinessName = "(not found)",
                    Error = "Lead not found, or no longer owned by this account.",
                });

                if (!await BeatAsync(state, abort, businessId.ToString(), stoppingToken)) break;
                continue;
            }

            state.CurrentTask = $"Enriching {lead.Name} via LinkedIn";
            if (!await BeatAsync(state, abort, null, stoppingToken)) break;

            LinkedInCompanyResult? company = null;
            IReadOnlyList<LinkedInPersonResult> people = [];
            string? error = null;

            try
            {
                company = await linkedInCompany.EnrichAsync(lead.Name, lead.LinkedIn, state.OwnerUserId, context, ct);

                var linkedInUrl = string.IsNullOrWhiteSpace(lead.LinkedIn) ? company?.LinkedInUrl : lead.LinkedIn;

                // Gated on having found the company on LinkedIn at all, same as
                // before — but the search itself now runs by name, not URL: see
                // PlaywrightLinkedInPeopleService's class remarks for why a
                // company-scoped URL no longer returns any individual profiles.
                if (!string.IsNullOrWhiteSpace(linkedInUrl))
                {
                    people = await linkedInPeople.SearchAsync(
                        lead.Name, state.Request.MaxDecisionMakersPerCompany, state.OwnerUserId, context, ct);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (ProviderException ex) when (ex.IsFatal)
            {
                // A missing/expired LinkedIn session fails every remaining
                // lead identically — stop now rather than burning through the
                // rest of the batch on a session that will not come back.
                await CompleteAsync(state.JobId, state.WorkerId, SearchJobStatus.Failed, ex.Message, stoppingToken);
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "LinkedIn enrichment failed for {Name}", lead.Name);
                error = ex.Message;
            }

            if (company is not null)
            {
                if (!string.IsNullOrWhiteSpace(company.Industry)) lead.Industry = company.Industry;
                if (company.EmployeeCount.HasValue) lead.EmployeeCount = company.EmployeeCount;
                if (!string.IsNullOrWhiteSpace(company.Description)) lead.CompanyDescription = company.Description;

                if (string.IsNullOrWhiteSpace(lead.LinkedIn) && !string.IsNullOrWhiteSpace(company.LinkedInUrl))
                {
                    lead.LinkedIn = company.LinkedInUrl;
                }
            }

            if (error is null) lead.LastLinkedInEnrichedAt = DateTimeOffset.UtcNow;

            foreach (var person in people)
            {
                db.People.Add(new Person
                {
                    BusinessId = lead.Id,
                    CompanyName = lead.Name,
                    FullName = Truncate(person.FullName, 256) ?? string.Empty,
                    JobTitle = Truncate(person.JobTitle, 256) ?? string.Empty,
                    Headline = Truncate(person.Headline, 512) ?? string.Empty,
                    LinkedInUrl = Truncate(person.LinkedInUrl, 512) ?? string.Empty,
                    Location = Truncate(person.Location, 256) ?? string.Empty,
                    ExperienceJson = person.ExperienceJson ?? string.Empty,
                    EducationJson = person.EducationJson ?? string.Empty,
                    SkillsJson = person.SkillsJson ?? string.Empty,
                    IsDecisionMaker = person.IsDecisionMaker,
                    DecisionMakerRole = Truncate(person.DecisionMakerRole, 256) ?? string.Empty,
                    OwnerUserId = state.OwnerUserId,
                    SearchJobId = state.JobId,
                });
            }

            if (error is null && !string.IsNullOrWhiteSpace(lead.LinkedIn))
            {
                lead.LastPeopleSearchedAt = DateTimeOffset.UtcNow;
            }

            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to save LinkedIn enrichment for {Name}", lead.Name);
                error ??= "Enriched, but the result could not be saved.";
            }

            await audit.LogAsync(
                "Leads.LinkedInEnriched", nameof(Business), lead.Id.ToString(), error is null,
                new { lead.Name, companyEnriched = company is not null, peopleFound = people.Count }, ct);

            if (company is not null) state.Enriched++;
            if (error is not null) state.Failed++;
            state.PeopleFound += people.Count;

            state.PushOutcome(new LinkedInEnrichmentOutcome
            {
                BusinessId = lead.Id,
                BusinessName = lead.Name,
                CompanyEnriched = company is not null,
                PeopleFound = people.Count,
                Error = error,
            });

            if (!await BeatAsync(state, abort, businessId.ToString(), stoppingToken)) break;

            if (state.Options.DelayMs > 0 && !ct.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(state.Options.DelayMs, ct);
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
                logger.LogWarning("Lost the lease on LinkedIn job {JobId}; another worker has it now", state.JobId);
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
            logger.LogWarning(ex, "Heartbeat failed for LinkedIn job {JobId}", state.JobId);
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
            logger.LogWarning(ex, "Could not finish LinkedIn job {JobId}", jobId);
        }
    }

    private static string? Truncate(string? value, int max) =>
        value is null || value.Length <= max ? value : value[..max];

    private static EnrichLeadsWithLinkedInRequest? ParseRequest(string json)
    {
        try
        {
            var parsed = JsonSerializer.Deserialize<EnrichLeadsWithLinkedInRequest>(json, Json);
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
        EnrichLeadsWithLinkedInRequest request,
        Guid ownerUserId,
        ScraperOptions options)
    {
        public Guid JobId { get; } = job.Id;
        public string WorkerId { get; } = workerId;
        public EnrichLeadsWithLinkedInRequest Request { get; } = request;
        public Guid OwnerUserId { get; } = ownerUserId;
        public ScraperOptions Options { get; } = options;
        public IReadOnlyList<string> CompletedTaskKeys { get; } = job.CompletedTaskKeys;

        public string CurrentTask { get; set; } = "Preparing";
        public bool LeaseLost { get; set; }

        public int Enriched { get; set; } = job.Enriched;
        public int PeopleFound { get; set; } = job.Saved;
        public int Failed { get; set; } = job.Failed;

        private readonly List<LinkedInEnrichmentOutcome> _recent = [];

        public void PushOutcome(LinkedInEnrichmentOutcome outcome)
        {
            _recent.Insert(0, outcome);
            if (_recent.Count > MaxRecentResults) _recent.RemoveRange(MaxRecentResults, _recent.Count - MaxRecentResults);
        }

        public JobHeartbeatRequest BuildHeartbeat(string? completedTaskKey) => new()
        {
            WorkerId = WorkerId,
            LeaseSeconds = Options.LeaseSeconds,
            CurrentTask = CurrentTask.Length > 256 ? CurrentTask[..256] : CurrentTask,
            CompletedTaskKey = completedTaskKey,
            Enriched = Enriched,
            // Repurposed: "Saved" has no natural meaning for this job kind, so
            // it carries the decision-maker count instead — SearchJobDto's
            // fields are generic counters, not fixed to the search shape.
            Saved = PeopleFound,
            Failed = Failed,
            RecentResultsJson = JsonSerializer.Serialize(_recent, Json),
        };
    }
}
