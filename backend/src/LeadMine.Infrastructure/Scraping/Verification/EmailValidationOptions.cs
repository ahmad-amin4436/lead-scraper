using System.ComponentModel.DataAnnotations;

namespace LeadMine.Infrastructure.Scraping.Verification;

/// <summary>
/// Tuning for the two background email-validation passes:
/// <see cref="EmailValidationWorkerService"/> (normalize / syntax / DNS / MX /
/// disposable / role-account, plus the optional SMTP probe) and
/// <see cref="EmailBounceCheckWorkerService"/> (send-and-watch-for-bounce, via a
/// real mailbox).
/// <para>
/// The two passes are deliberately separate services with deliberately
/// different risk profiles. The first only ever talks DNS and — if enabled —
/// makes a raw SMTP handshake it abandons before sending anything; nothing it
/// does can bounce or land in anyone's inbox. The second actually sends mail
/// from a real account, so it is gated far more conservatively (opt-in, capped
/// per day and per hour) — see <see cref="EnableBounceCheck"/>.
/// </para>
/// </summary>
public sealed class EmailValidationOptions
{
    public const string SectionName = "EmailValidation";

    /// <summary>Master switch for both passes.</summary>
    public bool Enabled { get; set; } = true;

    // --- Fast pass: normalize / syntax / DNS / MX / disposable / role -------
    // Cheap and safe to run often — no external party ever sees this traffic
    // beyond an ordinary DNS query, the same kind every browser makes.

    /// <summary>
    /// How often the fast pass wakes up and looks for work. Genuinely "every
    /// second" by default — safe at that cadence because each tick only claims
    /// a small batch (<see cref="BatchSize"/>) and the work itself is just DNS.
    /// </summary>
    [Range(1, 300)]
    public int PollSeconds { get; set; } = 1;

    /// <summary>Leads validated per tick.</summary>
    [Range(1, 200)]
    public int BatchSize { get; set; } = 10;

    /// <summary>
    /// A lead already validated more recently than this is left alone. DNS/MX
    /// records do change, so this is a periodic recheck, not a one-time pass.
    /// </summary>
    [Range(1, 365)]
    public int RevalidateAfterDays { get; set; } = 30;

    // --- SMTP probe: catch-all detection + RCPT TO on the real address ------
    // Off by default. EmailVerifier's own remarks explain why this is normally
    // skipped entirely: major providers refuse or lie about it, catch-all
    // domains accept every address regardless of whether it exists, greylisting
    // produces false negatives, and probing at volume from one IP risks that IP
    // being blacklisted — which would hurt deliverability for every legitimate
    // send this app makes afterward, not just this lookup. Enabling this is a
    // deliberate trade of some of that risk for a stronger validity signal;
    // the rate limits below exist to keep the risk small, not eliminate it.

    /// <summary>Enables the RCPT TO probe and catch-all check. Off by default.</summary>
    public bool EnableSmtpProbe { get; set; } = false;

    [Range(1, 3600)]
    public int SmtpProbePollSeconds { get; set; } = 20;

    /// <summary>Leads probed per tick — deliberately much smaller than <see cref="BatchSize"/>.</summary>
    [Range(1, 50)]
    public int SmtpProbeBatchSize { get; set; } = 3;

    [Range(1000, 30000)]
    public int SmtpProbeTimeoutMs { get; set; } = 8000;

    /// <summary>
    /// Probes against any one mail domain per rolling hour, across every lead
    /// that happens to share it — the actual guard against hammering one
    /// provider's mail servers, since a batch drawn from real leads will often
    /// contain several addresses at the same domain.
    /// </summary>
    [Range(1, 500)]
    public int SmtpProbeMaxPerHourPerDomain { get; set; } = 15;

    /// <summary>
    /// How many times a 4xx (temporary failure) reply is given a fresh
    /// attempt on a later tick before the address is left at whatever the
    /// DNS-based result already said. Counted in <c>Business.EmailRetryCount</c>.
    /// Bounded so a server that is permanently, rather than transiently, slow
    /// or overloaded doesn't get probed forever.
    /// </summary>
    [Range(0, 20)]
    public int SmtpProbeMaxRetries { get; set; } = 2;

