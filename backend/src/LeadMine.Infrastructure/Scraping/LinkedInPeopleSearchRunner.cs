using System.Text.Json;
using LeadMine.Application.DTOs;
using LeadMine.Application.Interfaces;
using LeadMine.Domain.Entities;
using LeadMine.Domain.Enums;
using LeadMine.Infrastructure.Persistence;
using LeadMine.Infrastructure.Scraping.Browser;
using LeadMine.Infrastructure.Scraping.Enrichment;
using LeadMine.Infrastructure.Scraping.Providers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LeadMine.Infrastructure.Scraping;

/// <summary>
/// Executes one claimed LinkedIn people-search job: resolves a company by
/// name, searches its People tab with a title/keyword/location filter, and
/// saves every match as a standalone <see cref="Person"/> — not tied to a
/// pre-existing saved lead, unlike <see cref="LinkedInEnrichmentRunner"/>.
/// <para>
/// A single browser operation rather than a per-item loop, so there is no
/// meaningful partial checkpoint the way <see cref="LinkedInEnrichmentRunner"/>
/// has per business id — if a worker dies mid-search, the job's lease simply
/// expires and the next attempt reruns the whole search (bounded by
/// <c>SearchJobService.MaxAttempts</c>), same recovery story, coarser grain.
/// </para>
/// </summary>
public sealed class LinkedInPeopleSearchRunner(
    IServiceScopeFactory scopeFactory,
    LinkedInSessionManager session,
    PlaywrightLinkedInPeopleService linkedInPeople,
    IOptionsMonitor<ScraperOptions> optionsMonitor,
    ILogger<LinkedInPeopleSearchRunner> logger)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task RunAsync(ClaimedJobDto job, string workerId, CancellationToken stoppingToken)
    {
        var options = optionsMonitor.CurrentValue;
        var request = ParseRequest(job.RequestJson);

        if (request is null || string.IsNullOrWhiteSpace(request.CompanyName))
        {
            await CompleteAsync(job.Id, workerId, SearchJobStatus.Failed,
                "The stored search request could not be read.", stoppingToken);
            return;
        }

        if (job.OwnerUserId is not { } ownerUserId)
        {
            await CompleteAsync(job.Id, workerId, SearchJobStatus.Failed,
                "This run has no owner, so its results could not be saved.", stoppingToken);
            return;
        }

        using var abort = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var ct = abort.Token;

        try
        {
            var notReady = await session.ReadinessAsync(ownerUserId, ct);
            if (notReady is not null)
            {
                await CompleteAsync(job.Id, workerId, SearchJobStatus.Failed, notReady, stoppingToken);
                return;
            }

            await BeatAsync(job.Id, workerId, options, $"Finding {request.CompanyName} on LinkedIn", null, ct);

            var context = new ProviderContext(options, new RateLimiter(options.RateLimitPerMinute), null);

            // Existence check only — the search itself runs by name (see
            // PlaywrightLinkedInPeopleService's class remarks), so the resolved
            // slug isn't otherwise needed, but confirming the company exists on
            // LinkedIn first gives a precise error instead of a silent 0-found.
            var slug = await ResolveSlugAsync(request.CompanyName, ownerUserId, context, ct);
            if (slug is null)
            {
                await CompleteAsync(job.Id, workerId, SearchJobStatus.Failed,
                    $"Could not find \"{request.CompanyName}\" on LinkedIn.", stoppingToken);
                return;
            }

            await BeatAsync(job.Id, workerId, options, $"Searching LinkedIn for people at {request.CompanyName}", null, ct);

            var people = await linkedInPeople.SearchByKeywordAsync(
                request.CompanyName, request.Keywords, request.Location, request.MaxResults, ownerUserId, context, ct);

            await SaveResultsAsync(job.Id, workerId, ownerUserId, request.CompanyName, people, options, stoppingToken);

            await CompleteAsync(job.Id, workerId, SearchJobStatus.Completed, null, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation("Host stopping mid-run; LinkedIn people-search job {JobId} will be resumed", job.Id);
        }
        catch (OperationCanceledException)
        {
            await CompleteAsync(job.Id, workerId, SearchJobStatus.Stopped, null, stoppingToken);
        }
        catch (ProviderException ex)
        {
            await CompleteAsync(job.Id, workerId, SearchJobStatus.Failed, ex.Message, stoppingToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "LinkedIn people-search job {JobId} failed", job.Id);
            await CompleteAsync(job.Id, workerId, SearchJobStatus.Failed, ex.Message, stoppingToken);
        }
    }

    private async Task<string?> ResolveSlugAsync(string companyName, Guid userId, ProviderContext context, CancellationToken ct)
    {
        await using var lease = await session.AcquireContextAsync(userId, ct);
        var page = await lease.Context.NewPageAsync();

        try
        {
            return await PlaywrightLinkedInCompanyService.ResolveCompanySlugByNameAsync(page, companyName, userId, session, context, ct);
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    private async Task SaveResultsAsync(
        Guid jobId,
        string workerId,
        Guid ownerUserId,
        string companyName,
        IReadOnlyList<LinkedInPersonResult> people,
        ScraperOptions options,
        CancellationToken stoppingToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LeadMineDbContext>();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditService>();

        var outcomes = new List<LinkedInPersonFoundOutcome>();

        foreach (var person in people)
        {
            var entity = new Person
            {
                CompanyName = companyName,
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
                OwnerUserId = ownerUserId,
                SearchJobId = jobId,
            };

            db.People.Add(entity);

            outcomes.Add(new LinkedInPersonFoundOutcome
            {
                FullName = entity.FullName,
                JobTitle = entity.JobTitle,
                LinkedInUrl = entity.LinkedInUrl,
                Location = entity.Location,
                IsDecisionMaker = entity.IsDecisionMaker,
            });
        }

        try
        {
            await db.SaveChangesAsync(stoppingToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to save LinkedIn people-search results for job {JobId}", jobId);
            throw;
        }

        await audit.LogAsync("Leads.LinkedInPeopleSearched", nameof(Person), null, true,
            new { company = companyName, found = people.Count }, stoppingToken);

        await BeatAsync(jobId, workerId, options, "Finished", outcomes, stoppingToken, found: people.Count);
    }

    private async Task BeatAsync(
        Guid jobId,
        string workerId,
        ScraperOptions options,
        string currentTask,
        List<LinkedInPersonFoundOutcome>? outcomes,
        CancellationToken ct,
        int? found = null)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var jobs = scope.ServiceProvider.GetRequiredService<Services.ISearchJobService>();

            await jobs.HeartbeatAsync(jobId, new JobHeartbeatRequest
            {
                WorkerId = workerId,
                LeaseSeconds = options.LeaseSeconds,
                CurrentTask = currentTask.Length > 256 ? currentTask[..256] : currentTask,
                CompletedTaskKey = found.HasValue ? "search" : null,
                Found = found,
                Saved = found,
                RecentResultsJson = outcomes is null ? null : JsonSerializer.Serialize(outcomes, Json),
            }, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Heartbeat failed for LinkedIn people-search job {JobId}", jobId);
        }
    }

    private async Task CompleteAsync(
        Guid jobId, string workerId, SearchJobStatus status, string? error, CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var jobs = scope.ServiceProvider.GetRequiredService<Services.ISearchJobService>();

            await jobs.CompleteAsync(jobId, new CompleteJobRequest
            {
                WorkerId = workerId,
                Status = status,
                Error = Truncate(error, 2000),
            }, stoppingToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not finish LinkedIn people-search job {JobId}", jobId);
        }
    }

    private static string? Truncate(string? value, int max) =>
        value is null || value.Length <= max ? value : value[..max];

    private static LinkedInPeopleSearchRequest? ParseRequest(string json)
    {
        try
        {
            var parsed = JsonSerializer.Deserialize<LinkedInPeopleSearchRequest>(json, Json);
            return string.IsNullOrWhiteSpace(parsed?.CompanyName) ? null : parsed;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
