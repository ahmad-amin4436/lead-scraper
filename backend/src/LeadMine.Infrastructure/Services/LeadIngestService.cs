using LeadMine.Application.DTOs;
using LeadMine.Application.Interfaces;
using LeadMine.Domain.Entities;
using LeadMine.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LeadMine.Infrastructure.Services;

public interface ILeadIngestService
{
    Task<IngestResultDto> IngestAsync(IngestLeadsRequest request, CancellationToken ct = default);
}

/// <summary>
/// Bulk-saves scraped leads on behalf of a user.
/// <para>
/// Separate from <see cref="BusinessService"/> because this path is entered by
/// the trusted worker rather than a signed-in user: the owner is supplied rather
/// than inferred, and duplicate checking is done set-at-a-time so a batch of a
/// few hundred costs one query instead of one per lead.
/// </para>
/// </summary>
public sealed class LeadIngestService(
    LeadMineDbContext db,
    ILogger<LeadIngestService> logger) : ILeadIngestService
{
    public async Task<IngestResultDto> IngestAsync(
        IngestLeadsRequest request,
        CancellationToken ct = default)
    {
        var result = new IngestResultDto { Received = request.Leads.Count };

        var candidates = request.Leads
            .Where(lead => !string.IsNullOrWhiteSpace(lead.Name))
            .Select(lead => ToEntity(lead, request.OwnerUserId, request.SearchJobId))
            .ToList();

        result.Rejected = request.Leads.Count - candidates.Count;

        if (candidates.Count == 0) return result;

        if (request.SkipDuplicates)
        {
            // One pass over the owner's existing keys, rather than a round-trip
            // per candidate — the difference between a batch of 500 costing one
            // query and costing 500.
            var websiteKeys = candidates.Select(c => c.DedupeWebsiteKey).Where(k => k != null).ToList();
            var phoneKeys = candidates.Select(c => c.DedupePhoneKey).Where(k => k != null).ToList();
            var nameKeys = candidates.Select(c => c.DedupeNameKey).Where(k => k != null).ToList();

            var existing = await db.Businesses
                .Where(b => b.OwnerUserId == request.OwnerUserId)
                .Where(b =>
                    (b.DedupeWebsiteKey != null && websiteKeys.Contains(b.DedupeWebsiteKey)) ||
                    (b.DedupePhoneKey != null && phoneKeys.Contains(b.DedupePhoneKey)) ||
                    (b.DedupeNameKey != null && nameKeys.Contains(b.DedupeNameKey)))
                .Select(b => new { b.DedupeWebsiteKey, b.DedupePhoneKey, b.DedupeNameKey })
                .ToListAsync(ct);

            var seenWeb = new HashSet<string>(existing.Where(e => e.DedupeWebsiteKey != null).Select(e => e.DedupeWebsiteKey!));
            var seenPhone = new HashSet<string>(existing.Where(e => e.DedupePhoneKey != null).Select(e => e.DedupePhoneKey!));
            var seenName = new HashSet<string>(existing.Where(e => e.DedupeNameKey != null).Select(e => e.DedupeNameKey!));

            var accepted = new List<Business>(candidates.Count);

            foreach (var candidate in candidates)
            {
                var duplicate =
                    (candidate.DedupeWebsiteKey != null && seenWeb.Contains(candidate.DedupeWebsiteKey)) ||
                    (candidate.DedupePhoneKey != null && seenPhone.Contains(candidate.DedupePhoneKey)) ||
                    (candidate.DedupeNameKey != null && seenName.Contains(candidate.DedupeNameKey));

                if (duplicate)
                {
                    result.Duplicates++;
                    continue;
                }

                // Add to the seen sets as we go, so the batch also dedupes
                // against itself rather than inserting two copies at once.
                if (candidate.DedupeWebsiteKey != null) seenWeb.Add(candidate.DedupeWebsiteKey);
                if (candidate.DedupePhoneKey != null) seenPhone.Add(candidate.DedupePhoneKey);
                if (candidate.DedupeNameKey != null) seenName.Add(candidate.DedupeNameKey);

                accepted.Add(candidate);
            }

            candidates = accepted;
        }

        if (candidates.Count == 0) return result;

        db.Businesses.AddRange(candidates);
        await db.SaveChangesAsync(ct);

        result.Saved = candidates.Count;

        logger.LogInformation(
            "Ingested {Saved} lead(s) for {Owner} ({Duplicates} duplicate, {Rejected} rejected)",
            result.Saved, request.OwnerUserId, result.Duplicates, result.Rejected);

        return result;
    }

    private static Business ToEntity(IngestLead lead, Guid ownerId, Guid? jobId)
    {
        var entity = new Business
        {
            Name = lead.Name.Trim(),
            Category = lead.Category.Trim(),
            Country = lead.Country.Trim(),
            State = lead.State.Trim(),
            City = lead.City.Trim(),
            Address = lead.Address.Trim(),
            Phone = lead.Phone.Trim(),
            Website = lead.Website.Trim(),
            Email = lead.Email.Trim(),
            EmailStatus = lead.EmailStatus,
            WhatsApp = lead.WhatsApp.Trim(),
            WhatsAppStatus = lead.WhatsAppStatus,
            Facebook = lead.Facebook.Trim(),
            Instagram = lead.Instagram.Trim(),
            LinkedIn = lead.LinkedIn.Trim(),
            Latitude = lead.Latitude,
            Longitude = lead.Longitude,
            Rating = lead.Rating,
            ReviewCount = lead.ReviewCount,
            MapsUrl = lead.MapsUrl.Trim(),
            Notes = lead.Notes.Trim(),
            Source = lead.Source,
            Status = lead.Status,
            OwnerUserId = ownerId,
            SearchJobId = jobId,
            PostalCode = lead.PostalCode.Trim(),
            OpeningHoursJson = lead.OpeningHoursJson,
            PlaceId = lead.PlaceId.Trim(),
            ImageUrlsJson = lead.ImageUrlsJson,
            PermanentlyClosed = lead.PermanentlyClosed,
            LastMapsEnrichedAt = lead.MapsEnrichedAt,
            Industry = lead.Industry.Trim(),
            EmployeeCount = lead.EmployeeCount,
            CompanyDescription = lead.CompanyDescription.Trim(),
            LastLinkedInEnrichedAt = lead.LinkedInEnrichedAt,
        };

        BusinessService.ApplyDedupeKeys(entity);
        return entity;
    }
}
