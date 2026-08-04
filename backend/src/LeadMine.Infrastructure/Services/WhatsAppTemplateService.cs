using LeadMine.Application.Common;
using LeadMine.Application.DTOs;
using LeadMine.Application.Interfaces;
using LeadMine.Domain.Entities;
using LeadMine.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LeadMine.Infrastructure.Services;

public sealed class WhatsAppTemplateService(
    LeadMineDbContext db,
    IAuditService audit) : IWhatsAppTemplateService
{
    public async Task<IReadOnlyList<WhatsAppTemplateDto>> GetTemplatesAsync(
        bool activeOnly,
        CancellationToken ct = default)
    {
        var query = db.WhatsAppTemplates.AsNoTracking().AsQueryable();
        if (activeOnly) query = query.Where(t => t.IsActive);

        // Usage counts in one grouped query rather than per template.
        var counts = await db.WhatsAppContactLogs
            .Where(l => l.TemplateId != null)
            .GroupBy(l => l.TemplateId!.Value)
            .Select(g => new { TemplateId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TemplateId, x => x.Count, ct);

        var templates = await query.OrderBy(t => t.Name).ToListAsync(ct);

        return templates.Select(t => Map(t, counts.GetValueOrDefault(t.Id))).ToList();
    }

    public async Task<Result<WhatsAppTemplateDto>> GetTemplateAsync(Guid id, CancellationToken ct = default)
    {
        var template = await db.WhatsAppTemplates.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct);
        if (template is null) return Result<WhatsAppTemplateDto>.NotFound("WhatsApp preset not found.");

        var used = await db.WhatsAppContactLogs.CountAsync(l => l.TemplateId == id, ct);

        return Result<WhatsAppTemplateDto>.Success(Map(template, used));
    }

    public async Task<Result<WhatsAppTemplateDto>> CreateTemplateAsync(
        CreateWhatsAppTemplateRequest request,
        CancellationToken ct = default)
    {
        var name = request.Name.Trim();

        if (await db.WhatsAppTemplates.AnyAsync(t => t.Name == name, ct))
        {
            return Result<WhatsAppTemplateDto>.Conflict($"A preset named '{name}' already exists.");
        }

        var template = new WhatsAppTemplate
        {
            Name = name,
            Description = request.Description.Trim(),
            Message = request.Message,
            IsActive = request.IsActive,
        };

        db.WhatsAppTemplates.Add(template);
        await db.SaveChangesAsync(ct);

        await audit.LogAsync("WhatsAppTemplate.Created", nameof(WhatsAppTemplate), template.Id.ToString(), true,
            new { name }, ct);

        return await GetTemplateAsync(template.Id, ct);
    }

    public async Task<Result<WhatsAppTemplateDto>> UpdateTemplateAsync(
        Guid id,
        UpdateWhatsAppTemplateRequest request,
        CancellationToken ct = default)
    {
        var template = await db.WhatsAppTemplates.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (template is null) return Result<WhatsAppTemplateDto>.NotFound("WhatsApp preset not found.");

        if (request.Name is not null)
        {
            var name = request.Name.Trim();
            if (await db.WhatsAppTemplates.AnyAsync(t => t.Name == name && t.Id != id, ct))
            {
                return Result<WhatsAppTemplateDto>.Conflict($"A preset named '{name}' already exists.");
            }

            template.Name = name;
        }

        if (request.Description is not null) template.Description = request.Description.Trim();
        if (request.Message is not null) template.Message = request.Message;
        if (request.IsActive.HasValue) template.IsActive = request.IsActive.Value;

        await db.SaveChangesAsync(ct);
        await audit.LogAsync("WhatsAppTemplate.Updated", nameof(WhatsAppTemplate), id.ToString(), true, request, ct);

        return await GetTemplateAsync(id, ct);
    }

    public async Task<Result> DeleteTemplateAsync(Guid id, CancellationToken ct = default)
    {
        var template = await db.WhatsAppTemplates.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (template is null) return Result.NotFound("WhatsApp preset not found.");

        // Deleting a preset that has contact history would orphan its audit
        // rows, so retire it instead — the history stays intact.
        var used = await db.WhatsAppContactLogs.AnyAsync(l => l.TemplateId == id, ct);

        if (used)
        {
            template.IsActive = false;
            await db.SaveChangesAsync(ct);

            await audit.LogAsync("WhatsAppTemplate.Retired", nameof(WhatsAppTemplate), id.ToString(), true,
                new { reason = "has contact history" }, ct);

            return Result.Success();
        }

        db.WhatsAppTemplates.Remove(template);
        await db.SaveChangesAsync(ct);

        await audit.LogAsync("WhatsAppTemplate.Deleted", nameof(WhatsAppTemplate), id.ToString(), true, null, ct);
        return Result.Success();
    }

    private static WhatsAppTemplateDto Map(WhatsAppTemplate t, int timesUsed) => new()
    {
        Id = t.Id,
        Name = t.Name,
        Description = t.Description,
        Message = t.Message,
        IsActive = t.IsActive,
        TimesUsed = timesUsed,
        CreatedAt = t.CreatedAt,
    };
}
