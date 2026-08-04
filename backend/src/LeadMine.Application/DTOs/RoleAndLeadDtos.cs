using System.ComponentModel.DataAnnotations;
using LeadMine.Application.Validation;
using LeadMine.Domain.Enums;

namespace LeadMine.Application.DTOs;

// --- roles & permissions ----------------------------------------------------

public sealed class RoleDto
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public bool IsSystemRole { get; set; }

    public int UserCount { get; set; }

    public IReadOnlyList<string> Permissions { get; set; } = Array.Empty<string>();
}

public sealed class CreateRoleRequest
{
    [Required, MaxLength(128)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(512)]
    public string Description { get; set; } = string.Empty;

    public List<string> Permissions { get; set; } = new();
}

public sealed class UpdateRoleRequest
{
    [MaxLength(128)]
    public string? Name { get; set; }

    [MaxLength(512)]
    public string? Description { get; set; }
}

public sealed class SetRolePermissionsRequest
{
    /// <summary>The complete permission set for the role; omissions are revoked.</summary>
    public List<string> Permissions { get; set; } = new();
}

public sealed class PermissionDto
{
    public string Name { get; set; } = string.Empty;

    public string Group { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;
}

// --- leads ------------------------------------------------------------------

public sealed class BusinessDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Website { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public EmailStatus EmailStatus { get; set; }
    public string WhatsApp { get; set; } = string.Empty;
    public WhatsAppStatus WhatsAppStatus { get; set; }
    public string Facebook { get; set; } = string.Empty;
    public string Instagram { get; set; } = string.Empty;
    public string LinkedIn { get; set; } = string.Empty;
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public double? Rating { get; set; }
    public int? ReviewCount { get; set; }
    public string MapsUrl { get; set; } = string.Empty;
    public BusinessSource Source { get; set; }
    public BusinessStatus Status { get; set; }
    public string Notes { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }

    // --- Google Maps enrichment (Apify) --------------------------------------
    public string PostalCode { get; set; } = string.Empty;
    public string OpeningHoursJson { get; set; } = string.Empty;
    public string PlaceId { get; set; } = string.Empty;
    public string ImageUrlsJson { get; set; } = string.Empty;
    public bool? PermanentlyClosed { get; set; }
    public DateTimeOffset? LastMapsEnrichedAt { get; set; }

    // --- LinkedIn company enrichment (Apify) ---------------------------------
    public string Industry { get; set; } = string.Empty;
    public int? EmployeeCount { get; set; }
    public string CompanyDescription { get; set; } = string.Empty;
    public DateTimeOffset? LastLinkedInEnrichedAt { get; set; }
}

public sealed class CreateBusinessRequest
{
    [Required, MaxLength(256)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(128)] public string Category { get; set; } = string.Empty;
    [MaxLength(128)] public string Country { get; set; } = string.Empty;
    [MaxLength(128)] public string State { get; set; } = string.Empty;
    [MaxLength(128)] public string City { get; set; } = string.Empty;
    [MaxLength(512)] public string Address { get; set; } = string.Empty;
    [MaxLength(64)] public string Phone { get; set; } = string.Empty;
    [MaxLength(512)] public string Website { get; set; } = string.Empty;

    [OptionalEmailAddress, MaxLength(256)]
    public string Email { get; set; } = string.Empty;

    [MaxLength(512)] public string WhatsApp { get; set; } = string.Empty;
    [MaxLength(512)] public string Facebook { get; set; } = string.Empty;
    [MaxLength(512)] public string Instagram { get; set; } = string.Empty;
    [MaxLength(512)] public string LinkedIn { get; set; } = string.Empty;

    public double? Latitude { get; set; }
    public double? Longitude { get; set; }

    [Range(0, 5)] public double? Rating { get; set; }
    [Range(0, int.MaxValue)] public int? ReviewCount { get; set; }

    [MaxLength(1024)] public string MapsUrl { get; set; } = string.Empty;
    [MaxLength(2000)] public string Notes { get; set; } = string.Empty;

    public BusinessSource Source { get; set; } = BusinessSource.Manual;
}

public sealed class UpdateBusinessRequest
{
    [MaxLength(256)] public string? Name { get; set; }
    [MaxLength(128)] public string? Category { get; set; }
    [MaxLength(64)] public string? Phone { get; set; }

