using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using MimeKit.Text;

namespace LeadMine.Infrastructure.Email;

public sealed record OutboundMessage(
    string ToEmail,
    string ToName,
    string Subject,
    string BodyHtml,
    string BodyText,
    string? Cc = null,
    string? Bcc = null);

public sealed record SendOutcome(bool Succeeded, string? MessageId, string? Error);

public interface IEmailSender
{
    Task<SendOutcome> SendAsync(OutboundMessage message, CancellationToken ct = default);

    Task<SendOutcome> VerifyConnectionAsync(CancellationToken ct = default);

    bool IsConfigured { get; }
}

/// <summary>
/// MailKit-backed SMTP sender.
/// <para>
/// MailKit rather than <c>System.Net.Mail.SmtpClient</c>, which Microsoft
/// documents as obsolete for new work: it mishandles STARTTLS negotiation and
/// modern authentication, both of which this provider requires on port 587.
/// </para>
/// </summary>
public sealed class SmtpEmailSender(
    IOptions<SmtpOptions> options,
    ILogger<SmtpEmailSender> logger) : IEmailSender
{
    private readonly SmtpOptions _options = options.Value;

    public bool IsConfigured => _options.IsConfigured;

    public async Task<SendOutcome> SendAsync(OutboundMessage message, CancellationToken ct = default)
    {
        if (!IsConfigured)
        {
            return new SendOutcome(false, null, "SMTP is not configured.");
        }

        try
        {
            var mime = BuildMessage(message);

            using var client = new SmtpClient { Timeout = _options.TimeoutMs };

            await client.ConnectAsync(
                _options.Host,
                _options.Port,
                // Implicit TLS on 465; STARTTLS is negotiated on 587. Auto lets
                // MailKit pick correctly rather than hard-coding the wrong one.
                _options.Secure ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTlsWhenAvailable,
                ct);

            await client.AuthenticateAsync(_options.User, _options.Password, ct);

            var response = await client.SendAsync(mime, ct);
            await client.DisconnectAsync(quit: true, ct);

            return new SendOutcome(true, mime.MessageId ?? response, null);
        }
        catch (AuthenticationException ex)
        {
            logger.LogError(ex, "SMTP authentication failed for {User}", _options.User);
            return new SendOutcome(false, null, "SMTP authentication failed. Check the credentials.");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send email to {Recipient}", message.ToEmail);
            return new SendOutcome(false, null, ex.Message);
        }
    }

    /// <summary>
    /// Connects and authenticates without delivering anything, so credentials
    /// can be validated from the admin UI rather than by emailing a real lead.
    /// </summary>
    public async Task<SendOutcome> VerifyConnectionAsync(CancellationToken ct = default)
    {
        if (!IsConfigured)
        {
            return new SendOutcome(false, null, "SMTP is not configured.");
        }

        try
        {
            using var client = new SmtpClient { Timeout = _options.TimeoutMs };

            await client.ConnectAsync(
                _options.Host,
                _options.Port,
                _options.Secure ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTlsWhenAvailable,
                ct);

            await client.AuthenticateAsync(_options.User, _options.Password, ct);
            await client.DisconnectAsync(quit: true, ct);

            return new SendOutcome(true, null, null);
        }
        catch (Exception ex)
        {
            return new SendOutcome(false, null, ex.Message);
        }
    }

    private MimeMessage BuildMessage(OutboundMessage message)
    {
        var mime = new MimeMessage();

        mime.From.Add(new MailboxAddress(_options.FromName, _options.EffectiveFrom));
        mime.To.Add(MailboxAddress.Parse(message.ToEmail));

        if (!string.IsNullOrWhiteSpace(_options.ReplyTo))
        {
            mime.ReplyTo.Add(MailboxAddress.Parse(_options.ReplyTo));
        }

        AddAddresses(mime.Cc, message.Cc);
        AddAddresses(mime.Bcc, message.Bcc);
        AddAddresses(mime.Bcc, _options.ArchiveBcc);

        mime.Subject = message.Subject;

        // Multipart/alternative: a text part alongside the HTML materially
        // improves deliverability, since HTML-only mail scores as spam.
        var builder = new BodyBuilder
        {
            HtmlBody = message.BodyHtml,
            TextBody = string.IsNullOrWhiteSpace(message.BodyText)
                ? HtmlToPlainText(message.BodyHtml)
                : message.BodyText,
        };

        mime.Body = builder.ToMessageBody();
        return mime;
    }

    private static void AddAddresses(InternetAddressList list, string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return;

        foreach (var part in raw.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (MailboxAddress.TryParse(part.Trim(), out var address)) list.Add(address);
        }
    }

    /// <summary>Best-effort text fallback when a template supplies none.</summary>
    private static string HtmlToPlainText(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return string.Empty;

        try
        {
            var document = new HtmlAgilityPack.HtmlDocument();
            document.LoadHtml(html);

            foreach (var node in document.DocumentNode
                         .SelectNodes("//script|//style")?.ToList() ?? [])
            {
                node.Remove();
            }

            var text = HtmlAgilityPack.HtmlEntity.DeEntitize(document.DocumentNode.InnerText);
            return string.Join('\n',
                text.Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0));
        }
        catch
        {
            return new TextPart(TextFormat.Plain) { Text = html }.Text;
        }
    }
}
