namespace LeadMine.Domain.Enums;

/// <summary>
/// Coarse "what kind of lead do I want" preset.
/// <para>
/// These map onto combinations of <see cref="BusinessStatus"/>, contact presence
/// and verification status. They exist because asking a user to reason about
/// four separate filters to express "only ones I can actually email" is a bad
/// interface — this is the question they are really asking.
/// </para>
/// <para>
/// Applied both as a search-time filter (which results are worth keeping) and as
/// a database filter (which saved leads to list or export).
/// </para>
/// </summary>
public enum LeadKind
{
    /// <summary>No restriction.</summary>
    Any = 0,

    /// <summary>Discovered but not yet contact-enriched.</summary>
    New = 1,

    /// <summary>Enrichment found a usable email address.</summary>
    Enriched = 2,

    /// <summary>No website to crawl, so contact details are unlikely.</summary>
    NoWebsite = 3,

    /// <summary>Enrichment found something, but no email.</summary>
    Partial = 4,

    /// <summary>Reachable on WhatsApp (confirmed or likely), regardless of email.</summary>
    WhatsAppOnly = 5,

    /// <summary>Has an email address, regardless of anything else.</summary>
    EmailOnly = 6,

    /// <summary>Has an email whose domain accepts mail — the safest to contact.</summary>
    DeliverableEmail = 7,
}
