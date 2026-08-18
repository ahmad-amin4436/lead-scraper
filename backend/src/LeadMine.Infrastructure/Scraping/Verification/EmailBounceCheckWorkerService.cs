using LeadMine.Domain.Entities;
using LeadMine.Domain.Enums;
using LeadMine.Infrastructure.Persistence;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Net.Smtp;
using MailKit.Search;
using MailKit.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using MimeKit.Utils;

namespace LeadMine.Infrastructure.Scraping.Verification;

/// <summary>
/// Step 11 of the validation pipeline: sends a one-line probe email to
/// already-high-confidence leads from a real mailbox, then separately scans
/// that same mailbox over IMAP for delivery-failure notices to confirm or
/// invalidate the address.
/// <para>
/// Off by default (<see cref="EmailValidationOptions.EnableBounceCheck"/>) and
/// capped per day/hour with real margin under Gmail's own consumer sending
/// limit (~500/day): this pass is functionally a small outbound campaign from
/// a real account, and an account that trips abuse detection stops working
/// entirely — a much worse outcome than sending fewer probes. Deliberately a
/// separate, slower-cadence service from <see cref="EmailValidationWorkerService"/>,
/// which never sends anything and is safe to run every second.
/// </para>
/// </summary>
public sealed class EmailBounceCheckWorkerService(
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<EmailValidationOptions> optionsMonitor,
    ILogger<EmailBounceCheckWorkerService> logger) : BackgroundService
{
    /// <summary>
    /// Ceiling on sends in one tick, independent of the hourly/daily budget —
    /// stops a fresh restart with a full day's headroom from bursting the
    /// whole budget in a single pass.
    /// </summary>
    private const int MaxSendsPerTick = 5;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var options = optionsMonitor.CurrentValue;

        if (!options.EnableBounceCheck)
        {
            logger.LogInformation("Email bounce-check worker disabled (EmailValidation:EnableBounceCheck = false)");
            return;
        }

        if (!options.BounceCheckIsConfigured)
        {
            logger.LogWarning(
                "Email bounce-check worker enabled but no mailbox is configured (EmailValidation:BounceCheckUsername/BounceCheckAppPassword) — staying idle");
        }

        logger.LogInformation("Email bounce-check worker started");

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var current = optionsMonitor.CurrentValue;

            if (current.EnableBounceCheck && current.BounceCheckIsConfigured)
            {
                try
                {
                    await RunTickAsync(current, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Bounce-check tick failed; will retry");
                }
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(current.BounceCheckPollSeconds), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        logger.LogInformation("Email bounce-check worker stopped");
    }

    private async Task RunTickAsync(EmailValidationOptions options, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LeadMineDbContext>();

        await TimeOutStaleChecksAsync(db, options, ct);
        await SendProbesAsync(db, options, ct);
        await ScanForBouncesAsync(db, options, ct);
    }

    // --- 1. Resolve anything that has waited long enough without a bounce ---

    private static async Task TimeOutStaleChecksAsync(LeadMineDbContext db, EmailValidationOptions options, CancellationToken ct)
    {
        var cutoff = DateTimeOffset.UtcNow.AddHours(-options.BounceCheckWaitHours);

        var timedOut = await db.EmailBounceChecks
            .Where(c => c.Status == EmailBounceCheckStatus.Pending && c.SentAt < cutoff)
            .ToListAsync(ct);

        if (timedOut.Count == 0) return;

        var businessIds = timedOut.Select(c => c.BusinessId).ToHashSet();
        var businesses = await db.Businesses.Where(b => businessIds.Contains(b.Id)).ToListAsync(ct);
        var byId = businesses.ToDictionary(b => b.Id);

        foreach (var check in timedOut)
        {
            check.Status = EmailBounceCheckStatus.Confirmed;
            check.ResolvedAt = DateTimeOffset.UtcNow;

            // No bounce arrived in the window — a real send that nobody's mail
            // server rejected is the strongest signal available.
            if (byId.TryGetValue(check.BusinessId, out var business) &&
                string.Equals(business.Email, check.Email, StringComparison.OrdinalIgnoreCase))
            {
                business.EmailStatus = EmailStatus.Valid;
                business.EmailConfidence = 99;
            }
        }

        await db.SaveChangesAsync(ct);
    }

    // --- 2. Send a small batch of probes, inside the day/hour budget --------

    private async Task SendProbesAsync(LeadMineDbContext db, EmailValidationOptions options, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var dayStart = new DateTimeOffset(now.Date, TimeSpan.Zero);
        var hourStart = now.AddHours(-1);

        var sentToday = await db.EmailBounceChecks.CountAsync(c => c.SentAt >= dayStart, ct);
        var sentThisHour = await db.EmailBounceChecks.CountAsync(c => c.SentAt >= hourStart, ct);

        var remaining = Math.Min(options.BounceCheckMaxPerDay - sentToday, options.BounceCheckMaxPerHour - sentThisHour);
        remaining = Math.Min(remaining, MaxSendsPerTick);

        if (remaining <= 0) return;

        // Already-checked addresses (any status) never get a second probe —
        // this pass is one-shot per address, not a retry loop.
        var alreadyChecked = await db.EmailBounceChecks.Select(c => c.Email).Distinct().ToListAsync(ct);
        var alreadyCheckedSet = new HashSet<string>(alreadyChecked, StringComparer.OrdinalIgnoreCase);

        var candidates = await db.Businesses
            .Where(b => b.Email != ""
                && b.EmailStatus == EmailStatus.Valid
                && b.EmailConfidence != null && b.EmailConfidence >= options.BounceCheckMinConfidence
                && b.EmailIsDisposable != true)
            .OrderByDescending(b => b.EmailConfidence)
            .Take(remaining * 4) // slack for the in-memory alreadyChecked filter below
            .ToListAsync(ct);

        var toSend = candidates
            .Where(b => !alreadyCheckedSet.Contains(b.Email))
            .DistinctBy(b => b.Email, StringComparer.OrdinalIgnoreCase)
            .Take(remaining)
            .ToList();

        if (toSend.Count == 0) return;

        using var smtp = new SmtpClient();
        try
        {
            await smtp.ConnectAsync(options.BounceCheckSmtpHost, options.BounceCheckSmtpPort, SecureSocketOptions.StartTls, ct);
            await smtp.AuthenticateAsync(options.BounceCheckUsername, options.BounceCheckAppPassword, ct);

            foreach (var business in toSend)
            {
                var messageId = MimeUtils.GenerateMessageId();

                var message = new MimeMessage();
                message.MessageId = messageId;
                message.From.Add(MailboxAddress.Parse(options.BounceCheckUsername));
                message.To.Add(MailboxAddress.Parse(business.Email));
                message.Subject = "Contact information check";
                message.Body = new TextPart("plain")
                {
                    Text = "This is an automated message to confirm this email address can receive mail. "
                         + "No action is needed, and there is nothing to click or reply to.",
                };

                var check = new EmailBounceCheck
                {
                    BusinessId = business.Id,
                    Email = business.Email,
                    MessageId = messageId,
                    SentAt = DateTimeOffset.UtcNow,
                    Status = EmailBounceCheckStatus.Pending,
                };

                try
                {
                    await smtp.SendAsync(message, ct);
                    db.EmailBounceChecks.Add(check);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Bounce-check probe send failed for one lead; not counted against the budget");
                    check.Status = EmailBounceCheckStatus.SendFailed;
                    check.ResolvedAt = DateTimeOffset.UtcNow;
                    db.EmailBounceChecks.Add(check);
                }
            }

            await smtp.DisconnectAsync(true, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not connect to the bounce-check mailbox to send probes");
        }

        await db.SaveChangesAsync(ct);
    }

    // --- 3. Scan the mailbox for delivery-failure notices --------------------

    private async Task ScanForBouncesAsync(LeadMineDbContext db, EmailValidationOptions options, CancellationToken ct)
    {
        var pending = await db.EmailBounceChecks
            .Where(c => c.Status == EmailBounceCheckStatus.Pending)
            .ToListAsync(ct);

        if (pending.Count == 0) return;

        var byEmail = pending
            .GroupBy(c => c.Email, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        using var imap = new ImapClient();
        List<UniqueId> uids;
        IMailFolder inbox;

        try
        {
            await imap.ConnectAsync(options.BounceCheckImapHost, options.BounceCheckImapPort, SecureSocketOptions.SslOnConnect, ct);
            await imap.AuthenticateAsync(options.BounceCheckUsername, options.BounceCheckAppPassword, ct);

            inbox = imap.Inbox;
            await inbox.OpenAsync(FolderAccess.ReadOnly, ct);

            var since = DateTime.UtcNow.AddHours(-(options.BounceCheckWaitHours + 24));
            uids = (await inbox.SearchAsync(SearchQuery.DeliveredAfter(since), ct)).ToList();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not connect to the bounce-check mailbox to scan for bounces");
            return;
        }

        var resolvedBusinessIds = new List<(Guid BusinessId, string Reason)>();

        foreach (var uid in uids)
        {
            ct.ThrowIfCancellationRequested();

            MimeMessage message;
            try
            {
                message = await inbox.GetMessageAsync(uid, ct);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Could not read one message while scanning for bounces; skipping it");
                continue;
            }

            var failedRecipient = TryExtractStructuredBounce(message, out var reason);

            // No structured DSN recipient — for a message that still looks
            // like a bounce, fall back to searching its text for a pending
            // address, since an unstructured bounce rarely repeats the
            // address in a header we can parse reliably.
            if (failedRecipient is null && LooksLikeBounce(message, out var heuristicReason))
            {
                failedRecipient = FindPendingAddressInText(message, byEmail.Keys);
                reason ??= heuristicReason;
            }

            if (failedRecipient is null || !byEmail.TryGetValue(failedRecipient, out var check)) continue;

            check.Status = EmailBounceCheckStatus.Bounced;
            check.ResolvedAt = DateTimeOffset.UtcNow;
            check.BounceReason = reason;
            resolvedBusinessIds.Add((check.BusinessId, reason ?? "Delivery failure notice received"));
        }

        try
        {
            await imap.DisconnectAsync(true, ct);
        }
        catch
        {
            // Best-effort only — the scan already has what it needs.
        }

        if (resolvedBusinessIds.Count == 0) return;

        var ids = resolvedBusinessIds.Select(r => r.BusinessId).ToHashSet();
        var businesses = await db.Businesses.Where(b => ids.Contains(b.Id)).ToListAsync(ct);

        foreach (var (businessId, reason) in resolvedBusinessIds)
        {
            var business = businesses.FirstOrDefault(b => b.Id == businessId);
            if (business is null) continue;

            business.EmailStatus = EmailStatus.Invalid;
            business.EmailConfidence = 0;
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// The RFC 3464 structured format — what Gmail and most major providers
    /// send. Reliable when present: the recipient comes straight from a
    /// dedicated field, not a text guess.
    /// </summary>
    private static string? TryExtractStructuredBounce(MimeMessage message, out string? reason)
    {
        reason = null;

        foreach (var part in message.BodyParts)
        {
            if (part is not MessageDeliveryStatus status) continue;

            // StatusGroups[0] is the per-message fields; recipient groups follow.
            foreach (var group in status.StatusGroups.Skip(1))
            {
                var action = group["Action"];
                if (action is null || !action.Contains("fail", StringComparison.OrdinalIgnoreCase)) continue;

                var finalRecipient = group["Final-Recipient"] ?? group["Original-Recipient"];
                if (finalRecipient is null) continue;

                var cleaned = CleanAddress(finalRecipient);
                if (cleaned is null) continue;

                reason = group["Diagnostic-Code"] ?? group["Status"];
                return cleaned;
            }
        }

        return null;
    }

    /// <summary>From/subject heuristics for servers that don't send a structured DSN.</summary>
    private static bool LooksLikeBounce(MimeMessage message, out string? reason)
    {
        var from = message.From.Mailboxes.FirstOrDefault()?.Address ?? string.Empty;
        var subject = message.Subject ?? string.Empty;
        reason = subject;

        return
            from.Contains("mailer-daemon", StringComparison.OrdinalIgnoreCase) ||
            from.Contains("postmaster", StringComparison.OrdinalIgnoreCase) ||
            subject.Contains("undeliver", StringComparison.OrdinalIgnoreCase) ||
            subject.Contains("delivery status notification", StringComparison.OrdinalIgnoreCase) ||
            subject.Contains("failure notice", StringComparison.OrdinalIgnoreCase) ||
            subject.Contains("returned to sender", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Scans a bounce-looking message's plain-text body for any address still
    /// awaiting a result. Last resort, used only when the message carries no
    /// structured recipient field at all.
    /// </summary>
    private static string? FindPendingAddressInText(MimeMessage message, IEnumerable<string> pendingAddresses)
    {
        string text;
        try
        {
            text = message.TextBody ?? message.HtmlBody ?? string.Empty;
        }
        catch
        {
            return null;
        }

        if (text.Length == 0) return null;

        foreach (var address in pendingAddresses)
        {
            if (text.Contains(address, StringComparison.OrdinalIgnoreCase)) return address;
        }

        return null;
    }

    private static string? CleanAddress(string raw)
    {
        var value = raw.Trim();
        var prefixIndex = value.IndexOf(';');
        if (prefixIndex >= 0) value = value[(prefixIndex + 1)..].Trim();

        try
        {
            return MailboxAddress.Parse(value).Address;
        }
        catch
        {
            return value.Length > 0 ? value : null;
        }
    }
}
