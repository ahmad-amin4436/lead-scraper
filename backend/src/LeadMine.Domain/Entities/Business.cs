using LeadMine.Domain.Common;
using LeadMine.Domain.Enums;

namespace LeadMine.Domain.Entities;

/// <summary>A discovered business lead.</summary>
public class Business : AuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    public string Category { get; set; } = string.Empty;

    public string Country { get; set; } = string.Empty;

    public string State { get; set; } = string.Empty;

    public string City { get; set; } = string.Empty;

    public string Address { get; set; } = string.Empty;

    public string Phone { get; set; } = string.Empty;

    public string Website { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public EmailStatus EmailStatus { get; set; } = EmailStatus.Unverified;

    public string WhatsApp { get; set; } = string.Empty;

    public WhatsAppStatus WhatsAppStatus { get; set; } = WhatsAppStatus.Unverified;

    public string Facebook { get; set; } = string.Empty;

    public string Instagram { get; set; } = string.Empty;

    public string LinkedIn { get; set; } = string.Empty;

    public double? Latitude { get; set; }

    public double? Longitude { get; set; }

    public double? Rating { get; set; }

    public int? ReviewCount { get; set; }

    public string MapsUrl { get; set; } = string.Empty;

    public BusinessSource Source { get; set; } = BusinessSource.Manual;

    public BusinessStatus Status { get; set; } = BusinessStatus.New;

    public string Notes { get; set; } = string.Empty;

    /// <summary>The run that produced this lead, if it came from a search.</summary>
    public Guid? SearchJobId { get; set; }

    public SearchJob? SearchJob { get; set; }

    /// <summary>
    /// The user who scraped or created this lead.
    /// <para>
    /// Every lead query is filtered by this unless the caller holds
    /// <c>leads.view-all</c>, so one user's prospecting is never visible to
    /// another. Nullable only so leads imported before ownership existed still
    /// load; those are treated as unowned and visible to super admins only.
    /// </para>
    /// </summary>
    public Guid? OwnerUserId { get; set; }

    /// <summary>
    /// Normalised website host, phone and name+city keys used for duplicate
    /// detection. Persisted (not computed on read) so they can be indexed —
    /// scanning 100k rows per insert would not scale.
    /// </summary>
    public string? DedupeWebsiteKey { get; set; }

    public string? DedupePhoneKey { get; set; }

    public string? DedupeNameKey { get; set; }

    /// <summary>
    /// Set once the lead has been emailed, so the UI can avoid contacting the
    /// same business twice and the audit trail has a quick reference.
    /// </summary>
    public DateTimeOffset? LastContactedAt { get; set; }

    public int TimesContacted { get; set; }

    /// <summary>
    /// Set when the caller opens a click-to-chat WhatsApp link for this lead —
    /// there is no delivery confirmation the way SMTP gives one, so this means
    /// "a send was initiated", not "the message arrived". Kept separate from
    /// <see cref="LastContactedAt"/> because the two channels are contacted
    /// independently: emailing a lead should not hide it from WhatsApp outreach
    /// or vice versa.
    /// </summary>
    public DateTimeOffset? LastWhatsAppContactedAt { get; set; }

    public int WhatsAppTimesContacted { get; set; }

    // --- Google Maps enrichment (browser) -------------------------------------
    // Filled in after discovery, not by it: Google Places already supplies
    // Rating/ReviewCount/MapsUrl at discovery time, and this step refreshes
    // those plus the detail Places' Text Search doesn't return.

    public string PostalCode { get; set; } = string.Empty;

    /// <summary>Serialized <c>[{"day":"Monday","hours":"9 AM to 5 PM"}, ...]</c>.</summary>
    public string OpeningHoursJson { get; set; } = string.Empty;

    /// <summary>A stable-ish token parsed from the Maps URL (not Google's own Place ID format).</summary>
    public string PlaceId { get; set; } = string.Empty;

    /// <summary>Serialized array of image URLs. No blobs stored — links only.</summary>
    public string ImageUrlsJson { get; set; } = string.Empty;

    public bool? PermanentlyClosed { get; set; }

    public DateTimeOffset? LastMapsEnrichedAt { get; set; }

    // --- LinkedIn company enrichment (browser) --------------------------------

    public string Industry { get; set; } = string.Empty;

    public int? EmployeeCount { get; set; }

    /// <summary>LinkedIn's own company description, distinct from the user-authored <see cref="Notes"/>.</summary>
    public string CompanyDescription { get; set; } = string.Empty;

    public DateTimeOffset? LastLinkedInEnrichedAt { get; set; }

    /// <summary>
    /// Set once LinkedIn people search has run for this company. Distinct from
    /// <see cref="LastLinkedInEnrichedAt"/> (company-level detail, filled in
    /// before the lead is even saved) because people search runs in a separate
    /// phase afterward — against the business's real, saved id — and this is
    /// what stops it re-running for the same company if a run is resumed.
    /// </summary>
    public DateTimeOffset? LastPeopleSearchedAt { get; set; }

    /// <summary>Decision-makers found at this company via LinkedIn people search.</summary>
    public ICollection<Person> People { get; set; } = new List<Person>();

    // --- Email validation pipeline ---------------------------------------------
    // See EmailValidationPipeline. EmailStatus above still carries the final
    // verdict (existing callers/filters read only that); everything below is the
    // supporting detail for how that verdict was reached.

    /// <summary>0-100. Null until the pipeline has evaluated this address.</summary>
    public int? EmailConfidence { get; set; }

    public DateTimeOffset? EmailValidatedAt { get; set; }

    public bool? EmailIsDisposable { get; set; }

    /// <summary>A shared/team mailbox (info@, sales@, ...) rather than a named person.</summary>
    public bool? EmailIsRoleAccount { get; set; }

    /// <summary>
    /// True when the domain's mail server accepts RCPT TO for any address, not
    /// just this one — an SMTP "accepted" result on a catch-all domain proves
    /// nothing about this specific mailbox, so confidence is capped rather than
    /// maxed out. Null when the SMTP probe never ran (off by default — see
    /// EmailValidationOptions.EnableSmtpProbe).
    /// </summary>
    public bool? EmailIsCatchAll { get; set; }

    /// <summary>
    /// Serialized <c>EmailValidationDetails</c> (Infrastructure layer) — MX
    /// hosts used, the SMTP probe outcome, and bounce-check state. Free-form
    /// JSON rather than more columns so new diagnostic detail doesn't need a
    /// migration every time.
    /// </summary>
    public string? EmailValidationDetailsJson { get; set; }

    /// <summary>
    /// Set the moment an RCPT TO probe actually ran for this address —
    /// distinct from <see cref="EmailIsCatchAll"/> being null, which can also
    /// mean "probed, but the catch-all check itself was inconclusive". This is
    /// what <c>EmailSmtpProbeWorkerService</c> uses to find leads that have
    /// never been probed at all, so it doesn't keep re-probing (and re-billing
    /// the per-domain-per-hour budget for) ones it already has an answer for.
    /// </summary>
    public DateTimeOffset? EmailSmtpProbedAt { get; set; }

    /// <summary>The raw SMTP reply code from the most recent probe or bounce (e.g. 550, 450, 250).</summary>
    public int? EmailSmtpCode { get; set; }

    /// <summary>The RFC 3463 extended status code from the most recent probe or bounce (e.g. "5.1.1"), when one was present.</summary>
    public string? EmailDsnCode { get; set; }

    // --- Post-send bounce processing ------------------------------------------
    // Filled in by EmailBounceProcessorService from a real delivery-failure
    // notice matched back to an actual campaign send — distinct from the
    // pre-send SMTP probe above, which never sends anything. See
    // EmailBounceStatus/EmailBounceType's own doc comments for what each value
    // means and drives.

    public EmailBounceStatus EmailBounceStatus { get; set; } = EmailBounceStatus.Clean;

    public EmailBounceType EmailBounceType { get; set; } = EmailBounceType.None;

    public string? EmailBounceReason { get; set; }

    public DateTimeOffset? EmailLastBounceAt { get; set; }

    /// <summary>
    /// How many times this address has been given the benefit of the doubt on
    /// a non-definitive result — a 4xx during an SMTP probe, or a soft bounce
    /// on a real send. Capped (see <c>EmailValidationOptions.SmtpProbeMaxRetries</c>)
    /// so a permanently-4xx-ing server doesn't get retried forever.
    /// </summary>
    public int EmailRetryCount { get; set; }
}
