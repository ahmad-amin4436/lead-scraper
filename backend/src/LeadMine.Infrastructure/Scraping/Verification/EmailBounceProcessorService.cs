using LeadMine.Domain.Entities;
using LeadMine.Domain.Enums;
using LeadMine.Infrastructure.Persistence;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace LeadMine.Infrastructure.Scraping.Verification;

/// <summary>
/// Post-send bounce processing: watches the real outbound mailbox for
/// delivery-failure notices against real campaign sends (<see cref="EmailLog"/>
/// rows with <see cref="EmailSendStatus.Sent"/>), and rolls a match up onto
/// both that log row and the originating <see cref="Business"/>.
/// <para>
/// Deliberately separate from the two other mailbox-touching passes:
/// <see cref="SmtpProbe"/>/<c>EmailSmtpProbeWorkerService</c> never send
/// anything (pre-send, a live RCPT TO check), and
/// <see cref="EmailBounceCheckWorkerService"/> sends its own synthetic
/// one-line probe purely to trigger a bounce. This service sends nothing at
/// all — it only reads whatever bounces a real send already produced, which
/// is why it can default closer to "safe" than a mailbox-sending feature
/// would, though it still stays opt-in like the rest of this file for
/// consistency (see <see cref="EmailValidationOptions.EnableBounceProcessing"/>).
/// </para>
/// <para>
/// Reuses <see cref="EmailValidationOptions"/>'s <c>BounceCheck*</c> IMAP
/// settings rather than a separate config block — in practice this is the
/// same Gmail account <c>Smtp</c> sends real campaigns from.
/// </para>
/// </summary>
public sealed class EmailBounceProcessorService(
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<EmailValidationOptions> optionsMonitor,
    ILogger<EmailBounceProcessorService> logger) : BackgroundService
{
    /// <summary>
    /// AppSettings key holding "&lt;UIDVALIDITY&gt;:&lt;highest UID processed&gt;"
    /// so a tick only looks at what's new. UIDVALIDITY is included because IMAP
    /// UIDs are only stable within one validity epoch for a folder — if it
    /// ever changes (a rebuilt mailbox), old UIDs are meaningless and the
    /// watermark must reset rather than silently skip or reprocess everything.
    /// </summary>
    private const string WatermarkKeyPrefix = "EmailBounceProcessor.LastUid:";

    /// <summary>How far back a Sent log is still considered a plausible match for an unstructured (no Final-Recipient field) bounce.</summary>
    private static readonly TimeSpan HeuristicCandidateWindow = TimeSpan.FromDays(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Email bounce processor started");

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var options = optionsMonitor.CurrentValue;

            if (options.EnableBounceProcessing && options.BounceCheckIsConfigured)
            {
                try
                {
                    await RunTickAsync(options, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Bounce-processor tick failed; will retry");
                }
            }

            var delay = TimeSpan.FromSeconds(options.EnableBounceProcessing ? options.BounceProcessorPollSeconds : 60);

            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        logger.LogInformation("Email bounce processor stopped");
    }

    private async Task RunTickAsync(EmailValidationOptions options, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LeadMineDbContext>();

        var watermarkKey = WatermarkKeyPrefix + options.BounceCheckUsername;
        var watermark = await db.AppSettings.FirstOrDefaultAsync(s => s.Key == watermarkKey, ct);

        using var imap = new ImapClient();
        IMailFolder inbox;
        IList<UniqueId> uids;

        try
        {
            await imap.ConnectAsync(options.BounceCheckImapHost, options.BounceCheckImapPort, SecureSocketOptions.SslOnConnect, ct);
            await imap.AuthenticateAsync(options.BounceCheckUsername, options.BounceCheckAppPassword, ct);

            inbox = imap.Inbox;
            await inbox.OpenAsync(FolderAccess.ReadOnly, ct);

            var (validUidValidity, lastUid) = ParseWatermark(watermark?.Value, inbox.UidValidity);

            if (validUidValidity && lastUid < uint.MaxValue)
            {
                var range = new UniqueIdRange(new UniqueId(inbox.UidValidity, lastUid + 1), UniqueId.MaxValue);
                uids = await inbox.SearchAsync(SearchQuery.Uids(range), ct);
            }
            else
            {
                var since = DateTime.UtcNow.AddDays(-options.BounceProcessorLookbackDays);
                uids = await inbox.SearchAsync(SearchQuery.DeliveredAfter(since), ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not connect to the outbound mailbox to scan for bounces");
            return;
        }

        if (uids.Count == 0)
        {
            try { await imap.DisconnectAsync(true, ct); } catch { /* best-effort */ }
            return;
        }

        var highestUid = uids.Max(u => u.Id);
        var matched = 0;

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

            try
            {
                if (await TryProcessBounceAsync(db, message, ct)) matched++;
            }
            catch (Exception ex)
            {
                // One malformed or unexpected message must never stop the
                // rest of the tick, and must never move the watermark past
                // messages that were never actually looked at.
                logger.LogWarning(ex, "Failed to process one candidate bounce message; skipping it");
            }
        }

        try { await imap.DisconnectAsync(true, ct); } catch { /* best-effort */ }

        var watermarkValue = $"{inbox.UidValidity}:{highestUid}";
        if (watermark is null)
        {
            db.AppSettings.Add(new AppSetting
            {
                Key = watermarkKey,
                Value = watermarkValue,
                Description = "Highest IMAP UID (with UIDVALIDITY) processed by EmailBounceProcessorService.",
            });
        }
        else
        {
            watermark.Value = watermarkValue;
        }

        await db.SaveChangesAsync(ct);

        if (matched > 0)
        {
            logger.LogInformation("Bounce processor matched {Count} delivery-failure notice(s) to real sends", matched);
        }
    }

    private static (bool Valid, uint LastUid) ParseWatermark(string? raw, uint currentUidValidity)
    {
        if (string.IsNullOrEmpty(raw)) return (false, 0);

        var parts = raw.Split(':', 2);
        if (parts.Length != 2 || !uint.TryParse(parts[0], out var storedValidity) || !uint.TryParse(parts[1], out var lastUid))
        {
            return (false, 0);
        }

        // A changed UIDVALIDITY means the server renumbered the mailbox — the
        // old UID means nothing now, so start over from the lookback window
        // rather than either skipping everything or misreading unrelated mail.
        return storedValidity == currentUidValidity ? (true, lastUid) : (false, 0);
    }

    /// <summary>
    /// Attempts to classify one message as a bounce and match it to the real
    /// send it belongs to. Returns false for anything that isn't a bounce, or
    /// that couldn't be matched to a known send — never throws for a
    /// malformed message.
    /// </summary>
    private async Task<bool> TryProcessBounceAsync(LeadMineDbContext db, MimeMessage message, CancellationToken ct)
    {
        var parsed = DsnMessageParser.TryParseStructured(message);

        if (parsed is null)
        {
            if (!DsnMessageParser.LooksLikeBounce(message, out var heuristicReason)) return false;

            var cutoff = DateTimeOffset.UtcNow - HeuristicCandidateWindow;
            var recentRecipients = await db.EmailLogs
                .Where(l => l.Status == EmailSendStatus.Sent && l.SentAt >= cutoff)
                .Select(l => l.ToEmail)
                .Distinct()
                .ToListAsync(ct);

            var address = DsnMessageParser.FindAddressInText(message, recentRecipients);
            if (address is null) return false;

            parsed = DsnMessageParser.BuildHeuristicBounce(address, heuristicReason, message);
        }

        if (parsed.FinalRecipient is null) return false;

        // Prefer an exact Message-Id match — the bounce's own copy of the
        // original message (a message/rfc822 attachment, which Gmail and most
        // providers include) pins this to one specific send even when the
        // same address was emailed more than once. Falls back to "the most
        // recent still-Sent log for this recipient" otherwise.
        var originalMessageId = DsnMessageParser.TryFindOriginalMessageId(message);

        EmailLog? log = null;
        if (originalMessageId is not null)
        {
            log = await db.EmailLogs.FirstOrDefaultAsync(l => l.MessageId == originalMessageId, ct);
            if (log is not null && log.Status != EmailSendStatus.Sent) log = null;
        }

        log ??= await db.EmailLogs
            .Where(l => l.ToEmail == parsed.FinalRecipient && l.Status == EmailSendStatus.Sent)
            .OrderByDescending(l => l.SentAt)
            .FirstOrDefaultAsync(ct);

        if (log is null)
        {
            logger.LogDebug("Bounce for {Recipient} matched no known real send; ignoring", parsed.FinalRecipient);
            return false;
        }

        var smtpClass = SmtpResponseClassifier.Classify(parsed.SmtpCode ?? 0, parsed.DsnCode);
        var bounceType = SmtpResponseClassifier.ToBounceType(smtpClass);
        var reason = parsed.Reason ?? "Delivery failure notice received";
        var now = DateTimeOffset.UtcNow;

        // Every classification decision, logged with exactly what drove it —
        // the point being that "why is this address suppressed" is always
        // answerable from the logs, not just the end state in the database.
        logger.LogInformation(
            "Bounce processed: {Recipient} -> {BounceType} (smtp={SmtpCode}, dsn={DsnCode}, structured={Structured}): {Reason}",
            parsed.FinalRecipient, bounceType, parsed.SmtpCode, parsed.DsnCode, parsed.IsStructured, reason);

        log.Status = EmailSendStatus.Bounced;
        log.BounceType = bounceType;
        log.BounceReason = reason;
        log.BounceSmtpCode = parsed.SmtpCode;
        log.BounceDsnCode = parsed.DsnCode;
        log.BounceDetectedAt = now;

        if (log.BusinessId is { } businessId)
        {
            var business = await db.Businesses.FirstOrDefaultAsync(b => b.Id == businessId, ct);
            if (business is not null)
            {
                ApplyBounceToLead(business, bounceType, parsed.SmtpCode, parsed.DsnCode, reason, now);
            }
        }

        return true;
    }

    /// <summary>
    /// The suppress/retry/review decision each bounce type drives — see
    /// <see cref="EmailBounceStatus"/>'s own remarks for what each outcome
    /// means. A hard bounce is the only one that downgrades
    /// <see cref="Business.EmailStatus"/> itself: a soft bounce or policy
    /// rejection is explicitly not evidence the address doesn't exist.
    /// </summary>
    internal static void ApplyBounceToLead(
        Business business, EmailBounceType bounceType, int? smtpCode, string? dsnCode, string reason, DateTimeOffset now)
    {
        business.EmailBounceType = bounceType;
        business.EmailBounceReason = reason;
        business.EmailLastBounceAt = now;
        business.EmailSmtpCode = smtpCode;
        business.EmailDsnCode = dsnCode;

        switch (bounceType)
        {
            case EmailBounceType.HardBounce:
                business.EmailBounceStatus = EmailBounceStatus.Suppressed;
                business.EmailStatus = EmailStatus.Invalid;
                business.EmailConfidence = 0;
                break;

            case EmailBounceType.SoftBounce:
                business.EmailBounceStatus = EmailBounceStatus.RetryScheduled;
                business.EmailRetryCount++;
                break;

            default: // PolicyRejection, Unknown — needs a human look, no automatic verdict either way.
                business.EmailBounceStatus = EmailBounceStatus.UnderReview;
                break;
        }
    }
}
