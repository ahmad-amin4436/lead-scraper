using LeadMine.Application.DTOs;
using LeadMine.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LeadMine.Infrastructure.Scraping;

/// <summary>
/// Runs search jobs inside the API process.
/// <para>
/// The deployment target is a .NET application host with no way to run a second
/// service, so the scraper lives here rather than in a separate worker. It claims
/// jobs from the same SQL queue a standalone worker would, which means the
/// durability story is unchanged: a claim is leased, progress is checkpointed per
/// task, and an app-pool recycle mid-run leaves a job that the reaper requeues
/// and the next instance resumes from where it stopped.
/// </para>
/// <para>
/// It deliberately does not run the whole queue at once. This process also serves
/// user requests, so <see cref="ScraperOptions.Concurrency"/> caps how much of it
/// the scraper may occupy.
/// </para>
/// </summary>
public sealed class ScraperWorkerService(
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<ScraperOptions> optionsMonitor,
    ILogger<ScraperWorkerService> logger) : BackgroundService
{
    /// <summary>
    /// Identifies this instance's claims. The machine name alone is not enough:
    /// two processes on one host, or a restarted one, must not look like the same
    /// lease holder — otherwise a straggler could complete a job that has already
    /// been handed to its replacement.
    /// </summary>
    private readonly string _workerId =
        $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid().ToString("N")[..8]}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var options = optionsMonitor.CurrentValue;

        if (!options.WorkerEnabled)
        {
            logger.LogInformation("Scraper worker is disabled by configuration");
            return;
        }

        logger.LogInformation(
            "Scraper worker {WorkerId} started ({Concurrency} concurrent run(s))",
            _workerId, options.Concurrency);

        // Let the app finish starting — and migrations finish running — before
        // the first claim.
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(8), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        var slots = Enumerable
            .Range(0, Math.Max(1, options.Concurrency))
            .Select(slot => PollLoopAsync(slot, stoppingToken));

        await Task.WhenAll(slots);

        logger.LogInformation("Scraper worker {WorkerId} stopped", _workerId);
    }

    /// <summary>One claim-and-run loop. Several run side by side when configured.</summary>
    private async Task PollLoopAsync(int slot, CancellationToken stoppingToken)
    {
        // Each slot needs its own lease identity, or the second claim would look
        // like the first slot renewing and the two would fight over the job.
        var slotWorkerId = $"{_workerId}#{slot}";

        while (!stoppingToken.IsCancellationRequested)
        {
            var options = optionsMonitor.CurrentValue;
            ClaimedJobDto? job = null;

            try
            {
                job = await ClaimAsync(slotWorkerId, options, stoppingToken);

                if (job is not null)
                {
                    logger.LogInformation("Worker {WorkerId} running job {JobId}", slotWorkerId, job.Id);

                    await using var scope = scopeFactory.CreateAsyncScope();
                    var runner = scope.ServiceProvider.GetRequiredService<SearchRunner>();

                    await runner.RunAsync(job, slotWorkerId, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Never let one bad job kill the loop: that would silently stop
                // every future search on this instance.
                logger.LogError(ex, "Worker {WorkerId} failed while handling a job", slotWorkerId);
            }

            // Straight back round after a job — the queue may hold more.
            if (job is not null) continue;

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(options.IdlePollSeconds), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task<ClaimedJobDto?> ClaimAsync(
        string workerId,
        ScraperOptions options,
        CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var jobs = scope.ServiceProvider.GetRequiredService<ISearchJobService>();

        return await jobs.ClaimNextAsync(new ClaimJobRequest
        {
            WorkerId = workerId,
            LeaseSeconds = options.LeaseSeconds,
        }, ct);
    }
}
