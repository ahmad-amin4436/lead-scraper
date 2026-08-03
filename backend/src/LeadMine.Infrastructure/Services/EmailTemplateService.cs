using LeadMine.Application.Common;
using LeadMine.Application.DTOs;
using LeadMine.Application.Interfaces;
using LeadMine.Domain.Entities;
using LeadMine.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LeadMine.Infrastructure.Services;

public sealed class EmailTemplateService(
    LeadMineDbContext db,
    ICurrentUser currentUser,
    IAuditService audit) : IEmailTemplateService
{
    public async Task<IReadOnlyList<EmailTemplateDto>> GetTemplatesAsync(
        bool activeOnly,
        CancellationToken ct = default)
    {
        var query = db.EmailTemplates.AsNoTracking().Include(t => t.Signature).AsQueryable();
        if (activeOnly) query = query.Where(t => t.IsActive);

        // Send counts in one grouped query rather than per template.
        var counts = await db.EmailLogs
            .Where(l => l.TemplateId != null && l.Status == EmailSendStatus.Sent)
            .GroupBy(l => l.TemplateId!.Value)
            .Select(g => new { TemplateId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TemplateId, x => x.Count, ct);

        var templates = await query.OrderBy(t => t.Name).ToListAsync(ct);

        return templates.Select(t => Map(t, counts.GetValueOrDefault(t.Id))).ToList();
    }

    public async Task<Result<EmailTemplateDto>> GetTemplateAsync(Guid id, CancellationToken ct = default)
    {
        var template = await db.EmailTemplates.AsNoTracking()
            .Include(t => t.Signature)
            .FirstOrDefaultAsync(t => t.Id == id, ct);

        if (template is null) return Result<EmailTemplateDto>.NotFound("Email preset not found.");

        var sent = await db.EmailLogs.CountAsync(
            l => l.TemplateId == id && l.Status == EmailSendStatus.Sent, ct);

        return Result<EmailTemplateDto>.Success(Map(template, sent));
    }

    public async Task<Result<EmailTemplateDto>> CreateTemplateAsync(
        CreateEmailTemplateRequest request,
        CancellationToken ct = default)
    {
        var name = request.Name.Trim();

        if (await db.EmailTemplates.AnyAsync(t => t.Name == name, ct))
        {
            return Result<EmailTemplateDto>.Conflict($"A preset named '{name}' already exists.");
        }

        if (request.SignatureId.HasValue &&
            !await db.EmailSignatures.AnyAsync(s => s.Id == request.SignatureId.Value, ct))
        {
            return Result<EmailTemplateDto>.Failure("The selected signature does not exist.");
        }

        var template = new EmailTemplate
        {
            Name = name,
            Description = request.Description.Trim(),
            Subject = request.Subject.Trim(),
            BodyHtml = request.BodyHtml,
            BodyText = request.BodyText,
            IsActive = request.IsActive,
            SignatureId = request.SignatureId,
        };

        db.EmailTemplates.Add(template);
        await db.SaveChangesAsync(ct);

        await audit.LogAsync("EmailTemplate.Created", nameof(EmailTemplate), template.Id.ToString(), true,
            new { name }, ct);

        return await GetTemplateAsync(template.Id, ct);
    }

    public async Task<Result<EmailTemplateDto>> UpdateTemplateAsync(
        Guid id,
        UpdateEmailTemplateRequest request,
        CancellationToken ct = default)
    {
        var template = await db.EmailTemplates.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (template is null) return Result<EmailTemplateDto>.NotFound("Email preset not found.");

        if (request.Name is not null)
        {
            var name = request.Name.Trim();
            if (await db.EmailTemplates.AnyAsync(t => t.Name == name && t.Id != id, ct))
            {
                return Result<EmailTemplateDto>.Conflict($"A preset named '{name}' already exists.");
            }

            template.Name = name;
        }

        if (request.Description is not null) template.Description = request.Description.Trim();
        if (request.Subject is not null) template.Subject = request.Subject.Trim();
        if (request.BodyHtml is not null) template.BodyHtml = request.BodyHtml;
        if (request.BodyText is not null) template.BodyText = request.BodyText;
        if (request.IsActive.HasValue) template.IsActive = request.IsActive.Value;
        if (request.SignatureId.HasValue) template.SignatureId = request.SignatureId.Value;

        await db.SaveChangesAsync(ct);
        await audit.LogAsync("EmailTemplate.Updated", nameof(EmailTemplate), id.ToString(), true, request, ct);

        return await GetTemplateAsync(id, ct);
    }

    public async Task<Result> DeleteTemplateAsync(Guid id, CancellationToken ct = default)
    {
        var template = await db.EmailTemplates.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (template is null) return Result.NotFound("Email preset not found.");

        // Deleting a preset that has been sent would orphan its audit rows, so
        // retire it instead — the history stays intact and it stops appearing.
        var used = await db.EmailLogs.AnyAsync(l => l.TemplateId == id, ct);

        if (used)
        {
            template.IsActive = false;
            await db.SaveChangesAsync(ct);

            await audit.LogAsync("EmailTemplate.Retired", nameof(EmailTemplate), id.ToString(), true,
                new { reason = "has send history" }, ct);

            return Result.Success();
        }

        db.EmailTemplates.Remove(template);
        await db.SaveChangesAsync(ct);

        await audit.LogAsync("EmailTemplate.Deleted", nameof(EmailTemplate), id.ToString(), true, null, ct);
        return Result.Success();
    }

    public async Task<IReadOnlyList<EmailSignatureDto>> GetSignaturesAsync(
        Guid? ownerUserId,
        CancellationToken ct = default)
    {
        var query = db.EmailSignatures.AsNoTracking().Where(s => s.IsActive);

        // Callers see their own signatures plus the shared ones.
        query = ownerUserId.HasValue
            ? query.Where(s => s.OwnerUserId == ownerUserId.Value || s.OwnerUserId == null)
            : query;

        return await query
            .OrderByDescending(s => s.IsDefault)
            .ThenBy(s => s.Name)
            .Select(s => new EmailSignatureDto
            {
                Id = s.Id,
                Name = s.Name,
                BodyHtml = s.BodyHtml,
                BodyText = s.BodyText,
                OwnerUserId = s.OwnerUserId,
                IsDefault = s.IsDefault,
                IsActive = s.IsActive,
            })
            .ToListAsync(ct);
    }

    public async Task<Result<EmailSignatureDto>> SaveSignatureAsync(
        Guid? id,
        SaveEmailSignatureRequest request,
        CancellationToken ct = default)
    {
        var signature = id.HasValue
            ? await db.EmailSignatures.FirstOrDefaultAsync(s => s.Id == id.Value, ct)
            : null;

        if (id.HasValue && signature is null)
        {
            return Result<EmailSignatureDto>.NotFound("Signature not found.");
        }

        signature ??= new EmailSignature();

        signature.Name = request.Name.Trim();
        signature.BodyHtml = request.BodyHtml;
        signature.BodyText = request.BodyText;
        signature.OwnerUserId = request.OwnerUserId;
        signature.IsDefault = request.IsDefault;
        signature.IsActive = request.IsActive;

        if (!id.HasValue) db.EmailSignatures.Add(signature);

        // Only one default per scope, or resolution becomes arbitrary.
        if (request.IsDefault)
        {
            await db.EmailSignatures
                .Where(s => s.Id != signature.Id && s.OwnerUserId == request.OwnerUserId && s.IsDefault)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsDefault, false), ct);
        }

        await db.SaveChangesAsync(ct);

        await audit.LogAsync("EmailSignature.Saved", nameof(EmailSignature), signature.Id.ToString(), true,
            new { signature.Name }, ct);

        return Result<EmailSignatureDto>.Success(new EmailSignatureDto
        {
            Id = signature.Id,
            Name = signature.Name,
            BodyHtml = signature.BodyHtml,
            BodyText = signature.BodyText,
            OwnerUserId = signature.OwnerUserId,
            IsDefault = signature.IsDefault,
            IsActive = signature.IsActive,
        });
    }

    public async Task<Result> DeleteSignatureAsync(Guid id, CancellationToken ct = default)
    {
        var signature = await db.EmailSignatures.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (signature is null) return Result.NotFound("Signature not found.");

        // Detach from any preset first; the FK is Restrict on purpose so this
        // cannot silently break a template.
        await db.EmailTemplates
            .Where(t => t.SignatureId == id)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.SignatureId, (Guid?)null), ct);

        db.EmailSignatures.Remove(signature);
        await db.SaveChangesAsync(ct);

        await audit.LogAsync("EmailSignature.Deleted", nameof(EmailSignature), id.ToString(), true, null, ct);
        return Result.Success();
    }

    private static EmailTemplateDto Map(EmailTemplate t, int timesSent) => new()
    {
        Id = t.Id,
        Name = t.Name,
        Description = t.Description,
        Subject = t.Subject,
        BodyHtml = t.BodyHtml,
        BodyText = t.BodyText,
        IsActive = t.IsActive,
        SignatureId = t.SignatureId,
        SignatureName = t.Signature?.Name,
        TimesSent = timesSent,
        CreatedAt = t.CreatedAt,
    };
}