    [OptionalEmailAddress, MaxLength(256)]
    public string? Email { get; set; }

    [MaxLength(512)] public string? Website { get; set; }
    [MaxLength(2000)] public string? Notes { get; set; }

    public BusinessStatus? Status { get; set; }
    public EmailStatus? EmailStatus { get; set; }
    public WhatsAppStatus? WhatsAppStatus { get; set; }
}

/// <summary>Filters for the lead list. Bound from the query string.</summary>
public sealed class BusinessQueryRequest
{
    public string? Search { get; set; }
    public string? Category { get; set; }
    public string? Country { get; set; }
    public string? City { get; set; }
    public BusinessSource? Source { get; set; }
    public BusinessStatus? Status { get; set; }
    public EmailStatus? EmailStatus { get; set; }
    public WhatsAppStatus? WhatsAppStatus { get; set; }

    [Range(0, 5)] public double? MinRating { get; set; }
    [Range(0, int.MaxValue)] public int? MinReviews { get; set; }

    public bool? HasEmail { get; set; }
    public bool? HasPhone { get; set; }
    public bool? HasWebsite { get; set; }

    /// <summary>
    /// True: only leads with a prior send (<c>LastContactedAt</c> set). False:
    /// only leads never contacted. Null: no filter. Exists mainly for the send
    /// screen, so a user can exclude leads they already emailed instead of
    /// re-sending and burning the daily cap on a repeat.
    /// </summary>
    public bool? HasBeenContacted { get; set; }

    /// <summary>
    /// Same idea as <see cref="HasBeenContacted"/> but for the WhatsApp
    /// click-to-chat channel (<c>LastWhatsAppContactedAt</c>), which is tracked
    /// independently since a lead can be emailed without being WhatsApp'd or
    /// vice versa.
    /// </summary>
    public bool? HasBeenWhatsAppContacted { get; set; }

    /// <summary>
    /// Admin-only. Restricts the list to one user's leads. Ignored — and the
    /// caller forced to their own rows — without `leads.view-all`.
    /// </summary>
    public Guid? OwnerUserId { get; set; }

    /// <summary>Coarse lead-quality preset; see <see cref="LeadKind"/>.</summary>
    public LeadKind? Kind { get; set; }

    public string? SortBy { get; set; }
    public string? SortDir { get; set; }

    [Range(1, int.MaxValue)] public int Page { get; set; } = 1;

    [Range(1, 500)] public int PageSize { get; set; } = 25;
}

public sealed class BulkDeleteRequest
{
    [Required, MinLength(1)]
    public List<Guid> Ids { get; set; } = new();
}

public sealed class BusinessStatsDto
{
    public int Total { get; set; }
    public int WithEmail { get; set; }
    public int WithPhone { get; set; }
    public int WithWebsite { get; set; }
    public int WithSocial { get; set; }
    public int Enriched { get; set; }
    public int VerifiedEmails { get; set; }
    public int WhatsAppReachable { get; set; }
    public double? AverageRating { get; set; }
    public IReadOnlyList<CountByLabel> ByCategory { get; set; } = Array.Empty<CountByLabel>();
    public IReadOnlyList<CountByLabel> ByCountry { get; set; } = Array.Empty<CountByLabel>();
    public IReadOnlyList<CountByLabel> BySource { get; set; } = Array.Empty<CountByLabel>();

    /// <summary>Oldest first, one entry per day, including days with zero leads.</summary>
    public IReadOnlyList<CountByDate> AddedLast7Days { get; set; } = Array.Empty<CountByDate>();
}

public sealed record CountByLabel(string Label, int Count);

public sealed record CountByDate(string Date, int Count);

/// <summary>Backfills verification for leads that don't have it yet.</summary>
public sealed class VerifyLeadsRequest
{
    /// <summary>Re-check leads that already carry a status, not just unverified ones.</summary>
    public bool Force { get; set; }

    /// <summary>
    /// Cap per call so one request can't run unbounded against a large table.
    /// The client re-invokes while <see cref="VerifyLeadsResultDto.Remaining"/>
    /// is above zero.
    /// </summary>
    [Range(1, 500)]
    public int Limit { get; set; } = 400;
}

public sealed class VerifyLeadsResultDto
{
    public int Verified { get; set; }
    public int Remaining { get; set; }
    public int Candidates { get; set; }
    public long ElapsedMs { get; set; }
}
