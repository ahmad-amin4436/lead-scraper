using LeadMine.Application.Authorization;
using LeadMine.Application.Common;
using LeadMine.Application.DTOs;
using LeadMine.Application.Interfaces;
using LeadMine.Domain.Entities;
using LeadMine.Infrastructure.Email;
using LeadMine.Infrastructure.Persistence;
using LeadMine.Infrastructure.Scraping.Verification;
using Microsoft.EntityFrameworkCore;

namespace LeadMine.Infrastructure.Services;

public sealed class WhatsAppService(
    LeadMineDbContext db,
    ICurrentUser currentUser,
    IAuditService audit) : IWhatsAppService
{
    public async Task<Result<GenerateWhatsAppLinksResultDto>> GenerateLinksAsync(
        GenerateWhatsAppLinksRequest request,
        CancellationToken ct = default)
    {
        if (currentUser.UserId is null) return Result<GenerateWhatsAppLinksResultDto>.Unauthorized();

        var template = await db.WhatsAppTemplates.FirstOrDefaultAsync(t => t.Id == request.TemplateId, ct);
        if (template is null) return Result<GenerateWhatsAppLinksResultDto>.NotFound("WhatsApp preset not found.");

        // Retired presets stay for audit history but must not be usable.
        if (!template.IsActive)
        {
            return Result<GenerateWhatsAppLinksResultDto>.Failure("That WhatsApp preset is no longer active.");
        }

        // Only leads the caller may see: scoping here stops a user building
        // links for another user's pipeline by passing their lead ids.
        var leads = await ScopedLeads()
            .Where(b => request.BusinessIds.Contains(b.Id))
            .ToListAsync(ct);

        var senderName = currentUser.Email ?? string.Empty;
        var senderEmail = currentUser.Email ?? string.Empty;

        var result = new GenerateWhatsAppLinksResultDto { Requested = request.BusinessIds.Count };

        foreach (var lead in leads)
        {
            ct.ThrowIfCancellationRequested();

            var link = ResolveLink(lead);
            var message = TemplateRenderer.Render(template.Message, lead, senderName, senderEmail, htmlEncode: false);

            if (link is null)
            {
                result.Links.Add(new WhatsAppLinkDto
                {
                    BusinessId = lead.Id,
                    BusinessName = lead.Name,
                    Message = message,
                    SkipReason = "No usable phone number",
                });
                continue;
            }

            result.Links.Add(new WhatsAppLinkDto
            {
                BusinessId = lead.Id,
                BusinessName = lead.Name,
                Phone = link.Value.Phone,
                Message = message,
                Url = $"{link.Value.WaMeUrl}?text={Uri.EscapeDataString(message)}",
            });
        }

        await audit.LogAsync("WhatsApp.LinksGenerated", nameof(WhatsAppTemplate), template.Id.ToString(), true,
            new { template = template.Name, count = result.Links.Count(l => l.SkipReason is null) }, ct);

        return Result<GenerateWhatsAppLinksResultDto>.Success(result);
    }

    public async Task<Result<WhatsAppPreviewDto>> PreviewAsync(
        WhatsAppPreviewRequest request,
        CancellationToken ct = default)
    {
        var template = await db.WhatsAppTemplates.FirstOrDefaultAsync(t => t.Id == request.TemplateId, ct);
        if (template is null) return Result<WhatsAppPreviewDto>.NotFound("WhatsApp preset not found.");

        Business? lead = null;
        if (request.BusinessId.HasValue)
        {
            lead = await ScopedLeads().FirstOrDefaultAsync(b => b.Id == request.BusinessId.Value, ct);
        }

        var senderName = currentUser.Email ?? string.Empty;
        var senderEmail = currentUser.Email ?? string.Empty;

        return Result<WhatsAppPreviewDto>.Success(new WhatsAppPreviewDto
        {
            Message = TemplateRenderer.Render(template.Message, lead, senderName, senderEmail, htmlEncode: false),
            ToPhone = lead is null ? string.Empty : ResolveLink(lead)?.Phone ?? string.Empty,
        });
    }

    public async Task<Result> MarkContactedAsync(MarkWhatsAppContactedRequest request, CancellationToken ct = default)
    {
        if (currentUser.UserId is not { } senderId) return Result.Unauthorized();

        var lead = await ScopedLeads().FirstOrDefaultAsync(b => b.Id == request.BusinessId, ct);
        if (lead is null) return Result.NotFound("Lead not found.");

        string templateName = string.Empty;
        if (request.TemplateId.HasValue)
        {
            templateName = await db.WhatsAppTemplates
                .Where(t => t.Id == request.TemplateId.Value)
                .Select(t => t.Name)
                .FirstOrDefaultAsync(ct) ?? string.Empty;
        }

        var link = ResolveLink(lead);

        db.WhatsAppContactLogs.Add(new WhatsAppContactLog
        {
            SenderUserId = senderId,
            SenderEmail = currentUser.Email ?? string.Empty,
            BusinessId = lead.Id,
            ToName = lead.Name,
            ToPhone = link?.Phone ?? string.Empty,
            TemplateId = request.TemplateId,
            TemplateName = templateName,
            Message = request.Message ?? string.Empty,
        });

        lead.LastWhatsAppContactedAt = DateTimeOffset.UtcNow;
        lead.WhatsAppTimesContacted++;

        await db.SaveChangesAsync(ct);

        await audit.LogAsync("WhatsApp.Contacted", nameof(Business), lead.Id.ToString(), true,
            new { template = templateName }, ct);

        return Result.Success();
    }

    /// <summary>Leads visible to the caller — mirrors EmailService scoping.</summary>
    private IQueryable<Business> ScopedLeads()
    {
        var query = db.Businesses.AsQueryable();

        if (currentUser.HasPermission(Permissions.Leads.ViewAll)) return query;

        var me = currentUser.UserId;
        return query.Where(b => b.OwnerUserId == me);
    }

    /// <summary>
    /// A confirmed/published link on the record beats a guess from the phone
    /// number, exactly as <see cref="WhatsAppClassifier"/> already prioritises
    /// them during verification.
    /// </summary>
    private static (string WaMeUrl, string Phone)? ResolveLink(Business lead)
    {
        if (!string.IsNullOrWhiteSpace(lead.WhatsApp))
        {
            // Business.WhatsApp is itself a wa.me URL (see WhatsAppClassifier);
            // fall back to it for the display number only if Phone is blank.
            var displayPhone = string.IsNullOrWhiteSpace(lead.Phone)
                ? lead.WhatsApp[(lead.WhatsApp.LastIndexOf('/') + 1)..]
                : lead.Phone;

            return (lead.WhatsApp, displayPhone);
        }

        var assessment = WhatsAppClassifier.Assess(lead.Phone, lead.Country, null);
        return string.IsNullOrWhiteSpace(assessment.Link) ? null : (assessment.Link, lead.Phone);
    }
}
