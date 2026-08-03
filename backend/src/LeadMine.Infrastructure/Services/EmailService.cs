using LeadMine.Application.Authorization;
using LeadMine.Application.Common;
using LeadMine.Application.DTOs;
using LeadMine.Application.Interfaces;
using LeadMine.Domain.Entities;
using LeadMine.Infrastructure.Email;
using LeadMine.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LeadMine.Infrastructure.Services;

public sealed class EmailService(
    LeadMineDbContext db,
    IEmailSender sender,
    ICurrentUser currentUser,
    IAuditService audit,
    IOptions<SmtpOptions> smtpOptions,
    ILogger<EmailService> logger) : IEmailService
{
    private readonly SmtpOptions _smtp = smtpOptions.Value;

    public async Task<Result<SendEmailResultDto>> SendAsync(
        SendEmailRequest request,
        CancellationToken ct = default)
    {
        if (currentUser.UserId is not { } senderId)
        {
            return Result<SendEmailResultDto>.Unauthorized();
        }

        if (!sender.IsConfigured)
        {
            return Result<SendEmailResultDto>.Failure(
                "Email is not configured on the server. Set the Smtp settings first.");
        }

        if (request.BusinessIds.Count > _smtp.MaxRecipientsPerRequest)
        {
            return Result<SendEmailResultDto>.Failure(
                $"At most {_smtp.MaxRecipientsPerRequest} recipients per request.");
        }

        var template = await db.EmailTemplates
            .Include(t => t.Signature)
            .FirstOrDefaultAsync(t => t.Id == request.TemplateId, ct);

        if (template is null) return Result<SendEmailResultDto>.NotFound("Email preset not found.");

        // Retired presets stay for audit history but must not be sendable.
        if (!template.IsActive)
        {
            return Result<SendEmailResultDto>.Failure("That email preset is no longer active.");
        }

        var quota = await CheckDailyQuotaAsync(senderId, request.BusinessIds.Count, ct);
        if (!quota.Succeeded) return Result<SendEmailResultDto>.Failure(quota.Error!);

        // Only leads the caller may see: scoping here stops a user emailing
        // another user's pipeline by passing their lead ids.
        var leads = await ScopedLeads()
            .Where(b => request.BusinessIds.Contains(b.Id))
            .ToListAsync(ct);

        var signature = await ResolveSignatureAsync(request.SignatureId ?? template.SignatureId, senderId, ct);

        var senderName = currentUser.Email ?? _smtp.FromName;
        var senderEmail = currentUser.Email ?? _smtp.EffectiveFrom;

        var result = new SendEmailResultDto { Requested = request.BusinessIds.Count };

        foreach (var lead in leads)
        {
            ct.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(lead.Email))
            {
                result.Skipped++;
                result.Outcomes.Add(new SendEmailOutcome(lead.Id, string.Empty, "skipped", "No email address"));
                continue;
            }

            var subject = TemplateRenderer.Render(template.Subject, lead, senderName, senderEmail, htmlEncode: false);
            var bodyHtml = TemplateRenderer.AppendSignature(
                TemplateRenderer.Render(template.BodyHtml, lead, senderName, senderEmail, htmlEncode: true),
                signature?.BodyHtml);
            var bodyText = TemplateRenderer.AppendSignatureText(
                TemplateRenderer.Render(template.BodyText, lead, senderName, senderEmail, htmlEncode: false),
                signature?.BodyText);

            // Logged before the attempt, so a crash mid-send still leaves a
            // record rather than an email nobody can account for.
            var log = new EmailLog
            {
                SenderUserId = senderId,
                SenderEmail = senderEmail,
                SenderName = senderName,
                ToEmail = lead.Email,
                ToName = lead.Name,
                Cc = request.Cc,
                Bcc = request.Bcc,
                Subject = subject,
                BodyHtml = bodyHtml,
                TemplateId = template.Id,
                TemplateName = template.Name,
                BusinessId = lead.Id,
                Status = EmailSendStatus.Queued,
            };

            db.EmailLogs.Add(log);
            await db.SaveChangesAsync(ct);

            if (request.DryRun)
            {
                log.Status = EmailSendStatus.Sent;
                log.Error = "Dry run — not delivered";
                log.SentAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(ct);

                result.Sent++;
                result.Outcomes.Add(new SendEmailOutcome(lead.Id, lead.Email, "dry-run", null));
                continue;
            }

            var startedAt = DateTimeOffset.UtcNow;

            var outcome = await sender.SendAsync(
                new OutboundMessage(lead.Email, lead.Name, subject, bodyHtml, bodyText, request.Cc, request.Bcc),
                ct);

            log.DurationMs = (long)(DateTimeOffset.UtcNow - startedAt).TotalMilliseconds;

            if (outcome.Succeeded)
            {
                log.Status = EmailSendStatus.Sent;
                log.SentAt = DateTimeOffset.UtcNow;
                log.MessageId = outcome.MessageId;

                lead.LastContactedAt = log.SentAt;
                lead.TimesContacted++;

                result.Sent++;
                result.Outcomes.Add(new SendEmailOutcome(lead.Id, lead.Email, "sent", null));
            }
            else
            {
                log.Status = EmailSendStatus.Failed;
                log.Error = outcome.Error;

                result.Failed++;
                result.Outcomes.Add(new SendEmailOutcome(lead.Id, lead.Email, "failed", outcome.Error));
            }

            await db.SaveChangesAsync(ct);

            // Pace the batch: shared SMTP providers throttle or blacklist bursts.
            if (_smtp.DelayBetweenSendsMs > 0 && lead != leads[^1])
            {
                await Task.Delay(_smtp.DelayBetweenSendsMs, ct);
            }
        }

        // Ids that matched no visible lead.
        var missing = result.Requested - leads.Count;
        if (missing > 0) result.Skipped += missing;

        await audit.LogAsync("Email.Sent", nameof(EmailLog), template.Id.ToString(), true,
            new { template = template.Name, result.Sent, result.Failed, result.Skipped }, ct);

        return Result<SendEmailResultDto>.Success(result);
    }

    public async Task<Result<EmailPreviewDto>> PreviewAsync(
        EmailPreviewRequest request,
        CancellationToken ct = default)
    {
        var template = await db.EmailTemplates
            .Include(t => t.Signature)
            .FirstOrDefaultAsync(t => t.Id == request.TemplateId, ct);

        if (template is null) return Result<EmailPreviewDto>.NotFound("Email preset not found.");

        Business? lead = null;
        if (request.BusinessId.HasValue)
        {
            lead = await ScopedLeads().FirstOrDefaultAsync(b => b.Id == request.BusinessId.Value, ct);
        }

        var senderName = currentUser.Email ?? _smtp.FromName;
        var senderEmail = currentUser.Email ?? _smtp.EffectiveFrom;

        var signature = await ResolveSignatureAsync(
            request.SignatureId ?? template.SignatureId, currentUser.UserId, ct);

        return Result<EmailPreviewDto>.Success(new EmailPreviewDto
        {
            Subject = TemplateRenderer.Render(template.Subject, lead, senderName, senderEmail, false),
            BodyHtml = TemplateRenderer.AppendSignature(
                TemplateRenderer.Render(template.BodyHtml, lead, senderName, senderEmail, true),
                signature?.BodyHtml),
            ToEmail = lead?.Email ?? string.Empty,
        });
    }

    public async Task<PagedResult<EmailLogDto>> GetLogAsync(
        EmailLogQueryRequest request,
        CancellationToken ct = default)
    {
        var query = db.EmailLogs.AsNoTracking();

        // Without the all-logs right the caller only ever sees their own sends,
        // whatever SenderUserId they asked for.
        if (currentUser.HasPermission(Permissions.Email.ViewAllLogs))
        {
            if (request.SenderUserId.HasValue)
            {
                query = query.Where(l => l.SenderUserId == request.SenderUserId.Value);
            }
        }
        else
        {
            var me = currentUser.UserId;
            query = query.Where(l => l.SenderUserId == me);
        }

        if (request.TemplateId.HasValue) query = query.Where(l => l.TemplateId == request.TemplateId.Value);
        if (request.Status.HasValue) query = query.Where(l => l.Status == request.Status.Value);

        // Inclusive on both ends: "1st to 5th" should include everything on the 5th.
        if (request.From.HasValue) query = query.Where(l => l.CreatedAt >= request.From.Value);
        if (request.To.HasValue) query = query.Where(l => l.CreatedAt <= request.To.Value);

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim().ToLower();
            query = query.Where(l =>
                l.ToEmail.ToLower().Contains(term) ||
                l.ToName.ToLower().Contains(term) ||
                l.Subject.ToLower().Contains(term) ||
                l.SenderEmail.ToLower().Contains(term) ||
                l.TemplateName.ToLower().Contains(term));
        }

        var total = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(l => l.CreatedAt)
            .Skip((Math.Max(1, request.Page) - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(l => new EmailLogDto
            {
                Id = l.Id,
                SenderUserId = l.SenderUserId,
                SenderEmail = l.SenderEmail,
                SenderName = l.SenderName,
                ToEmail = l.ToEmail,
                ToName = l.ToName,
                Subject = l.Subject,
                BodyHtml = l.BodyHtml,
                TemplateName = l.TemplateName,
                BusinessId = l.BusinessId,
                Status = l.Status,
                Error = l.Error,
                CreatedAt = l.CreatedAt,
                SentAt = l.SentAt,
            })
            .ToListAsync(ct);

        return PagedResult<EmailLogDto>.Create(items, total, request.Page, request.PageSize);
    }

    public async Task<EmailStatsDto> GetStatsAsync(
        DateTimeOffset? from,
        DateTimeOffset? to,
        CancellationToken ct = default)
    {
        var query = db.EmailLogs.AsNoTracking();

        if (!currentUser.HasPermission(Permissions.Email.ViewAllLogs))
        {
            var me = currentUser.UserId;
            query = query.Where(l => l.SenderUserId == me);
        }

        if (from.HasValue) query = query.Where(l => l.CreatedAt >= from.Value);
        if (to.HasValue) query = query.Where(l => l.CreatedAt <= to.Value);

        var todayStart = DateTimeOffset.UtcNow.Date;
        var weekStart = todayStart.AddDays(-7);

        var totals = await query
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Sent = g.Count(l => l.Status == EmailSendStatus.Sent),
                Failed = g.Count(l => l.Status == EmailSendStatus.Failed),
                Today = g.Count(l => l.Status == EmailSendStatus.Sent && l.CreatedAt >= todayStart),
                Week = g.Count(l => l.Status == EmailSendStatus.Sent && l.CreatedAt >= weekStart),
            })
            .FirstOrDefaultAsync(ct);

        var bySender = await query
            .Where(l => l.Status == EmailSendStatus.Sent)
            .GroupBy(l => l.SenderEmail)
            .Select(g => new { Label = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .Take(10)
            .ToListAsync(ct);

        var byTemplate = await query
            .Where(l => l.Status == EmailSendStatus.Sent)
            .GroupBy(l => l.TemplateName)
            .Select(g => new { Label = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .Take(10)
            .ToListAsync(ct);

        return new EmailStatsDto
        {
            TotalSent = totals?.Sent ?? 0,
            TotalFailed = totals?.Failed ?? 0,
            SentToday = totals?.Today ?? 0,
            SentLast7Days = totals?.Week ?? 0,
            BySender = bySender.Select(x => new CountByLabel(x.Label, x.Count)).ToList(),
            ByTemplate = byTemplate.Select(x => new CountByLabel(x.Label, x.Count)).ToList(),
        };
    }

    public async Task<Result> TestConnectionAsync(CancellationToken ct = default)
    {
        var outcome = await sender.VerifyConnectionAsync(ct);

        await audit.LogAsync("Email.ConnectionTested", "Smtp", null, outcome.Succeeded,
            new { error = outcome.Error }, ct);

        return outcome.Succeeded
            ? Result.Success()
            : Result.Failure(outcome.Error ?? "Could not connect to the SMTP server.");
    }

    /// <summary>Leads visible to the caller — mirrors BusinessService scoping.</summary>
    private IQueryable<Business> ScopedLeads()
    {
        var query = db.Businesses.AsQueryable();

        if (currentUser.HasPermission(Permissions.Leads.ViewAll)) return query;

        var me = currentUser.UserId;
        return query.Where(b => b.OwnerUserId == me);
    }

    /// <summary>
    /// Picks the signature to append: the explicit one, else the sender's own
    /// default, else a shared organisation default.
    /// </summary>
    private async Task<EmailSignature?> ResolveSignatureAsync(
        Guid? signatureId,
        Guid? userId,
        CancellationToken ct)
    {
        if (signatureId.HasValue)
        {
            return await db.EmailSignatures
                .FirstOrDefaultAsync(s => s.Id == signatureId.Value && s.IsActive, ct);
        }

        return await db.EmailSignatures
            .Where(s => s.IsActive && (s.OwnerUserId == userId || s.OwnerUserId == null))
            // The user's own signature outranks the shared one.
            .OrderByDescending(s => s.OwnerUserId == userId)
            .ThenByDescending(s => s.IsDefault)
            .FirstOrDefaultAsync(ct);
    }

    private async Task<Result> CheckDailyQuotaAsync(Guid userId, int requested, CancellationToken ct)
    {
        if (_smtp.DailySendLimitPerUser <= 0) return Result.Success();

        var since = DateTimeOffset.UtcNow.Date;

        var sentToday = await db.EmailLogs.CountAsync(
            l => l.SenderUserId == userId && l.CreatedAt >= since && l.Status == EmailSendStatus.Sent, ct);

        if (sentToday + requested > _smtp.DailySendLimitPerUser)
        {
            logger.LogWarning("User {UserId} hit the daily send limit ({Limit})", userId, _smtp.DailySendLimitPerUser);

            return Result.Failure(
                $"Daily send limit reached ({sentToday}/{_smtp.DailySendLimitPerUser}). Try again tomorrow.");
        }

        return Result.Success();
    }
}
