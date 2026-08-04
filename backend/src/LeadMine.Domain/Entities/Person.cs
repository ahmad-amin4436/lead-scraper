using LeadMine.Domain.Common;

namespace LeadMine.Domain.Entities;

/// <summary>
/// An individual professional found via LinkedIn people search, usually tied to
/// a <see cref="Business"/> lead as a decision-maker to contact.
/// <para>
/// Experience/education/skills are LinkedIn's own structured lists, stored as
/// serialized JSON columns rather than child tables — the same choice already
/// made for <c>SearchJob.RequestJson</c>/<c>RecentResultsJson</c>. They are
/// read-only history from the source, never edited field-by-field in this app,
/// so a relational shape would only add join overhead for no real benefit.
/// </para>
/// </summary>
public class Person : AuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The company this person was found at, if it matched an existing lead.</summary>
    public Guid? BusinessId { get; set; }

    public Business? Business { get; set; }

    /// <summary>
    /// Denormalised company name, kept even when <see cref="BusinessId"/> is
    /// null — LinkedIn's own record of where someone works does not always
    /// resolve to a business already in this database.
    /// </summary>
    public string CompanyName { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;

    public string JobTitle { get; set; } = string.Empty;

    /// <summary>LinkedIn's one-line summary under the name (often richer than JobTitle alone).</summary>
    public string Headline { get; set; } = string.Empty;

    public string LinkedInUrl { get; set; } = string.Empty;

    public string Location { get; set; } = string.Empty;

    /// <summary>Serialized <c>[{"company","title","dateRange","description"}, ...]</c>.</summary>
    public string ExperienceJson { get; set; } = string.Empty;

    /// <summary>Serialized <c>[{"school","degree","dateRange"}, ...]</c>.</summary>
    public string EducationJson { get; set; } = string.Empty;

    /// <summary>Serialized string array.</summary>
    public string SkillsJson { get; set; } = string.Empty;

    /// <summary>Public contact info, when the source actually surfaces one — often blank.</summary>
    public string Email { get; set; } = string.Empty;

    public string Phone { get; set; } = string.Empty;

    /// <summary>
    /// True when <see cref="JobTitle"/> matched a known ownership/leadership
    /// pattern (Owner, Founder, CEO, Director, ...) — see
    /// <c>DecisionMakerTitles</c> in the LinkedIn enrichment service. An
    /// inference from job title text, not a verified org-chart fact.
    /// </summary>
    public bool IsDecisionMaker { get; set; }

    /// <summary>The matched title text, kept for context on why this was flagged.</summary>
    public string DecisionMakerRole { get; set; } = string.Empty;

    /// <summary>
    /// The user who ran the search that found this person. Same visibility
    /// rule as <see cref="Business.OwnerUserId"/>: hidden from other users
    /// unless the caller holds <c>leads.view-all</c>.
    /// </summary>
    public Guid? OwnerUserId { get; set; }

    /// <summary>The run that produced this person, if it came from a search.</summary>
    public Guid? SearchJobId { get; set; }
}
