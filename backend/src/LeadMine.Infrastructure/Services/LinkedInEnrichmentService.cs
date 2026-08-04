using System.Collections.Concurrent;
using LeadMine.Application.Authorization;
using LeadMine.Application.Common;
using LeadMine.Application.DTOs;
using LeadMine.Application.Interfaces;
using LeadMine.Domain.Entities;
using LeadMine.Infrastructure.Persistence;
using LeadMine.Infrastructure.Scraping;
using LeadMine.Infrastructure.Scraping.Enrichment;
using LeadMine.Infrastructure.Scraping.Providers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LeadMine.Infrastructure.Services;

/// <summary>
/// Enriches a bounded batch of already-saved leads with LinkedIn data, for the
/// dedicated LinkedIn Enrichment page — as opposed to
/// <see cref="SearchRunner"/>'s enrichment, which only runs on leads a search
/// just discovered.
/// <para>
/// The Apify calls run concurrently (bounded by
/// <see cref="ScraperOptions.EnrichmentConcurrency"/>), but touch nothing
/// tracked by the <see cref="LeadMineDbContext"/> while doing so —
/// <c>DbContext</c>'s change tracker is not thread-safe, so every entity
/// mutation and <c>People.Add</c> happens in a second, sequential pass once
/// all the slow network calls have finished.
/// </para>
/// </summary>
public sealed class LinkedInEnrichmentService(
    LeadMineDbContext db,
    ICurrentUser currentUser,
    IAuditService audit,
    ApifyLinkedInCompanyService linkedInCompany,
    ApifyLinkedInPeopleService linkedInPeople,
    IOptionsMonitor<ScraperOptions> optionsMonitor,
    ILogger<LinkedInEnrichmentService> logger) : ILinkedInEnrichmentService
{
    private sealed record LeadResult(
        Business Lead,
        LinkedInCompanyResult? Company,
        IReadOnlyList<LinkedInPersonResult> People,
        string? Error);

    public async Task<Result<EnrichLeadsWithLinkedInResultDto>> EnrichAsync(
        EnrichLeadsWithLinkedInRequest request,
        CancellationToken ct = default)
    {
        var options = optionsMonitor.CurrentValue;

        var notReady = ApifyClient.Readiness(options, "LinkedIn enrichment (Apify)");
        if (notReady is not null) return Result<EnrichLeadsWithLinkedInResultDto>.Failure(notReady);

        var leads = await ScopedLeads()
            .Where(b => request.BusinessIds.Contains(b.Id))
            .ToListAsync(ct);

        if (leads.Count == 0)
        {
            return Result<EnrichLeadsWithLinkedInResultDto>.NotFound("None of the selected leads were found.");
        }

        var context = new ProviderContext(options, new RateLimiter(options.RateLimitPerMinute), null);
        var gathered = new ConcurrentBag<LeadResult>();

        await Parallel.ForEachAsync(
            leads,
            new ParallelOptions { MaxDegreeOfParallelism = options.EnrichmentConcurrency, CancellationToken = ct },
            async (lead, token) =>
            {
                LinkedInCompanyResult? company = null;
                IReadOnlyList<LinkedInPersonResult> people = [];
                string? error = null;

                try
                {
                    company = await linkedInCompany.EnrichAsync(lead.Name, lead.LinkedIn, context, token);

                    var linkedInUrl = string.IsNullOrWhiteSpace(lead.LinkedIn) ? company?.LinkedInUrl : lead.LinkedIn;

                    if (!string.IsNullOrWhiteSpace(linkedInUrl))
                    {
                        people = await linkedInPeople.SearchAsync(
                            linkedInUrl, request.MaxDecisionMakersPerCompany, context, token);
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "LinkedIn enrichment failed for {Name}", lead.Name);
                    error = ex.Message;
                }

                gathered.Add(new LeadResult(lead, company, people, error));
            });

        // Sequential from here: every write to the tracked Business entities
        // and every db.People.Add happens on this one thread now that the
        // concurrent network calls are done.
        var result = new EnrichLeadsWithLinkedInResultDto { Requested = request.BusinessIds.Count };

        foreach (var item in gathered)
        {
            var outcome = new LinkedInEnrichmentOutcome
            {
                BusinessId = item.Lead.Id,
                BusinessName = item.Lead.Name,
                Error = item.Error,
            };

            if (item.Company is not null)
            {
                if (!string.IsNullOrWhiteSpace(item.Company.Industry)) item.Lead.Industry = item.Company.Industry;
                if (item.Company.EmployeeCount.HasValue) item.Lead.EmployeeCount = item.Company.EmployeeCount;

                if (!string.IsNullOrWhiteSpace(item.Company.Description))
                {
                    item.Lead.CompanyDescription = item.Company.Description;
                }

                if (string.IsNullOrWhiteSpace(item.Lead.LinkedIn) && !string.IsNullOrWhiteSpace(item.Company.LinkedInUrl))
                {
                    item.Lead.LinkedIn = item.Company.LinkedInUrl;
                }

                outcome.CompanyEnriched = true;
            }

            if (item.Error is null) item.Lead.LastLinkedInEnrichedAt = DateTimeOffset.UtcNow;

            foreach (var person in item.People)
            {
                db.People.Add(new Person
                {
                    BusinessId = item.Lead.Id,
                    CompanyName = item.Lead.Name,
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
                    OwnerUserId = item.Lead.OwnerUserId,
                });
            }

            if (item.Error is null && !string.IsNullOrWhiteSpace(item.Lead.LinkedIn))
            {
                item.Lead.LastPeopleSearchedAt = DateTimeOffset.UtcNow;
            }

            outcome.PeopleFound = item.People.Count;
            result.Outcomes.Add(outcome);
        }

        await db.SaveChangesAsync(ct);

        result.Enriched = result.Outcomes.Count(o => o.CompanyEnriched);
        result.PeopleFound = result.Outcomes.Sum(o => o.PeopleFound);
        result.Failed = result.Outcomes.Count(o => o.Error is not null);

        await audit.LogAsync("Leads.LinkedInEnriched", nameof(Business), null, true,
            new { count = leads.Count, result.Enriched, result.PeopleFound, result.Failed }, ct);

        return Result<EnrichLeadsWithLinkedInResultDto>.Success(result);
    }

    /// <summary>Mirrors <see cref="BusinessService.ScopeToCaller"/>: without view-all, only the caller's own.</summary>
    private IQueryable<Business> ScopedLeads()
    {
        var query = db.Businesses.AsQueryable();
        if (currentUser.HasPermission(Permissions.Leads.ViewAll)) return query;

        var me = currentUser.UserId;
        return query.Where(b => b.OwnerUserId == me);
    }

    private static string? Truncate(string? value, int max) =>
        value is null || value.Length <= max ? value : value[..max];
}
