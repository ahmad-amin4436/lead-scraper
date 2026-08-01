namespace LeadMine.Domain.Enums;

/// <summary>Where a lead was discovered.</summary>
public enum BusinessSource
{
    Manual = 0,
    GooglePlaces = 1,
    OpenStreetMap = 2,
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

public enum ActivityLogLevel
{
    Debug = 0,
    Information = 1,
    Warning = 2,
    Error = 3,
}
