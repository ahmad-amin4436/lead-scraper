using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LeadMine.Infrastructure.Services;

/// <summary>
/// Continuously frees search jobs whose worker died.
/// <para>
/// This is what turns a crashed or killed worker into a temporary pause rather
/// than a stuck run. A job whose lease lapses goes back to `Queued` and is picked
/// up by any available worker, which resumes from the last checkpoint. No human
/// has to notice or intervene.
/// </para>
/// <para>
/// Runs inside the API because the API is the process guaranteed to be up; if it
/// lived in the worker, the failure it exists to recover from would also take
/// out the recovery.
/// </para>
/// </summary>
public sealed class StaleJobReaperService(
    IServiceScopeFactory scopeFactory,
    ILogger<StaleJobReaperService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Stale-job reaper started (every {Seconds}s)", Interval.TotalSeconds);

        // Let the app finish starting before the first sweep.
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        using var timer = new PeriodicTimer(Interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var jobs = scope.ServiceProvider.GetRequiredService<ISearchJobService>();

                await jobs.ReleaseExpiredLeasesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Never let one bad sweep kill the reaper — that would silently
                // disable crash recovery for the whole deployment.
                logger.LogError(ex, "Stale-job sweep failed; will retry");
            }

            try
            {
                await timer.WaitForNextTickAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        logger.LogInformation("Stale-job reaper stopped");
    }
}
