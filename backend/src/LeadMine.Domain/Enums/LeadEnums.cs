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
/// queue/lease/checkpoint mechanism serves all of them, dispatched by
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

    /// <summary>
    /// A preset email batch to a fixed list of leads, run by
    /// <c>EmailSendRunner</c>. Exists for the same reason as
    /// <see cref="LinkedInEnrichment"/>: the synchronous version — one HTTP
    /// request sending to every selected recipient inline, each paced
    /// <c>SmtpOptions.DelayBetweenSendsMs</c> apart — structurally cannot
    /// finish inside a standard Netlify Function's ~10-26s ceiling once more
    /// than a handful of recipients are selected. Confirmed live: a real send
    /// returned <c>504</c> from <c>/api/backend/email/send</c>.
    /// </summary>
    EmailSend = 3,
}

public enum ActivityLogLevel
{
    Debug = 0,
    Information = 1,
    Warning = 2,
    Error = 3,
}

/// <summary>
/// Lifecycle of one send-and-watch-for-bounce attempt (<c>EmailBounceCheckWorkerService</c>).
/// </summary>
public enum EmailBounceCheckStatus
{
    /// <summary>Probe sent; still inside the wait window.</summary>
    Pending = 0,

    /// <summary>No bounce arrived before the wait window elapsed — treated as confirmed deliverable.</summary>
    Confirmed = 1,

    /// <summary>A delivery-failure notice matched back to this attempt.</summary>
    Bounced = 2,

    /// <summary>The probe send itself failed (SMTP error) — inconclusive, not a bounce.</summary>
    SendFailed = 3,
}

/// <summary>
/// What an SMTP response (pre-send RCPT TO probe, or a real send's own
/// rejection) actually proves — shared by <c>SmtpProbe</c> (pre-send) and
/// <c>EmailBounceProcessorService</c> (post-send DSN parsing), since both read
/// the same RFC 5321/3463 codes and must draw the same conclusions from them.
/// <para>
/// The two facts this whole pipeline is built around: a 2xx accept is not
/// proof a mailbox exists (catch-all domains, and providers that accept
/// everything and bounce later), and a 5xx reject is not automatically proof
/// it doesn't (5.7.x is a policy/security decision, not a statement about the
/// address).
/// </para>
/// </summary>
public enum SmtpResponseClass
{
    /// <summary>2xx. Accepted by the receiving server — not proof the mailbox exists.</summary>
    Accepted = 0,

    /// <summary>
    /// 550/551/553 with no extended code, or an extended code meaning "no such
    /// mailbox" (5.1.1 bad destination mailbox, 5.1.2 bad destination system,
    /// 5.1.3 bad mailbox syntax, 5.1.6 mailbox moved, 5.1.10 recipient
    /// rejected). Excludes 5.1.5 ("mailbox valid") and 5.1.0/5.1.4, which are
    /// too ambiguous to call a hard failure.
    /// </summary>
    HardFailure = 1,

    /// <summary>4xx (421, 450, 451, 452, or any other 4xx) — transient, worth retrying later.</summary>
    TempFailure = 2,

    /// <summary>
    /// 5.7.x — a policy, security or authentication decision (greylisting,
    /// SPF/DKIM/DMARC, spam filtering), not a statement that the address
    /// doesn't exist. Must never be auto-classified as invalid.
    /// </summary>
    PolicyRejection = 3,

    /// <summary>Anything else: unparseable, a 5xx not covered above, connection-level failure, or no response at all.</summary>
    Inconclusive = 4,
}

/// <summary>
/// Classification of an actual delivery-failure notice (DSN/NDR) matched back
/// to a real send — <c>EmailBounceProcessorService</c>'s output. Distinct from
/// <see cref="SmtpResponseClass"/> (which is the raw code) in that it's the
/// business-facing label callers act on: see <c>Business.EmailBounceStatus</c>
/// for the resulting suppress/retry/review decision.
/// </summary>
public enum EmailBounceType
{
    /// <summary>No bounce has ever been observed for this address.</summary>
    None = 0,

    /// <summary>Permanent — the mailbox or domain doesn't exist. Stop sending.</summary>
    HardBounce = 1,

    /// <summary>Transient — mailbox full, greylisted, server temporarily down. Worth a later retry.</summary>
    SoftBounce = 2,

    /// <summary>A 5.7.x policy/security rejection — not evidence the address doesn't exist.</summary>
    PolicyRejection = 3,

    /// <summary>A bounce-looking message arrived but couldn't be classified from its code.</summary>
    Unknown = 4,
}

/// <summary>
/// The action-oriented state <c>Business</c> carries forward from the last
/// bounce classification — what a caller should actually do about it, not
/// just what happened.
/// </summary>
public enum EmailBounceStatus
{
    /// <summary>No bounce on record. Safe to send.</summary>
    Clean = 0,

    /// <summary>A hard bounce was confirmed — sends to this address are blocked (see <c>EmailService.SendCoreAsync</c>).</summary>
    Suppressed = 1,

    /// <summary>A soft bounce was seen — eligible for another attempt after backoff, up to the configured retry cap.</summary>
    RetryScheduled = 2,

    /// <summary>A policy rejection was seen — needs a human look, not an automatic verdict either way.</summary>
    UnderReview = 3,
}
