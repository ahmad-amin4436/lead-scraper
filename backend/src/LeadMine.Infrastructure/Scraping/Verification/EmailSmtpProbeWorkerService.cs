using LeadMine.Domain.Enums;
using LeadMine.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LeadMine.Infrastructure.Scraping.Verification;

/// <summary>
/// The SMTP-probe tier (steps 7-8 of the validation pipeline: catch-all
/// detection and an RCPT TO check), on its own deliberately slower,
/// rate-limited cadence — separate from <see cref="EmailValidationWorkerService"/>'s
/// always-on, 1-per-second DNS pass, which never runs this step regardless of
/// <see cref="EmailValidationOptions.EnableSmtpProbe"/>.
/// <para>
/// Off by default. <see cref="EmailVerifier"/>'s own remarks explain why an
/// RCPT TO probe is normally skipped entirely (major providers refuse or lie
/// about it, catch-all domains defeat it, probing at volume risks the
/// sending IP being blacklisted); enabling this is a deliberate trade of some
/// of that risk for a stronger validity signal, which is why it runs at
/// <see cref="EmailValidationOptions.SmtpProbeBatchSize"/> leads every
/// <see cref="EmailValidationOptions.SmtpProbePollSeconds"/> rather than the
/// fast pass's cadence, and why <see cref="SmtpProbeDomainRateLimiter"/> caps
/// how often any one mail domain gets probed regardless of how many leads
/// happen to share it.
/// </para>
/// <para>
/// Targets leads that have already been through the DNS pass
/// (<c>EmailValidatedAt</c> set) but never through this one
/// (<c>EmailSmtpProbedAt</c> still null) — so a lead already probed is never
/// re-probed just because it comes up in a later sweep, and re-billed
/// against the per-domain budget for nothing.
/// </para>
/// </summary>
public sealed class EmailSmtpProbeWorkerService(
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<EmailValidationOptions> optionsMonitor,
    ILogger<EmailSmtpProbeWorkerService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!optionsMonitor.CurrentValue.EnableSmtpProbe)
        {
            logger.LogInformation("Email SMTP-probe worker disabled (EmailValidation:EnableSmtpProbe = false)");
        }

        logger.LogInformation("Email SMTP-probe worker started");

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(12), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var options = optionsMonitor.CurrentValue;
            var delay = TimeSpan.FromSeconds(options.SmtpProbePollSeconds);

            if (!options.EnableSmtpProbe)
            {
                delay = TimeSpan.FromSeconds(30);
            }
            else
            {
                try
                {
                    await ProbeBatchAsync(options, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "SMTP-probe tick failed; will retry");
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

        logger.LogInformation("Email SMTP-probe worker stopped");
    }

    private async Task ProbeBatchAsync(EmailValidationOptions options, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LeadMineDbContext>();
        var pipeline = scope.ServiceProvider.GetRequiredService<EmailValidationPipeline>();

        // Valid leads first — those are the ones a real send batch would
        // actually pick up (the compose page defaults to filtering on Valid).
        var batch = await db.Businesses
            .Where(b => b.Email != ""
                && b.EmailValidatedAt != null
                && b.EmailSmtpProbedAt == null
                && b.EmailIsDisposable != true)
            .OrderBy(b => b.EmailStatus == EmailStatus.Valid ? 0 : 1)
            .ThenBy(b => b.EmailValidatedAt)
            .Take(options.SmtpProbeBatchSize)
            .ToListAsync(ct);

        if (batch.Count == 0) return;

        var outcomes = new EmailValidationOutcome?[batch.Count];

        // Bounded by SmtpProbeBatchSize itself (already small — default 3),
        // so no separate concurrency cap is needed the way the fast pass
        // needs one against its larger BatchSize.
        await Parallel.ForEachAsync(
            Enumerable.Range(0, batch.Count),
            new ParallelOptions { MaxDegreeOfParallelism = batch.Count, CancellationToken = ct },
            async (i, token) =>
            {
                try
                {
                    outcomes[i] = await pipeline.ValidateAsync(batch[i].Email, token, forceSmtpProbe: true);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "SMTP probe failed for one lead; leaving it for the next pass");
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

            // Only mark it probed if the probe actually ran — a lead skipped
            // this tick because its domain hit the hourly cap stays eligible
            // to be picked up again on a later tick.
            if (outcome.ProbeAttempted) business.EmailSmtpProbedAt = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(ct);
    }
}
