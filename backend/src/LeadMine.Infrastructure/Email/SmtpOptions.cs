using System.ComponentModel.DataAnnotations;

namespace LeadMine.Infrastructure.Email;

/// <summary>
/// Outbound SMTP configuration.
/// <para>
/// Supplied through user-secrets or environment variables — never
/// <c>appsettings.json</c>, because <see cref="Password"/> is a live credential
/// that would otherwise land in source control.
/// </para>
/// </summary>
public sealed class SmtpOptions
{
    public const string SectionName = "Smtp";

    [Required]
    public string Host { get; set; } = string.Empty;

    [Range(1, 65535)]
    public int Port { get; set; } = 587;

    /// <summary>
    /// True for implicit TLS (port 465). Port 587 uses STARTTLS instead, which
    /// is negotiated after connecting — so this stays false there despite the
    /// connection still being encrypted.
    /// </summary>
    public bool Secure { get; set; }

    [Required]
    public string User { get; set; } = string.Empty;

    [Required]
    public string Password { get; set; } = string.Empty;

    /// <summary>Envelope From. Defaults to <see cref="User"/> when unset.</summary>
    public string? FromAddress { get; set; }

    public string FromName { get; set; } = "LeadMine";

    /// <summary>Address that receives replies, if different from the sender.</summary>
    public string? ReplyTo { get; set; }

    /// <summary>Silently BCC'd on every send, for compliance archiving.</summary>
    public string? ArchiveBcc { get; set; }

    [Range(1000, 300000)]
    public int TimeoutMs { get; set; } = 30000;

    /// <summary>
    /// Pause between messages in a batch. Shared providers throttle or blacklist
    /// bursts, so pacing protects sender reputation.
    /// </summary>
    [Range(0, 60000)]
    public int DelayBetweenSendsMs { get; set; } = 1200;

    /// <summary>Per-user daily cap. Zero disables the limit.</summary>
    [Range(0, 100000)]
    public int DailySendLimitPerUser { get; set; } = 500;

    /// <summary>Recipients per send request.</summary>
    [Range(1, 500)]
    public int MaxRecipientsPerRequest { get; set; } = 100;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Host) &&
        !string.IsNullOrWhiteSpace(User) &&
        !string.IsNullOrWhiteSpace(Password);

    public string EffectiveFrom =>
        string.IsNullOrWhiteSpace(FromAddress) ? User : FromAddress!;
}
