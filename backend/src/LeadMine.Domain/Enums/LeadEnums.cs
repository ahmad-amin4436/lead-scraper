namespace LeadMine.Domain.Enums;

/// <summary>Where a lead was discovered.</summary>
public enum BusinessSource
{
    Manual = 0,
    GooglePlaces = 1,
    OpenStreetMap = 2,

    /// <summary>
    /// Google Maps, scraped with a real browser (<c>PlaywrightGoogleMapsProvider</c>).
    /// Also the tag used for a lead found by both the browser and OpenStreetMap
    /// parallel sweep and merged — the browser source's data is preferred on
    /// conflicts, so the merged record is tagged the same as a browser-only one.
    /// <para>Formerly <c>Apify</c> (same underlying value <c>3</c>) — this app no longer uses Apify.</para>
    /// </summary>
    GoogleMapsBrowser = 3,
}

/// <summary>Progress of contact discovery for a lead.</summary>
public enum BusinessStatus
{
    New = 0,
    Enriched = 1,
    Partial = 2,
    NoWebsite = 3,
    EnrichmentFailed = 4,
}

/// <summary>
/// Deliverability signal for a stored email address.
/// <para>
/// <see cref="Valid"/> means the DOMAIN accepts mail (an MX record exists) — it
/// does not prove the individual mailbox exists. Proving that needs an SMTP
/// RCPT probe, which providers block, catch-all domains defeat, and which gets
/// the probing IP blacklisted. Read it as "safe to attempt".
/// </para>
/// </summary>
public enum EmailStatus
{
    Unverified = 0,
    Valid = 1,
    Risky = 2,
    Invalid = 3,
    Unknown = 4,
}

/// <summary>
/// Likelihood a phone number is reachable on WhatsApp.
/// <para>
/// Only <see cref="Confirmed"/> is evidence — a link the business published on
/// its own site. WhatsApp exposes no way to check registration for a number you
/// do not own, so everything else is inferred from the line type.
/// </para>
/// </summary>
public enum WhatsAppStatus
{
    Unverified = 0,
    Confirmed = 1,
    Likely = 2,
    Unlikely = 3,
    None = 4,
}

/// <summary>Lifecycle of a search run.</summary>
public enum SearchJobStatus
{
    Queued = 0,
    Running = 1,
    Stopping = 2,
    Completed = 3,
    Failed = 4,
    Stopped = 5,
}

/// <summary>
/// What a queued <see cref="Entities.SearchJob"/> actually runs — one shared
/// queue/lease/checkpoint mechanism serves both, dispatched by
/// <c>ScraperWorkerService</c> to a different runner per kind.
/// </summary>
public enum JobKind
{
    /// <summary>A category × city sweep, run by <c>SearchRunner</c>.</summary>
    Search = 0,

    /// <summary>
    /// LinkedIn company + decision-maker enrichment for a fixed list of
    /// already-saved leads, run by <c>LinkedInEnrichmentRunner</c>. Exists
    /// because the synchronous version of this (one HTTP request enriching up
    /// to 25 leads) structurally cannot finish inside a serverless proxy's
    /// timeout — see the LinkedIn Enrichment page's history for why.
    /// </summary>
    LinkedInEnrichment = 1,

    /// <summary>
    /// Standalone LinkedIn people search scoped to one named company — title/
    /// keyword/location filters, results saved as <see cref="Entities.Person"/>
    /// rows as they're found, run by <c>LinkedInPeopleSearchRunner</c>. Scoped
    /// to a single company rather than searching across all of LinkedIn
    /// because a general cross-company search returns blurred "LinkedIn
    /// Member" results with no profile link for accounts without much of a
    /// network — confirmed live, not a theoretical restriction. A company's
    /// own People tab does not have that restriction.
    /// </summary>
    LinkedInPeopleSearch = 2,
}

public enum ActivityLogLevel
{
    Debug = 0,
    Information = 1,
    Warning = 2,
    Error = 3,
}
