using LeadMine.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LeadMine.Infrastructure.Scraping.Verification;

/// <summary>
/// Continuously validates lead email addresses: normalize, syntax, DNS, MX,
/// disposable-domain, role-account, via <see cref="EmailValidationPipeline"/>.
/// <para>
/// Wakes up every <see cref="EmailValidationOptions.PollSeconds"/> (1s by
/// default) and claims a small batch — that cadence is safe specifically
/// because this pass is pure DNS, the same kind of query every browser makes
/// constantly; there is no external party to overwhelm. It never runs the
/// SMTP probe, even when <see cref="EmailValidationOptions.EnableSmtpProbe"/>
/// is on — that step talks to a real mail server and needs a much slower,
/// rate-limited cadence, which is <see cref="EmailSmtpProbeWorkerService"/>'s
/// job on its own schedule.
/// </para>
/// <para>
/// No distributed lease the way <c>SearchJob</c> has one: this app runs as a
/// single process, and re-validating the same address twice in a race is
/// harmless (it just recomputes the same result), unlike re-running a scrape.
/// </para>
/// </summary>
public sealed class EmailValidationWorkerService(
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<EmailValidationOptions> optionsMonitor,
    ILogger<EmailValidationWorkerService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!optionsMonitor.CurrentValue.Enabled)
        {
            logger.LogInformation("Email validation worker disabled (EmailValidation:Enabled = false)");
            return;
        }

        logger.LogInformation("Email validation worker started");

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(8), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var options = optionsMonitor.CurrentValue;
            var delay = TimeSpan.FromSeconds(options.PollSeconds);

            if (!options.Enabled)
            {
                delay = TimeSpan.FromSeconds(30);
            }
            else
            {
                try
                {
                    await ValidateBatchAsync(options, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    // One bad tick (a DB blip, a DNS resolver hiccup) must never
                    // stop the whole continuous pass.
                    logger.LogError(ex, "Email validation tick failed; will retry");
                }
            }

            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        logger.LogInformation("Email validation worker stopped");
    }

    private async Task ValidateBatchAsync(EmailValidationOptions options, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LeadMineDbContext>();
        var pipeline = scope.ServiceProvider.GetRequiredService<EmailValidationPipeline>();

        var cutoff = DateTimeOffset.UtcNow.AddDays(-options.RevalidateAfterDays);

        var batch = await db.Businesses
            .Where(b => b.Email != "" && (b.EmailValidatedAt == null || b.EmailValidatedAt < cutoff))
            .OrderBy(b => b.EmailValidatedAt ?? DateTimeOffset.MinValue)
            .Take(options.BatchSize)
            .ToListAsync(ct);

        if (batch.Count == 0) return;

        // The pipeline itself (DNS, and optionally SMTP) is pure network I/O —
        // no DbContext access — so it's safe and worthwhile to run the batch
        // concurrently: one slow or unresponsive domain must not stall the
        // other nine behind it, which a plain sequential loop measured doing
        // (a batch of 10 took over 30s against real-world domains). DbContext
        // itself is not thread-safe, so every entity mutation and the single
        // SaveChangesAsync call stay strictly single-threaded, after the
        // parallel work below has finished.
        var outcomes = new EmailValidationOutcome?[batch.Count];

        await Parallel.ForEachAsync(
            Enumerable.Range(0, batch.Count),
            new ParallelOptions { MaxDegreeOfParallelism = options.BatchSize, CancellationToken = ct },
            async (i, token) =>
            {
                try
                {
                    // Never probes here, regardless of EnableSmtpProbe — this
                    // pass runs every second across the whole lead table, and
                    // an RCPT TO probe at that cadence is exactly the kind of
                    // volume EmailVerifier's own remarks warn against.
                    // EmailSmtpProbeWorkerService owns that step, on its own
                    // slower, rate-limited cadence.
                    outcomes[i] = await pipeline.ValidateAsync(batch[i].Email, token, forceSmtpProbe: false);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "Validation failed for one lead's email; leaving it for the next pass");
                }
            });

        for (var i = 0; i < batch.Count; i++)
        {
            var outcome = outcomes[i];
            if (outcome is null) continue;

            var business = batch[i];
            business.EmailStatus = outcome.Status;
            business.EmailConfidence = outcome.Confidence;
            business.EmailValidatedAt = DateTimeOffset.UtcNow;
            business.EmailIsDisposable = outcome.IsDisposable;
            business.EmailIsRoleAccount = outcome.IsRoleAccount;
            business.EmailIsCatchAll = outcome.IsCatchAll;
            business.EmailValidationDetailsJson = outcome.DetailsJson;
            // Always false from this pass (forceSmtpProbe: false above), but
            // written the same way EmailSmtpProbeWorkerService does — never
            // clear a real probe timestamp this pass didn't set.
            if (outcome.ProbeAttempted) business.EmailSmtpProbedAt = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(ct);
    }
}
