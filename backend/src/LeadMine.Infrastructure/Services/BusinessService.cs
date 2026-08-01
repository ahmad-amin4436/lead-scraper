using System.Text.RegularExpressions;
using LeadMine.Application.Common;
using LeadMine.Application.DTOs;
using LeadMine.Application.Interfaces;
using LeadMine.Domain.Entities;
using LeadMine.Domain.Enums;
using LeadMine.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LeadMine.Infrastructure.Services;

public sealed partial class BusinessService(
    LeadMineDbContext db,
    IAuditService audit) : IBusinessService
{
    public async Task<PagedResult<BusinessDto>> QueryAsync(
        BusinessQueryRequest request,
        CancellationToken ct = default)
    {
        var query = db.Businesses.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim().ToLower();
            query = query.Where(b =>
                b.Name.ToLower().Contains(term) ||
                b.Email.ToLower().Contains(term) ||
                b.City.ToLower().Contains(term) ||
                b.Website.ToLower().Contains(term) ||
                b.Phone.Contains(term));
        }

        if (!string.IsNullOrWhiteSpace(request.Category)) query = query.Where(b => b.Category == request.Category);
        if (!string.IsNullOrWhiteSpace(request.Country)) query = query.Where(b => b.Country == request.Country);
        if (!string.IsNullOrWhiteSpace(request.City)) query = query.Where(b => b.City == request.City);

        if (request.Source.HasValue) query = query.Where(b => b.Source == request.Source.Value);
        if (request.Status.HasValue) query = query.Where(b => b.Status == request.Status.Value);
        if (request.EmailStatus.HasValue) query = query.Where(b => b.EmailStatus == request.EmailStatus.Value);
        if (request.WhatsAppStatus.HasValue) query = query.Where(b => b.WhatsAppStatus == request.WhatsAppStatus.Value);

        if (request.MinRating.HasValue) query = query.Where(b => b.Rating >= request.MinRating.Value);
        if (request.MinReviews.HasValue) query = query.Where(b => b.ReviewCount >= request.MinReviews.Value);

        if (request.HasEmail.HasValue)
        {
            query = request.HasEmail.Value ? query.Where(b => b.Email != "") : query.Where(b => b.Email == "");
        }

        if (request.HasPhone.HasValue)
        {
            query = request.HasPhone.Value ? query.Where(b => b.Phone != "") : query.Where(b => b.Phone == "");
        }

        if (request.HasWebsite.HasValue)
        {
            query = request.HasWebsite.Value ? query.Where(b => b.Website != "") : query.Where(b => b.Website == "");
        }

        var total = await query.CountAsync(ct);

        query = ApplySort(query, request.SortBy, request.SortDir);

        var items = await query
            .Skip((Math.Max(1, request.Page) - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(b => Map(b))
            .ToListAsync(ct);

        return PagedResult<BusinessDto>.Create(items, total, request.Page, request.PageSize);
    }

    /// <summary>
    /// Whitelisted sorting. Binding a raw column name straight into the query
    /// would let a caller order by anything in the table, so unknown values fall
    /// back to newest-first.
    /// </summary>
    private static IQueryable<Business> ApplySort(IQueryable<Business> query, string? sortBy, string? sortDir)
    {
        var descending = !string.Equals(sortDir, "asc", StringComparison.OrdinalIgnoreCase);

        return (sortBy?.ToLowerInvariant()) switch
        {
            "name" => descending ? query.OrderByDescending(b => b.Name) : query.OrderBy(b => b.Name),
            "category" => descending ? query.OrderByDescending(b => b.Category) : query.OrderBy(b => b.Category),
            "city" => descending ? query.OrderByDescending(b => b.City) : query.OrderBy(b => b.City),
            "rating" => descending ? query.OrderByDescending(b => b.Rating) : query.OrderBy(b => b.Rating),
            "reviewcount" => descending ? query.OrderByDescending(b => b.ReviewCount) : query.OrderBy(b => b.ReviewCount),
            "email" => descending ? query.OrderByDescending(b => b.Email) : query.OrderBy(b => b.Email),
            _ => descending ? query.OrderByDescending(b => b.CreatedAt) : query.OrderBy(b => b.CreatedAt),
        };
    }

    public async Task<Result<BusinessDto>> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await db.Businesses.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id, ct);
        return entity is null
            ? Result<BusinessDto>.NotFound("Lead not found.")
            : Result<BusinessDto>.Success(Map(entity));
    }

    public async Task<Result<BusinessDto>> CreateAsync(CreateBusinessRequest request, CancellationToken ct = default)
    {
        var entity = new Business
        {
            Name = request.Name.Trim(),
            Category = request.Category.Trim(),
            Country = request.Country.Trim(),
            State = request.State.Trim(),
            City = request.City.Trim(),
            Address = request.Address.Trim(),
            Phone = request.Phone.Trim(),
            Website = request.Website.Trim(),
            Email = request.Email.Trim(),
            WhatsApp = request.WhatsApp.Trim(),
            Facebook = request.Facebook.Trim(),
            Instagram = request.Instagram.Trim(),
            LinkedIn = request.LinkedIn.Trim(),
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            Rating = request.Rating,
            ReviewCount = request.ReviewCount,
            MapsUrl = request.MapsUrl.Trim(),
            Notes = request.Notes.Trim(),
            Source = request.Source,
            Status = string.IsNullOrWhiteSpace(request.Website) ? BusinessStatus.NoWebsite : BusinessStatus.New,
        };

        ApplyDedupeKeys(entity);

        var duplicate = await FindDuplicateAsync(entity, ct);
        if (duplicate is not null)
        {
            return Result<BusinessDto>.Conflict(
                $"A lead matching this one already exists: '{duplicate.Name}'.");
        }

        db.Businesses.Add(entity);
        await db.SaveChangesAsync(ct);

        await audit.LogAsync("Lead.Created", nameof(Business), entity.Id.ToString(), true,
            new { entity.Name, entity.City }, ct);

        return Result<BusinessDto>.Success(Map(entity));
    }

    public async Task<Result<BusinessDto>> UpdateAsync(
        Guid id,
        UpdateBusinessRequest request,
        CancellationToken ct = default)
    {
        var entity = await db.Businesses.FirstOrDefaultAsync(b => b.Id == id, ct);
        if (entity is null) return Result<BusinessDto>.NotFound("Lead not found.");

        if (request.Name is not null) entity.Name = request.Name.Trim();
        if (request.Category is not null) entity.Category = request.Category.Trim();
        if (request.Phone is not null) entity.Phone = request.Phone.Trim();
        if (request.Email is not null) entity.Email = request.Email.Trim();
        if (request.Website is not null) entity.Website = request.Website.Trim();
        if (request.Notes is not null) entity.Notes = request.Notes.Trim();
        if (request.Status.HasValue) entity.Status = request.Status.Value;
        if (request.EmailStatus.HasValue) entity.EmailStatus = request.EmailStatus.Value;
        if (request.WhatsAppStatus.HasValue) entity.WhatsAppStatus = request.WhatsAppStatus.Value;

        // Identity fields may have changed, so the dedupe keys must follow.
        ApplyDedupeKeys(entity);

        await db.SaveChangesAsync(ct);
        await audit.LogAsync("Lead.Updated", nameof(Business), id.ToString(), true, request, ct);

        return Result<BusinessDto>.Success(Map(entity));
    }

    public async Task<Result<int>> DeleteAsync(IReadOnlyList<Guid> ids, CancellationToken ct = default)
    {
        if (ids.Count == 0) return Result<int>.Success(0);

        var entities = await db.Businesses.Where(b => ids.Contains(b.Id)).ToListAsync(ct);

        // Soft delete: SaveChanges converts Remove into IsDeleted = true.
        db.Businesses.RemoveRange(entities);
        await db.SaveChangesAsync(ct);

        await audit.LogAsync("Lead.Deleted", nameof(Business), null, true, new { count = entities.Count }, ct);
        return Result<int>.Success(entities.Count);
    }

    public async Task<BusinessStatsDto> GetStatsAsync(CancellationToken ct = default)
    {
        var query = db.Businesses.AsNoTracking();

        // A single aggregate rather than one query per counter.
        var totals = await query
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Total = g.Count(),
                WithEmail = g.Count(b => b.Email != ""),
                WithPhone = g.Count(b => b.Phone != ""),
                WithWebsite = g.Count(b => b.Website != ""),
                WithSocial = g.Count(b =>
                    b.Facebook != "" || b.Instagram != "" || b.LinkedIn != "" || b.WhatsApp != ""),
                Enriched = g.Count(b => b.Status == BusinessStatus.Enriched || b.Status == BusinessStatus.Partial),
                VerifiedEmails = g.Count(b => b.EmailStatus == EmailStatus.Valid),
                WhatsAppReachable = g.Count(b =>
                    b.WhatsAppStatus == WhatsAppStatus.Confirmed || b.WhatsAppStatus == WhatsAppStatus.Likely),
                AverageRating = g.Where(b => b.Rating != null).Average(b => b.Rating),
            })
            .FirstOrDefaultAsync(ct);

        var byCategory = await TopCountsAsync(query.Where(b => b.Category != ""), b => b.Category, ct);
        var byCountry = await TopCountsAsync(query.Where(b => b.Country != ""), b => b.Country, ct);

        var bySource = await query
            .GroupBy(b => b.Source)
            .Select(g => new { Source = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        return new BusinessStatsDto
        {
            Total = totals?.Total ?? 0,
            WithEmail = totals?.WithEmail ?? 0,
            WithPhone = totals?.WithPhone ?? 0,
            WithWebsite = totals?.WithWebsite ?? 0,
            WithSocial = totals?.WithSocial ?? 0,
            Enriched = totals?.Enriched ?? 0,
            VerifiedEmails = totals?.VerifiedEmails ?? 0,
            WhatsAppReachable = totals?.WhatsAppReachable ?? 0,
            AverageRating = totals?.AverageRating,
            ByCategory = byCategory,
            ByCountry = byCountry,
            BySource = bySource
                .Select(x => new CountByLabel(x.Source.ToString(), x.Count))
                .OrderByDescending(x => x.Count)
                .ToList(),
        };
    }

    private static async Task<List<CountByLabel>> TopCountsAsync(
        IQueryable<Business> query,
        System.Linq.Expressions.Expression<Func<Business, string>> selector,
        CancellationToken ct)
    {
        var grouped = await query
            .GroupBy(selector)
            .Select(g => new { Label = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .Take(8)
            .ToListAsync(ct);

        return grouped.Select(x => new CountByLabel(x.Label, x.Count)).ToList();
    }

    /// <summary>
    /// A candidate is a duplicate if it matches an existing lead on website host,
    /// phone, or name-within-city — the same rules the scraper front end uses.
    /// </summary>
    private async Task<Business?> FindDuplicateAsync(Business candidate, CancellationToken ct)
    {
        return await db.Businesses.FirstOrDefaultAsync(b =>
            (candidate.DedupeWebsiteKey != null && b.DedupeWebsiteKey == candidate.DedupeWebsiteKey) ||
            (candidate.DedupePhoneKey != null && b.DedupePhoneKey == candidate.DedupePhoneKey) ||
            (candidate.DedupeNameKey != null && b.DedupeNameKey == candidate.DedupeNameKey), ct);
    }

    private static void ApplyDedupeKeys(Business entity)
    {
        entity.DedupeWebsiteKey = NormalizeHost(entity.Website);

        var digits = DigitsOnly().Replace(entity.Phone ?? string.Empty, string.Empty);
        // Under 7 digits is an extension or noise, not an identity.
        entity.DedupePhoneKey = digits.Length >= 7
            ? digits[Math.Max(0, digits.Length - 10)..]
            : null;

        var name = NormalizeText(entity.Name);
        entity.DedupeNameKey = string.IsNullOrEmpty(name)
            ? null
            : $"{name}|{NormalizeText(string.IsNullOrWhiteSpace(entity.City) ? entity.Country : entity.City)}";
    }

    private static string? NormalizeHost(string? website)
    {
        if (string.IsNullOrWhiteSpace(website)) return null;

        var value = website.Trim();
        if (!value.Contains("://")) value = $"https://{value}";

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)) return null;

        var host = uri.Host.ToLowerInvariant();
        return host.StartsWith("www.", StringComparison.Ordinal) ? host[4..] : host;
    }

    private static string NormalizeText(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : NonAlphanumeric().Replace(value.ToLowerInvariant(), " ").Trim();

    [GeneratedRegex(@"\D")]
    private static partial Regex DigitsOnly();

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonAlphanumeric();

    private static BusinessDto Map(Business b) => new()
    {
        Id = b.Id,
        Name = b.Name,
        Category = b.Category,
        Country = b.Country,
        State = b.State,
        City = b.City,
        Address = b.Address,
        Phone = b.Phone,
        Website = b.Website,
        Email = b.Email,
        EmailStatus = b.EmailStatus,
        WhatsApp = b.WhatsApp,
        WhatsAppStatus = b.WhatsAppStatus,
        Facebook = b.Facebook,
        Instagram = b.Instagram,
        LinkedIn = b.LinkedIn,
        Latitude = b.Latitude,
        Longitude = b.Longitude,
        Rating = b.Rating,
        ReviewCount = b.ReviewCount,
        MapsUrl = b.MapsUrl,
        Source = b.Source,
        Status = b.Status,
        Notes = b.Notes,
        CreatedAt = b.CreatedAt,
    };
}