    // --- Bounce processing: watch the real outbound mailbox for real bounces -
    // Read-only against IMAP — never sends anything, so it carries none of
    // EnableBounceCheck's abuse-detection risk below. Off by default anyway,
    // consistent with every other mailbox-touching feature in this file, and
    // because it's meaningless without BounceCheckUsername/AppPassword (reused
    // here rather than duplicated — see those fields' own remarks) configured.

    /// <summary>Enables scanning the outbound mailbox for delivery-failure notices against real campaign sends. Off by default.</summary>
    public bool EnableBounceProcessing { get; set; } = false;

    [Range(30, 3600)]
    public int BounceProcessorPollSeconds { get; set; } = 300;

    /// <summary>
    /// On first run (no watermark recorded yet), how far back to look rather
    /// than scanning the mailbox's entire history.
    /// </summary>
    [Range(1, 30)]
    public int BounceProcessorLookbackDays { get; set; } = 7;

    // --- Bounce check: send a real probe email, watch a real inbox ----------
    // Opt-in, and capped well under Gmail's consumer sending limit (~500/day)
    // with margin — this pass is functionally a small outbound campaign, and
    // an account that trips abuse detection stops working entirely, which is a
    // worse outcome than a lower daily cap.

    /// <summary>Enables sending probe emails and scanning for bounces. Off by default.</summary>
    public bool EnableBounceCheck { get; set; } = false;

    [Range(30, 3600)]
    public int BounceCheckPollSeconds { get; set; } = 180;

    [Range(1, 2000)]
    public int BounceCheckMaxPerDay { get; set; } = 400;

    /// <summary>Spreads the daily cap across the day rather than bursting it in one tick.</summary>
    [Range(1, 500)]
    public int BounceCheckMaxPerHour { get; set; } = 40;

    /// <summary>
    /// How long a sent probe waits for a bounce before it is treated as
    /// confirmed deliverable. Most bounces (mailbox does not exist, domain
    /// rejects) arrive within minutes to a few hours; this window also absorbs
    /// greylisting delays.
    /// </summary>
    [Range(1, 168)]
    public int BounceCheckWaitHours { get; set; } = 48;

    /// <summary>Only leads at or above this confidence are worth spending a bounce-check send on.</summary>
    [Range(0, 100)]
    public int BounceCheckMinConfidence { get; set; } = 50;

    /// <summary>
    /// SMTP host used to send the probe email. Gmail defaults shown; any
    /// account reachable over SMTP with an app password works.
    /// </summary>
    public string BounceCheckSmtpHost { get; set; } = "smtp.gmail.com";

    [Range(1, 65535)]
    public int BounceCheckSmtpPort { get; set; } = 587;

    /// <summary>
    /// IMAP host the bounce scanner reads from — normally the same mailbox the
    /// probe was sent from. Also what <see cref="EnableBounceProcessing"/>
    /// connects to; reused rather than duplicated since in practice this is
    /// the same Gmail account <c>Smtp</c> already sends real campaigns from.
    /// </summary>
    public string BounceCheckImapHost { get; set; } = "imap.gmail.com";

    [Range(1, 65535)]
    public int BounceCheckImapPort { get; set; } = 993;

    /// <summary>
    /// The mailbox address probes are sent from and bounces are read back
    /// from — and, when <see cref="EnableBounceProcessing"/> is on, the same
    /// mailbox real campaign bounces are read back from too.
    /// </summary>
    public string BounceCheckUsername { get; set; } = string.Empty;

    /// <summary>
    /// An app password, not the account password — Gmail (and most providers)
    /// require this for SMTP/IMAP access once 2-step verification is on.
    /// <para>
    /// A secret: supply it through user-secrets, an environment variable
    /// (<c>EmailValidation__BounceCheckAppPassword</c>) or the host's
    /// configuration store — never <c>appsettings.json</c>, which is committed.
    /// </para>
    /// </summary>
    public string BounceCheckAppPassword { get; set; } = string.Empty;

    public bool BounceCheckIsConfigured =>
        !string.IsNullOrWhiteSpace(BounceCheckUsername) && !string.IsNullOrWhiteSpace(BounceCheckAppPassword);
}
