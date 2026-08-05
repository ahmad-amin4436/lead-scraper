using System.Text.Json;
using System.Text.Json.Serialization;
using LeadMine.Application.DTOs;
using LeadMine.Domain.Entities;
using LeadMine.Domain.Enums;
using LeadMine.Infrastructure.Persistence;
using LeadMine.Infrastructure.Scraping.Enrichment;
using LeadMine.Infrastructure.Scraping.Providers;
using LeadMine.Infrastructure.Scraping.Verification;
using LeadMine.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LeadMine.Infrastructure.Scraping;

/// <summary>
/// Executes one claimed search run.
/// <para>
/// The unit of progress is a (city × category) task. Each is checkpointed to the
/// database as it finishes, so if this process dies — a crash, a deploy, an app
/// pool recycle — the next worker skips what is already done rather than
/// re-scraping it.
/// </para>
/// <para>
/// Database work is done in short-lived scopes rather than one scope held for
/// the whole run: a run can last minutes, and a <c>DbContext</c> open that long
/// would hold a pooled connection and accumulate tracked entities for no reason.
/// </para>
/// </summary>
public sealed class SearchRunner(
    IServiceScopeFactory scopeFactory,
    ProviderRegistry providers,
    GeocodingService geocoding,
    EnrichmentService enrichment,
    VerificationService verification,
    PlaywrightMapsEnrichmentService mapsEnrichment,
    PlaywrightLinkedInCompanyService linkedInCompany,
    PlaywrightLinkedInPeopleService linkedInPeople,
    IOptionsMonitor<ScraperOptions> optionsMonitor,
    ILogger<SearchRunner> logger)
{
    /// <summary>Live results kept for the progress panel.</summary>
    private const int MaxRecentResults = 200;

    private static readonly JsonSerializerOptions LiveResultJson = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public async Task RunAsync(ClaimedJobDto job, string workerId, CancellationToken stoppingToken)
    {
        var options = optionsMonitor.CurrentValue;
        var payload = ParseRequest(job.RequestJson);

        if (payload is null)
        {
            await CompleteAsync(job.Id, workerId, SearchJobStatus.Failed,
                "The stored search request could not be read.", stoppingToken);
            return;
        }

        if (job.OwnerUserId is not { } ownerUserId)
        {
            await CompleteAsync(job.Id, workerId, SearchJobStatus.Failed,
                "This run has no owner, so its leads could not be saved.", stoppingToken);
            return;
        }

        // Cancels on a stop request, a lost lease, or host shutdown.
        using var abort = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);

        var state = new RunState(job, workerId, payload, ownerUserId, options);

        try
        {
            await ExecuteAsync(state, abort, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // The host is shutting down. Leave the job leased and non-terminal:
            // the lease lapses, the reaper requeues it, and the next instance
            // resumes from the checkpoint. Reporting a status here would turn a
            // routine restart into a failed run.
            logger.LogInformation("Host stopping mid-run; job {JobId} will be resumed", job.Id);
        }
        catch (OperationCanceledException)
        {
            // A stop the user asked for, surfacing as cancellation from deep
            // inside the crawl. That is a Stopped run, not a failed one.
            await FlushAsync(state, stoppingToken);
            await CompleteAsync(state.JobId, workerId, SearchJobStatus.Stopped, null, stoppingToken, state.LeaseLost);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Search job {JobId} failed", job.Id);

            await FlushAsync(state, stoppingToken);
            await CompleteAsync(state.JobId, workerId, SearchJobStatus.Failed, ex.Message, stoppingToken, state.LeaseLost);
        }
    }

    private async Task ExecuteAsync(RunState state, CancellationTokenSource abort, CancellationToken stoppingToken)
    {
        var ct = abort.Token;

        // Resolve the provider before anything else: a missing API key should
        // fail the run immediately, not after geocoding every city.
        IPlaceProvider provider;

        try
        {
            provider = providers.Select(state.Payload.Provider, state.ProviderContext);
        }
        catch (ProviderException ex)
        {
            await CompleteAsync(state.JobId, state.WorkerId, SearchJobStatus.Failed, ex.Message, stoppingToken);
            return;
        }

        // Once true, a Google quota failure has already downgraded this run to
        // the fallback provider for good — no point trying Google again this attempt.
        var switchedFallback = false;

        // Builds the query for the current loop iteration; declared once so the
        // primary attempt and the post-fallback retry construct it identically.
        Task<IReadOnlyList<ProviderBusiness>> SearchTaskAsync(SearchTask t, ResolvedLocation loc) =>
            provider.SearchAsync(
                new ProviderQuery(t.Category, t.CategoryLabel, loc, state.Payload.RadiusMeters, state.Payload.MaxResults),
                state.ProviderContext,
                ct);

        var tasks = state.Payload.EnumerateTasks()
            .Select(t => new SearchTask(t.Key, t.City, t.Category, CategoryCatalog.Resolve(t.Category).Label))
            .ToList();

        state.TotalTasks = tasks.Count;

        // The resume checkpoint: everything already finished is skipped.
        var done = new HashSet<string>(state.CompletedTaskKeys, StringComparer.Ordinal);
        var remaining = tasks.Where(t => !done.Contains(t.Key)).ToList();

        if (remaining.Count == 0)
        {
            await CompleteAsync(state.JobId, state.WorkerId, SearchJobStatus.Completed, null, stoppingToken);
            return;
        }

        // Geocode up front, so a misspelled city fails before any crawling.
        var locations = await ResolveLocationsAsync(state, remaining, abort, stoppingToken);

        if (locations.Count == 0 && !ct.IsCancellationRequested)
        {
            await CompleteAsync(state.JobId, state.WorkerId, SearchJobStatus.Failed,
                "None of the selected cities could be located. Check the spelling and try again.",
                stoppingToken);
            return;
        }

        foreach (var task in remaining)
        {
            if (ct.IsCancellationRequested) break;

            if (!locations.TryGetValue(task.City, out var location))
            {
                state.Skipped++;
                await BeatAsync(state, abort, task.Key, stoppingToken);
                continue;
            }

            state.CurrentTask = $"Searching {task.CategoryLabel} in {task.City}";
            if (!await BeatAsync(state, abort, null, stoppingToken)) break;

            IReadOnlyList<ProviderBusiness> businesses;
            state.TasksAttempted++;

            try
            {
                businesses = await SearchTaskAsync(task, location);
                state.Found += businesses.Count;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (ProviderException ex) when (ex.IsFatal)
            {
                // A bad key fails every remaining task identically; stop now
                // rather than burning through the rest of the sweep.
                await FlushAsync(state, stoppingToken);
                await CompleteAsync(state.JobId, state.WorkerId, SearchJobStatus.Failed, ex.Message, stoppingToken);
                return;
            }
            catch (ProviderException ex) when (ex.Failure == ProviderFailure.QuotaExceeded && !switchedFallback)
            {
                // A billed provider running dry does not mean every other
                // source is also out of road. Switch once for the rest of the
                // sweep, and retry this same task immediately on the new
                // provider rather than letting the switch itself cost a task.
                switchedFallback = true;

                // Google Maps via a real browser first — it is the closer
                // substitute for Google Places' data. OpenStreetMap is the
                // last resort, used only if the browser itself cannot run (see
                // PlaywrightBrowserManager.Readiness), so a quota exhaustion
                // never leaves the sweep with nothing.
                string fallbackId;
                string fallbackLabel;

                try
                {
                    provider = providers.Select(ProviderRegistry.BrowserId, state.ProviderContext);
                    fallbackId = ProviderRegistry.BrowserId;
                    fallbackLabel = "Google Maps (browser)";
                }
                catch (ProviderException)
                {
                    provider = providers.Select(ProviderRegistry.OpenStreetMapId, state.ProviderContext);
                    fallbackId = ProviderRegistry.OpenStreetMapId;
                    fallbackLabel = "OpenStreetMap";
                }

                logger.LogWarning(
                    ex, "Google Places quota exhausted on job {JobId}; switching to {Fallback} for the rest of the run",
                    state.JobId, fallbackId);

                state.CurrentTask = $"Google Places quota exceeded — switching to {fallbackLabel}";
                await BeatAsync(state, abort, null, stoppingToken);

                try
                {
                    businesses = await SearchTaskAsync(task, location);
                    state.Found += businesses.Count;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception retryEx)
                {
                    state.Failed++;
                    state.TasksFailed++;
                    state.LastTaskError = retryEx.Message;

                    logger.LogWarning(retryEx, "Task {Key} failed on the {Fallback} fallback too", task.Key, fallbackId);

                    await BeatAsync(state, abort, task.Key, stoppingToken);
                    continue;
                }
            }
            catch (Exception ex)
            {
                state.Failed++;
                state.TasksFailed++;
                state.LastTaskError = ex.Message;

                logger.LogWarning(ex, "Task {Key} failed", task.Key);

                // Checkpoint anyway: a category with no results should not be
                // retried forever on the next resume.
                await BeatAsync(state, abort, task.Key, stoppingToken);
                continue;
            }

            var kept = businesses.Where(b => PassesRatingFilters(b, state.Payload)).ToList();
            state.Skipped += businesses.Count - kept.Count;

            var leads = kept.Select(b => ToLead(b, location)).ToList();

            if (state.Payload.EnrichContacts && leads.Count > 0)
            {
                await EnrichAsync(state, leads, task, abort, stoppingToken);
            }

            if ((state.Payload.EnrichGoogleMaps || state.Payload.EnrichLinkedIn) && leads.Count > 0)
            {
                await EnrichWithBrowserAsync(state, leads, task, abort, stoppingToken);
            }

            foreach (var lead in leads)
            {
                if (ct.IsCancellationRequested) break;

                try
                {
                    var outcome = await verification.ApplyToAsync(lead, ct);

                    if (outcome.EmailStatus == EmailStatus.Valid) state.EmailsVerified++;

                    if (outcome.WhatsAppStatus is WhatsAppStatus.Confirmed or WhatsAppStatus.Likely)
                    {
                        state.WhatsAppReachable++;
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    // Verification is best-effort; the lead is still worth keeping.
                    logger.LogDebug(ex, "Verification failed for {Name}", lead.Name);
                }

                if (!MatchesLeadKind(lead, state.Payload.LeadKind))
                {
                    state.Skipped++;
                    continue;
                }

                state.Pending.Add(lead);
                state.PushRecent(lead);

                // Write in batches rather than buffering a whole task: a run that
                // dies mid-task then loses at most one batch, not everything the
                // task found.
                if (state.Pending.Count >= state.Options.SaveBatchSize)
                {
                    await FlushAsync(state, stoppingToken);
                }
            }

            // Save before checkpointing, so a crash between the two re-runs the
            // task rather than losing its leads.
            await FlushAsync(state, stoppingToken);

            // People search needs a real, saved Business id (it is the FK on
            // Person), so it runs here — after the flush, against rows already
            // in the database — rather than alongside the other enrichment
            // steps above, which only ever see in-memory candidates.
            if (state.Payload.EnrichLinkedIn && !ct.IsCancellationRequested)
            {
                await SearchLinkedInPeopleAsync(state, abort, stoppingToken);
            }

            if (!await BeatAsync(state, abort, task.Key, stoppingToken)) break;

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

        await FlushAsync(state, stoppingToken);

        // Cancellation here always came from a stop request or a lost lease —
        // host shutdown would have thrown out of this method instead.
        if (ct.IsCancellationRequested)
        {
            await CompleteAsync(state.JobId, state.WorkerId, SearchJobStatus.Stopped, null, stoppingToken, state.LeaseLost);
            return;
        }

        // A sweep in which every task failed is a failed run, not a completed one
        // that happened to find nothing. Reporting "Completed, 0 found" for an
        // unreachable provider reads as "there are none there", which is a
        // different and much more expensive conclusion for the user to draw.
        var allFailed = state.TasksAttempted > 0 && state.TasksFailed == state.TasksAttempted;

        // No "of N" denominator: after a resume this attempt only ran the tasks
        // that were still outstanding, so "1 of 2" on a four-task run would read
        // as a miscount rather than as the honest per-attempt figure it is.
        var summary = state.TasksFailed == 0
            ? null
            : $"{state.TasksFailed} task(s) failed. Last error: {state.LastTaskError}";

        await CompleteAsync(
            state.JobId,
            state.WorkerId,
            allFailed ? SearchJobStatus.Failed : SearchJobStatus.Completed,
            summary,
            stoppingToken,
            state.LeaseLost);
    }

    private async Task<Dictionary<string, ResolvedLocation>> ResolveLocationsAsync(
        RunState state,
        IReadOnlyList<SearchTask> remaining,
        CancellationTokenSource abort,
        CancellationToken stoppingToken)
    {
        var locations = new Dictionary<string, ResolvedLocation>(StringComparer.OrdinalIgnoreCase);
        var ct = abort.Token;

        foreach (var city in remaining.Select(t => t.City).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (ct.IsCancellationRequested) break;

            state.CurrentTask = $"Locating {city}";
            if (!await BeatAsync(state, abort, null, stoppingToken)) break;

            try
            {
                var resolved = await geocoding.ResolveAsync(
                    state.Payload.Country,
                    state.Payload.State,
                    city,
                    state.ProviderContext,
                    ct);

                if (resolved is not null) locations[city] = resolved;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // One unresolvable city should not sink a multi-city sweep.
                logger.LogWarning(ex, "Geocoding failed for {City}", city);
            }
        }

        return locations;
    }

    /// <summary>
    /// Crawls the websites of a task's leads, several at a time.
    /// <para>
    /// Bounded by <see cref="ScraperOptions.EnrichmentConcurrency"/> because this
    /// runs inside the API process — unbounded crawling would starve the thread
    /// pool that is also serving user requests.
    /// </para>
    /// </summary>
    private async Task EnrichAsync(
        RunState state,
        List<IngestLead> leads,
        SearchTask task,
        CancellationTokenSource abort,
        CancellationToken stoppingToken)
    {
        var withSite = leads.Where(l => !string.IsNullOrWhiteSpace(l.Website)).ToList();
        if (withSite.Count == 0) return;

        var completed = 0;

        // Ticks rather than a captured DateTimeOffset, so the "is another beat
        // due" check is a single atomic read across the parallel workers.
        var lastBeatTicks = DateTime.UtcNow.Ticks;
        var beatInterval = TimeSpan.FromSeconds(15).Ticks;

        await Parallel.ForEachAsync(
            withSite,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = state.Options.EnrichmentConcurrency,
                CancellationToken = abort.Token,
            },
            async (lead, ct) =>
            {
                try
                {
                    var result = await enrichment.EnrichAsync(lead.Website, state.Options, ct);

                    lead.Email = result.Contact.Email ?? string.Empty;
                    lead.WhatsApp = result.Contact.WhatsApp ?? string.Empty;
                    lead.Facebook = result.Contact.Facebook ?? string.Empty;
                    lead.Instagram = result.Contact.Instagram ?? string.Empty;
                    lead.LinkedIn = result.Contact.LinkedIn ?? string.Empty;
                    lead.Status = result.Status;

                    if (string.IsNullOrWhiteSpace(lead.Phone) && result.Contact.WebsitePhones.Count > 0)
                    {
                        lead.Phone = result.Contact.WebsitePhones[0];
                    }

                    if (result.Status == BusinessStatus.EnrichmentFailed) Interlocked.Increment(ref state.EnrichmentFailed);
                    else Interlocked.Increment(ref state.Enriched);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "Enrichment failed for {Website}", lead.Website);
                    lead.Status = BusinessStatus.EnrichmentFailed;
                    Interlocked.Increment(ref state.EnrichmentFailed);
                }

                var done = Interlocked.Increment(ref completed);
                state.CurrentTask = $"Enriching {task.CategoryLabel} in {task.City} ({done}/{withSite.Count})";

                // Enrichment is the slowest phase, so it must beat on its own —
                // otherwise a large task could outlive the lease mid-crawl.
                var now = DateTime.UtcNow.Ticks;
                var previous = Interlocked.Read(ref lastBeatTicks);

                if (now - previous > beatInterval &&
                    Interlocked.CompareExchange(ref lastBeatTicks, now, previous) == previous)
                {
                    await BeatAsync(state, abort, null, stoppingToken);
                }
            });
    }

    /// <summary>
    /// Google Maps detail and LinkedIn company enrichment via a real browser,
    /// opt-in (<see cref="SearchRequestPayload.EnrichGoogleMaps"/>/
    /// <see cref="SearchRequestPayload.EnrichLinkedIn"/>) since each is a real
    /// page load, not a cheap API call. Runs on every lead regardless of
    /// whether it has a website — unlike <see cref="EnrichAsync"/>, neither of
    /// these needs one.
    /// <para>
    /// Best-effort like the website crawl above: a failure on one lead (a
    /// selector that no longer matches, nothing found, a session hiccup) is
    /// logged and skipped, not allowed to fail the task over what the rest of
    /// the batch still found.
    /// </para>
    /// </summary>
    private async Task EnrichWithBrowserAsync(
        RunState state,
        List<IngestLead> leads,
        SearchTask task,
        CancellationTokenSource abort,
        CancellationToken stoppingToken)
    {
        var completed = 0;
        var lastBeatTicks = DateTime.UtcNow.Ticks;
        var beatInterval = TimeSpan.FromSeconds(15).Ticks;

        await Parallel.ForEachAsync(
            leads,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = state.Options.EnrichmentConcurrency,
                CancellationToken = abort.Token,
            },
            async (lead, ct) =>
            {
                if (state.Payload.EnrichGoogleMaps)
                {
                    try
                    {
                        var maps = await mapsEnrichment.EnrichAsync(
                            lead.Name, lead.City, lead.State, lead.Country, lead.MapsUrl, state.ProviderContext, ct);

                        if (maps is not null)
                        {
                            if (maps.PostalCode is { } postalCode) lead.PostalCode = postalCode;
                            if (maps.OpeningHoursJson is { } hours) lead.OpeningHoursJson = hours;
                            if (maps.PlaceId is { } placeId) lead.PlaceId = placeId;
                            if (maps.ImageUrlsJson is { } images) lead.ImageUrlsJson = images;
                            if (maps.PermanentlyClosed.HasValue) lead.PermanentlyClosed = maps.PermanentlyClosed;
                            if (maps.Rating.HasValue) lead.Rating = maps.Rating;
                            if (maps.ReviewCount.HasValue) lead.ReviewCount = maps.ReviewCount;
                        }

                        lead.MapsEnrichedAt = DateTimeOffset.UtcNow;
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        logger.LogDebug(ex, "Google Maps (browser) enrichment failed for {Name}", lead.Name);
                    }
                }

                if (state.Payload.EnrichLinkedIn)
                {
                    try
                    {
                        var company = await linkedInCompany.EnrichAsync(lead.Name, lead.LinkedIn, state.ProviderContext, ct);

                        if (company is not null)
                        {
                            if (company.Industry is { } industry) lead.Industry = industry;
                            if (company.EmployeeCount.HasValue) lead.EmployeeCount = company.EmployeeCount;
                            if (company.Description is { } description) lead.CompanyDescription = description;

                            // The people-search phase after ingest needs this
                            // URL, so it must land on the lead even though
                            // Business already has a LinkedIn field for other
                            // reasons (a link the website crawler happened to find).
                            if (string.IsNullOrWhiteSpace(lead.LinkedIn) && company.LinkedInUrl is { } linkedInUrl)
                            {
                                lead.LinkedIn = linkedInUrl;
                            }
                        }

                        lead.LinkedInEnrichedAt = DateTimeOffset.UtcNow;
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        logger.LogDebug(ex, "LinkedIn company enrichment (browser) failed for {Name}", lead.Name);
                    }
                }

                var done = Interlocked.Increment(ref completed);
                state.CurrentTask = $"Enriching {task.CategoryLabel} in {task.City} ({done}/{leads.Count})";

                var now = DateTime.UtcNow.Ticks;
                var previous = Interlocked.Read(ref lastBeatTicks);

                if (now - previous > beatInterval &&
                    Interlocked.CompareExchange(ref lastBeatTicks, now, previous) == previous)
                {
                    await BeatAsync(state, abort, null, stoppingToken);
                }
            });
    }

    /// <summary>
    /// Finds decision-makers at each company this task just saved, via
    /// LinkedIn people search. Runs after the flush rather than alongside
    /// <see cref="EnrichWithBrowserAsync"/> because <c>Person.BusinessId</c> needs
    /// a real, saved business id.
    /// <para>
    /// Scoped to this job (<c>SearchJobId</c>) and guarded by
    /// <c>LastPeopleSearchedAt IS NULL</c> so a resumed run does not re-search
    /// (and re-bill) a company it already covered on a prior attempt.
    /// </para>
    /// </summary>
    private async Task SearchLinkedInPeopleAsync(
        RunState state,
        CancellationTokenSource abort,
        CancellationToken stoppingToken)
    {
        var ct = abort.Token;

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LeadMineDbContext>();

        var pending = await db.Businesses
            .Where(b => b.SearchJobId == state.JobId && b.LastPeopleSearchedAt == null && b.LinkedIn != "")
            .ToListAsync(ct);

        if (pending.Count == 0) return;

        foreach (var business in pending)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                var people = await linkedInPeople.SearchAsync(
                    business.LinkedIn, state.Payload.MaxDecisionMakersPerCompany, state.ProviderContext, ct);

                foreach (var person in people)
                {
                    db.People.Add(new Person
                    {
                        BusinessId = business.Id,
                        CompanyName = business.Name,
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
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Best-effort, same as every other enrichment step: one
                // company's search failing should not lose the rest.
                logger.LogDebug(ex, "LinkedIn people search (browser) failed for {Name}", business.Name);
            }

            business.LastPeopleSearchedAt = DateTimeOffset.UtcNow;

            state.CurrentTask = $"Searching LinkedIn for decision-makers at {business.Name}";
            await BeatAsync(state, abort, null, stoppingToken);
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to save LinkedIn people results for job {JobId}", state.JobId);
        }
    }

    // --- persistence --------------------------------------------------------

    /// <summary>Writes the buffered leads. Never throws: a save failure is counted, not fatal.</summary>
    private async Task FlushAsync(RunState state, CancellationToken stoppingToken)
    {
        if (state.Pending.Count == 0) return;

        var batch = state.Pending.ToList();
        state.Pending.Clear();

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var ingest = scope.ServiceProvider.GetRequiredService<ILeadIngestService>();

            var outcome = await ingest.IngestAsync(new IngestLeadsRequest
            {
                OwnerUserId = state.OwnerUserId,
                SearchJobId = state.JobId,
                SkipDuplicates = state.Payload.SkipDuplicates,
                Leads = batch,
            }, stoppingToken);

            state.Saved += outcome.Saved;
            state.Duplicates += outcome.Duplicates;
        }
        catch (Exception ex)
        {
            // Never drop scraped leads silently: count the failure so it shows up
            // in the run summary rather than as a mysteriously short result set.
            state.Failed += batch.Count;
            logger.LogError(ex, "Failed to save {Count} lead(s) for job {JobId}", batch.Count, state.JobId);
        }
    }

    /// <summary>
    /// Pushes progress and picks up a stop request from the response.
    /// Returns false when the run must end.
    /// </summary>
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
                logger.LogWarning("Lost the lease on job {JobId}; another worker has it now", state.JobId);
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
            // A transient database blip must not kill the run — the lease has
            // slack, and the next beat will catch up.
            logger.LogWarning(ex, "Heartbeat failed for job {JobId}", state.JobId);
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
        // The job belongs to someone else now, so this worker must not report an
        // outcome — that would truncate a run about to be resumed elsewhere.
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
            logger.LogWarning(ex, "Could not finish job {JobId}", jobId);
        }
    }

    // --- mapping and filters ------------------------------------------------

    private static bool PassesRatingFilters(ProviderBusiness business, SearchRequestPayload payload)
    {
        if (payload.MinRating is { } minRating && (business.Rating ?? 0) < minRating) return false;
        if (payload.MinReviews is { } minReviews && (business.ReviewCount ?? 0) < minReviews) return false;
        return true;
    }

    /// <summary>Mirrors <see cref="LeadKind"/> so run-time and query-time agree.</summary>
    private static bool MatchesLeadKind(IngestLead lead, LeadKind kind) => kind switch
    {
        LeadKind.Any => true,
        LeadKind.New => lead.Status == BusinessStatus.New,
        LeadKind.Enriched => lead.Status == BusinessStatus.Enriched,
        LeadKind.NoWebsite => lead.Status == BusinessStatus.NoWebsite,
        LeadKind.Partial => lead.Status == BusinessStatus.Partial,
        LeadKind.WhatsAppOnly => lead.WhatsAppStatus is WhatsAppStatus.Confirmed or WhatsAppStatus.Likely,
        LeadKind.EmailOnly => !string.IsNullOrWhiteSpace(lead.Email),
        LeadKind.DeliverableEmail => !string.IsNullOrWhiteSpace(lead.Email) && lead.EmailStatus == EmailStatus.Valid,
        _ => true,
    };

    private static IngestLead ToLead(ProviderBusiness business, ResolvedLocation location) => new()
    {
        Name = Truncate(business.Name, 256) ?? string.Empty,
        Category = Truncate(business.Category, 128) ?? string.Empty,
        Country = Truncate(Fallback(business.Country, location.Country), 128) ?? string.Empty,
        State = Truncate(Fallback(business.State, location.State), 128) ?? string.Empty,
        City = Truncate(Fallback(business.City, location.City), 128) ?? string.Empty,
        Address = Truncate(business.Address, 512) ?? string.Empty,
        Phone = Truncate(business.Phone, 64) ?? string.Empty,
        Website = Truncate(business.Website, 512) ?? string.Empty,
        Latitude = business.Latitude,
        Longitude = business.Longitude,
        Rating = business.Rating,
        ReviewCount = business.ReviewCount,
        MapsUrl = Truncate(business.MapsUrl, 1024) ?? string.Empty,
        Source = business.Source,
        // Without a website there is nothing to crawl, so say so up front rather
        // than reporting an enrichment failure that was never attempted.
        Status = string.IsNullOrWhiteSpace(business.Website) ? BusinessStatus.NoWebsite : BusinessStatus.New,
    };

    private static string Fallback(string value, string alternative) =>
        string.IsNullOrWhiteSpace(value) ? alternative : value;

    private static string? Truncate(string? value, int max) =>
        value is null || value.Length <= max ? value : value[..max];

    private static SearchRequestPayload? ParseRequest(string json)
    {
        try
        {
            var parsed = JsonSerializer.Deserialize<SearchRequestPayload>(json, LiveResultJson);
            return parsed is { Categories.Count: > 0, Cities.Count: > 0 } ? parsed : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record SearchTask(string Key, string City, string Category, string CategoryLabel);

    /// <summary>
    /// Everything that changes as a run proceeds.
    /// <para>
    /// Counters resume from the claimed job's totals rather than zero, so a
    /// resumed run's numbers continue instead of restarting.
    /// </para>
    /// </summary>
    private sealed class RunState
    {
        public RunState(
            ClaimedJobDto job,
            string workerId,
            SearchRequestPayload payload,
            Guid ownerUserId,
            ScraperOptions options)
        {
            JobId = job.Id;
            WorkerId = workerId;
            Payload = payload;
            OwnerUserId = ownerUserId;
            Options = options;
            CompletedTaskKeys = job.CompletedTaskKeys;

            Found = job.Found;
            Saved = job.Saved;
            Duplicates = job.Duplicates;
            Enriched = job.Enriched;
            EnrichmentFailed = job.EnrichmentFailed;
            EmailsVerified = job.EmailsVerified;
            WhatsAppReachable = job.WhatsAppReachable;
            Skipped = job.Skipped;
            Failed = job.Failed;

            ProviderContext = new ProviderContext(
                options,
                new RateLimiter(options.RateLimitPerMinute),
                string.IsNullOrWhiteSpace(options.GoogleApiKey) ? null : options.GoogleApiKey);
        }

        public Guid JobId { get; }
        public string WorkerId { get; }
        public SearchRequestPayload Payload { get; }
        public Guid OwnerUserId { get; }
        public ScraperOptions Options { get; }
        public ProviderContext ProviderContext { get; }
        public IReadOnlyList<string> CompletedTaskKeys { get; }

        public List<IngestLead> Pending { get; } = [];
        public string CurrentTask { get; set; } = "Preparing";
        public int TotalTasks { get; set; }
        public bool LeaseLost { get; set; }

        // Run-local, not persisted: they describe how this attempt went, which is
        // what decides the final status. The `Failed` counter below is the
        // user-facing tally and also absorbs save failures, so it cannot answer
        // "did every task fail".
        public int TasksAttempted { get; set; }
        public int TasksFailed { get; set; }
        public string? LastTaskError { get; set; }

        public int Found;
        public int Saved;
        public int Duplicates;
        public int Enriched;
        public int EnrichmentFailed;
        public int EmailsVerified;
        public int WhatsAppReachable;
        public int Skipped;
        public int Failed;

        private readonly List<LiveResult> _recent = [];

        public void PushRecent(IngestLead lead)
        {
            _recent.Insert(0, new LiveResult
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = lead.Name,
                Category = lead.Category,
                City = lead.City,
                Country = lead.Country,
                Email = lead.Email,
                Phone = lead.Phone,
                Website = lead.Website,
                Status = lead.Status,
                EmailStatus = lead.EmailStatus,
                WhatsAppStatus = lead.WhatsAppStatus,
            });

            if (_recent.Count > MaxRecentResults) _recent.RemoveRange(MaxRecentResults, _recent.Count - MaxRecentResults);
        }

        public JobHeartbeatRequest BuildHeartbeat(string? completedTaskKey) => new()
        {
            WorkerId = WorkerId,
            LeaseSeconds = Options.LeaseSeconds,
            CurrentTask = CurrentTask.Length > 256 ? CurrentTask[..256] : CurrentTask,
            CompletedTaskKey = completedTaskKey,
            TotalTasks = TotalTasks,
            Found = Found,
            Saved = Saved,
            Duplicates = Duplicates,
            Enriched = Enriched,
            EnrichmentFailed = EnrichmentFailed,
            EmailsVerified = EmailsVerified,
            WhatsAppReachable = WhatsAppReachable,
            Skipped = Skipped,
            Failed = Failed,
            RecentResultsJson = JsonSerializer.Serialize(_recent, LiveResultJson),
        };
    }

    /// <summary>A result row for the live progress panel.</summary>
    private sealed class LiveResult
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string City { get; set; } = string.Empty;
        public string Country { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string Website { get; set; } = string.Empty;

        [JsonConverter(typeof(JsonStringEnumConverter))]
        public BusinessStatus Status { get; set; }

        [JsonConverter(typeof(JsonStringEnumConverter))]
        public EmailStatus EmailStatus { get; set; }

        [JsonConverter(typeof(JsonStringEnumConverter))]
        public WhatsAppStatus WhatsAppStatus { get; set; }

        /// <summary>Always false here — duplicates are detected at save time.</summary>
        public bool Duplicate => false;
    }
}
