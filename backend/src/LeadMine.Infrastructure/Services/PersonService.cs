using LeadMine.Application.Authorization;
using LeadMine.Application.Common;
using LeadMine.Application.DTOs;
using LeadMine.Application.Interfaces;
using LeadMine.Domain.Entities;
using LeadMine.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LeadMine.Infrastructure.Services;

public sealed class PersonService(
    LeadMineDbContext db,
    ICurrentUser currentUser,
    IAuditService audit) : IPersonService
{
    /// <summary>Mirrors <see cref="BusinessService.ScopeToCaller"/>: without view-all, only the caller's own.</summary>
    private IQueryable<Person> ScopeToCaller(IQueryable<Person> query, Guid? requestedOwner)
    {
        if (!currentUser.HasPermission(Permissions.People.ViewAll))
        {
            var me = currentUser.UserId;
            return query.Where(p => p.OwnerUserId == me);
        }

        return requestedOwner.HasValue
            ? query.Where(p => p.OwnerUserId == requestedOwner.Value)
            : query;
    }

    public async Task<PagedResult<PersonDto>> QueryAsync(PersonQueryRequest request, CancellationToken ct = default)
    {
        var query = ScopeToCaller(db.People.AsNoTracking(), request.OwnerUserId);

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim().ToLower();
            query = query.Where(p =>
                p.FullName.ToLower().Contains(term) ||
                p.CompanyName.ToLower().Contains(term) ||
                p.JobTitle.ToLower().Contains(term));
        }

        if (request.BusinessId.HasValue) query = query.Where(p => p.BusinessId == request.BusinessId.Value);
        if (request.IsDecisionMaker.HasValue) query = query.Where(p => p.IsDecisionMaker == request.IsDecisionMaker.Value);

        var total = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(p => p.CreatedAt)
            .Skip((Math.Max(1, request.Page) - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(p => Map(p))
            .ToListAsync(ct);

        return PagedResult<PersonDto>.Create(items, total, request.Page, request.PageSize);
    }

    public async Task<Result<PersonDto>> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var person = await ScopeToCaller(db.People.AsNoTracking(), null).FirstOrDefaultAsync(p => p.Id == id, ct);
        return person is null
            ? Result<PersonDto>.NotFound("Person not found.")
            : Result<PersonDto>.Success(Map(person));
    }

    public async Task<Result<PersonDto>> UpdateAsync(Guid id, UpdatePersonRequest request, CancellationToken ct = default)
    {
        var person = await ScopeToCaller(db.People, null).FirstOrDefaultAsync(p => p.Id == id, ct);
        if (person is null) return Result<PersonDto>.NotFound("Person not found.");

        if (request.FullName is not null) person.FullName = request.FullName.Trim();
        if (request.JobTitle is not null) person.JobTitle = request.JobTitle.Trim();
        if (request.Email is not null) person.Email = request.Email.Trim();
        if (request.Phone is not null) person.Phone = request.Phone.Trim();
        if (request.IsDecisionMaker.HasValue) person.IsDecisionMaker = request.IsDecisionMaker.Value;

        await db.SaveChangesAsync(ct);
        await audit.LogAsync("Person.Updated", nameof(Person), id.ToString(), true, request, ct);

        return Result<PersonDto>.Success(Map(person));
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var person = await ScopeToCaller(db.People, null).FirstOrDefaultAsync(p => p.Id == id, ct);
        if (person is null) return Result.NotFound("Person not found.");

        // Soft delete, same as Business — DbContext.SaveChanges converts this
        // Remove into IsDeleted = true, so the record survives for audit even
        // though it drops out of every normal query.
        db.People.Remove(person);
        await db.SaveChangesAsync(ct);

        await audit.LogAsync("Person.Deleted", nameof(Person), id.ToString(), true, null, ct);
        return Result.Success();
    }

    private static PersonDto Map(Person p) => new()
    {
        Id = p.Id,
        BusinessId = p.BusinessId,
        CompanyName = p.CompanyName,
        FullName = p.FullName,
        JobTitle = p.JobTitle,
        Headline = p.Headline,
        LinkedInUrl = p.LinkedInUrl,
        Location = p.Location,
        ExperienceJson = p.ExperienceJson,
        EducationJson = p.EducationJson,
        SkillsJson = p.SkillsJson,
        Email = p.Email,
        Phone = p.Phone,
        IsDecisionMaker = p.IsDecisionMaker,
        DecisionMakerRole = p.DecisionMakerRole,
        CreatedAt = p.CreatedAt,
    };
}
