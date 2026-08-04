using System.Text.RegularExpressions;
using LeadMine.Application.Authorization;
using LeadMine.Application.Common;
using LeadMine.Application.DTOs;
using LeadMine.Application.Interfaces;
using LeadMine.Domain.Entities;
using LeadMine.Domain.Enums;
using LeadMine.Infrastructure.Persistence;
using LeadMine.Infrastructure.Scraping.Verification;
using Microsoft.EntityFrameworkCore;

namespace LeadMine.Infrastructure.Services;

public sealed partial class BusinessService(
    LeadMineDbContext db,
    ICurrentUser currentUser,
    IAuditService audit,
    VerificationService verification) : IBusinessService
{
    /// <summary>
    /// Restricts a lead query to what the caller is allowed to see.
    /// <para>
    /// This is the single chokepoint for tenant isolation: without
    /// <c>leads.view-all</c> the caller only ever sees rows they own, and the
    /// requested <c>OwnerUserId</c> is ignored rather than honoured — otherwise
    /// anyone could read another user's pipeline by guessing an id.
    /// </para>
    /// </summary>
    private IQueryable<Business> ScopeToCaller(IQueryable<Business> query, Guid? requestedOwner)
    {
        if (!currentUser.HasPermission(Permissions.Leads.ViewAll))
        {
            var me = currentUser.UserId;
            return query.Where(b => b.OwnerUserId == me);
        }

        return requestedOwner.HasValue
            ? query.Where(b => b.OwnerUserId == requestedOwner.Value)
            : query;
    }

    /// <summary>
    /// Applies the coarse "kind of lead" preset. Kept next to the enum's intent:
    /// each case is the filter combination a user means when they pick it.
    /// </summary>
    private static IQueryable<Business> ApplyKind(IQueryable<Business> query, LeadKind? kind) => kind switch
    {
        null or LeadKind.Any => query,
        LeadKind.New => query.Where(b => b.Status == BusinessStatus.New),
        LeadKind.Enriched => query.Where(b => b.Status == BusinessStatus.Enriched),
        LeadKind.NoWebsite => query.Where(b => b.Status == BusinessStatus.NoWebsite),
        LeadKind.Partial => query.Where(b => b.Status == BusinessStatus.Partial),
        LeadKind.WhatsAppOnly => query.Where(b =>
            b.WhatsAppStatus == WhatsAppStatus.Confirmed || b.WhatsAppStatus == WhatsAppStatus.Likely),
        LeadKind.EmailOnly => query.Where(b => b.Email != ""),
        LeadKind.DeliverableEmail => query.Where(b => b.Email != "" && b.EmailStatus == EmailStatus.Valid),
        _ => query,
    };

    public async Task<PagedResult<BusinessDto>> QueryAsync(
        BusinessQueryRequest request,
        CancellationToken ct = default)
    {
        var query = ApplyFilters(db.Businesses.AsNoTracking(), request);

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
    /// Deletes every lead matching <paramref name="request"/>'s filters.
    /// <para>
    /// Shares <see cref="ApplyFilters"/> with <see cref="QueryAsync"/> rather
    /// than re-stating the criteria: "delete all" must remove exactly what the
    /// screen it was triggered from was showing, not a second interpretation of
    /// the same filters that could quietly diverge from it. Page and sort
    /// fields on the request are ignored — a delete has no page.
    /// </para>
    /// </summary>
    public async Task<Result<int>> DeleteAllAsync(BusinessQueryRequest request, CancellationToken ct = default)
    {
        // Tracked, not AsNoTracking: RemoveRange needs the entities attached so
        // SaveChanges can convert the removal into a soft delete.
        var entities = await ApplyFilters(db.Businesses, request).ToListAsync(ct);
        if (entities.Count == 0) return Result<int>.Success(0);

        db.Businesses.RemoveRange(entities);
        await db.SaveChangesAsync(ct);

        await audit.LogAsync("Lead.BulkDeleted", nameof(Business), null, true,
            new { count = entities.Count }, ct);

        return Result<int>.Success(entities.Count);
    }

    /// <summary>Every filter <see cref="QueryAsync"/> and <see cref="DeleteAllAsync"/> share.</summary>
    private IQueryable<Business> ApplyFilters(IQueryable<Business> source, BusinessQueryRequest request)
    {
        var query = ApplyKind(ScopeToCaller(source, request.OwnerUserId), request.Kind);

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

        if (request.HasBeenContacted.HasValue)
        {
            query = request.HasBeenContacted.Value
                ? query.Where(b => b.LastContactedAt != null)
                : query.Where(b => b.LastContactedAt == null);
        }

        if (request.HasBeenWhatsAppContacted.HasValue)
        {
            query = request.HasBeenWhatsAppContacted.Value
                ? query.Where(b => b.LastWhatsAppContactedAt != null)
                : query.Where(b => b.LastWhatsAppContactedAt == null);
        }

        if (request.HasBeenLinkedInEnriched.HasValue)
        {
            query = request.HasBeenLinkedInEnriched.Value
                ? query.Where(b => b.LastLinkedInEnrichedAt != null)
                : query.Where(b => b.LastLinkedInEnrichedAt == null);
        }

        return query;
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
        // Scoped, so another user's lead reads as "not found" rather than
        // confirming it exists.
        var entity = await ScopeToCaller(db.Businesses.AsNoTracking(), null)
            .FirstOrDefaultAsync(b => b.Id == id, ct);

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
            // Ownership is taken from the authenticated caller, never the payload.
            OwnerUserId = currentUser.UserId,
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
        var entity = await ScopeToCaller(db.Businesses, null).FirstOrDefaultAsync(b => b.Id == id, ct);
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

        // Scoped: a delete can only ever touch rows the caller may see.
        var entities = await ScopeToCaller(db.Businesses, null)
            .Where(b => ids.Contains(b.Id))
            .ToListAsync(ct);

        // Soft delete: SaveChanges converts Remove into IsDeleted = true.
        db.Businesses.RemoveRange(entities);
        await db.SaveChangesAsync(ct);

        await audit.LogAsync("Lead.Deleted", nameof(Business), null, true, new { count = entities.Count }, ct);
        return Result<int>.Success(entities.Count);
    }

    public async Task<BusinessStatsDto> GetStatsAsync(CancellationToken ct = default)
    {
        // Dashboard numbers must match what the user can actually see.
        var query = ScopeToCaller(db.Businesses.AsNoTracking(), null);

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
            AddedLast7Days = await LastSevenDaysAsync(query, ct),
        };
    }

    /// <summary>
    /// One count per day for the last 7 days (today inclusive), oldest first,
    /// with zero-lead days present rather than omitted — the dashboard chart
    /// draws a fixed 7 bars and would misread a missing day as "no data yet"
    /// instead of "zero".
    /// </summary>
    private static async Task<List<CountByDate>> LastSevenDaysAsync(IQueryable<Business> query, CancellationToken ct)
    {
        var since = DateTimeOffset.UtcNow.Date.AddDays(-6);

        // Grouped by calendar-part tuple rather than `.Date`: every EF Core SQL
        // Server provider translates year/month/day grouping reliably, whereas
        // DateTimeOffset.Date translation has been provider-version-dependent.
        var daily = await query
            .Where(b => b.CreatedAt >= since)
            .GroupBy(b => new { b.CreatedAt.Year, b.CreatedAt.Month, b.CreatedAt.Day })
            .Select(g => new { g.Key.Year, g.Key.Month, g.Key.Day, Count = g.Count() })
            .ToListAsync(ct);

        return Enumerable.Range(0, 7)
            .Select(offset => since.AddDays(offset))
            .Select(day => new CountByDate(
                day.ToString("yyyy-MM-dd"),
                daily.Find(d => d.Year == day.Year && d.Month == day.Month && d.Day == day.Day)?.Count ?? 0))
            .ToList();
    }

    /// <summary>
    /// Backfills verification in bounded batches, so one call can't run
    /// unbounded against a large table. The caller re-invokes while
    /// <see cref="VerifyLeadsResultDto.Remaining"/> is above zero.
    /// </summary>
    public async Task<VerifyLeadsResultDto> VerifyAsync(VerifyLeadsRequest request, CancellationToken ct = default)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        // Only rows that have something worth checking.
        var candidateQuery = ScopeToCaller(db.Businesses, null)
            .Where(b => b.Email != "" || b.Phone != "");

        if (!request.Force)
        {
            candidateQuery = candidateQuery.Where(b =>
                b.EmailStatus == EmailStatus.Unverified || b.WhatsAppStatus == WhatsAppStatus.Unverified);
        }

        var candidateCount = await candidateQuery.CountAsync(ct);

        var batch = await candidateQuery
            .OrderBy(b => b.Id)
            .Take(request.Limit)
            .ToListAsync(ct);

        // No concurrent DbContext access happens inside the loop — each check is
        // DNS lookups and phone parsing against a distinct entity, so running
        // them side by side is safe; SaveChanges happens once afterward.
        await Parallel.ForEachAsync(
            batch,
            new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = ct },
            async (lead, token) =>
            {
                try
                {
                    await verification.ApplyToAsync(lead, token);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    throw;
                }
                catch
                {
                    // Best-effort backfill: one lead's DNS hiccup should not
                    // sink the batch, and the unresolved row just gets picked
                    // up again on the next call.
                }
            });

        await db.SaveChangesAsync(ct);

        await audit.LogAsync("Lead.Verified", nameof(Business), null, true,
            new { count = batch.Count, force = request.Force }, ct);

        return new VerifyLeadsResultDto
        {
            Verified = batch.Count,
            Remaining = Math.Max(0, candidateCount - batch.Count),
            Candidates = candidateCount,
            ElapsedMs = stopwatch.ElapsedMilliseconds,
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
        // Duplicates are per-owner: two users prospecting the same city should
        // each get their own copy, not have the second one silently rejected.
        var owner = candidate.OwnerUserId;

        return await db.Businesses
            .Where(b => b.OwnerUserId == owner)
            .FirstOrDefaultAsync(b =>
            (candidate.DedupeWebsiteKey != null && b.DedupeWebsiteKey == candidate.DedupeWebsiteKey) ||
            (candidate.DedupePhoneKey != null && b.DedupePhoneKey == candidate.DedupePhoneKey) ||
            (candidate.DedupeNameKey != null && b.DedupeNameKey == candidate.DedupeNameKey), ct);
    }

    /// <summary>
    /// Computes the normalised identity keys used for duplicate detection.
    /// Internal so the ingest path derives them the same way — two different
    /// implementations would silently disagree about what a duplicate is.
    /// </summary>
    internal static void ApplyDedupeKeys(Business entity)
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
        PostalCode = b.PostalCode,
        OpeningHoursJson = b.OpeningHoursJson,
        PlaceId = b.PlaceId,
        ImageUrlsJson = b.ImageUrlsJson,
        PermanentlyClosed = b.PermanentlyClosed,
        LastMapsEnrichedAt = b.LastMapsEnrichedAt,
        Industry = b.Industry,
        EmployeeCount = b.EmployeeCount,
        CompanyDescription = b.CompanyDescription,
        LastLinkedInEnrichedAt = b.LastLinkedInEnrichedAt,
    };
}
