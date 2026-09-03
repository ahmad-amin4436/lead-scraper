using System.Text;
using LeadMine.Domain.Enums;
using LeadMine.Infrastructure.Scraping.Verification;
using MimeKit;

namespace LeadMine.Tests.Verification;

public class DsnMessageParserTests
{
    // --- Well-formed RFC 3464 structured DSN -----------------------------------

    [Fact]
    public void TryParseStructured_WellFormedDsn_ExtractsAllFields()
    {
        var message = LoadMessage(StructuredDsn(
            finalRecipient: "recipient@example.com",
            status: "5.1.1",
            diagnosticCode: "smtp; 550 5.1.1 User unknown",
            remoteMta: "dns; mx.recipient-domain.com"));

        var parsed = DsnMessageParser.TryParseStructured(message);

        Assert.NotNull(parsed);
        Assert.Equal("recipient@example.com", parsed!.FinalRecipient);
        Assert.Equal("failed", parsed.Action);
        Assert.Equal("5.1.1", parsed.Status);
        Assert.Equal("smtp; 550 5.1.1 User unknown", parsed.DiagnosticCode);
        Assert.Equal("dns; mx.recipient-domain.com", parsed.RemoteMta);
        Assert.Equal(550, parsed.SmtpCode);
        Assert.Equal("5.1.1", parsed.DsnCode);
        Assert.True(parsed.IsStructured);
    }

    [Fact]
    public void TryParseStructured_DiagnosticCodeMissing_FallsBackToStatusForCodes()
    {
        var message = LoadMessage(StructuredDsn(
            finalRecipient: "recipient@example.com",
            status: "4.2.2",
            diagnosticCode: null,
            remoteMta: null));

        var parsed = DsnMessageParser.TryParseStructured(message);

        Assert.NotNull(parsed);
        Assert.Equal("4.2.2", parsed!.DsnCode);
        Assert.Null(parsed.DiagnosticCode);
    }

    [Fact]
    public void TryParseStructured_DeliveredNotFailed_ReturnsNull()
    {
        // Action: delivered — a success DSN, not a bounce. Must not be mistaken for one.
        var raw = StructuredDsn("recipient@example.com", "2.1.5", "smtp; 250 OK", null, action: "delivered");
        var message = LoadMessage(raw);

        Assert.Null(DsnMessageParser.TryParseStructured(message));
    }

    [Fact]
    public void TryParseStructured_NonDsnMessage_ReturnsNull()
    {
        var message = LoadMessage(PlainMessage("Hello", "Just a normal email, nothing to see here."));

        Assert.Null(DsnMessageParser.TryParseStructured(message));
    }

    [Fact]
    public void TryParseStructured_DsnPartWithNoRecipientFields_DoesNotThrow_ReturnsNull()
    {
        // Valid MIME, valid multipart/report — but the delivery-status part is
        // missing every recipient field a real DSN would carry. Parsing must
        // degrade to "no bounce found" here, never throw and take down a
        // whole scan tick over one oddly-formed message.
        var raw = """
            From: Mail Delivery Subsystem <mailer-daemon@example.com>
            To: sender@leadmine.local
            Subject: Delivery Status Notification (Failure)
            MIME-Version: 1.0
            Content-Type: multipart/report; report-type=delivery-status; boundary="BOUNDARY"

            --BOUNDARY
            Content-Type: text/plain; charset=UTF-8

            Something went wrong, no further detail.

            --BOUNDARY
            Content-Type: message/delivery-status

            Reporting-MTA: dns; mx.leadmine.local

            Action: failed

            --BOUNDARY--
            """.ReplaceLineEndings("\r\n");

        var message = LoadMessage(raw);

        var exception = Record.Exception(() => DsnMessageParser.TryParseStructured(message));

        Assert.Null(exception);
        Assert.Null(DsnMessageParser.TryParseStructured(message));
    }

    [Fact]
    public void TryFindOriginalMessageId_MalformedEmbeddedPart_DoesNotThrow()
    {
        // message/rfc822 content-type, but the "embedded message" is garbage —
        // must not throw walking it, just report "no id found".
        var raw = """
            From: Mail Delivery Subsystem <mailer-daemon@example.com>
            To: sender@leadmine.local
            Subject: Delivery Status Notification (Failure)
            MIME-Version: 1.0
            Content-Type: multipart/report; report-type=delivery-status; boundary="BOUNDARY"

            --BOUNDARY
            Content-Type: text/plain; charset=UTF-8

            body

            --BOUNDARY
            Content-Type: message/rfc822

            not a valid embedded message at all, no headers here

            --BOUNDARY--
            """.ReplaceLineEndings("\r\n");

        var message = LoadMessage(raw);

        var exception = Record.Exception(() => DsnMessageParser.TryFindOriginalMessageId(message));

        Assert.Null(exception);
    }

    // --- Original message-id extraction (message/rfc822 attachment) -----------

    [Fact]
    public void TryFindOriginalMessageId_EmbeddedRfc822Part_ReturnsItsMessageId()
    {
        var message = LoadMessage(StructuredDsn(
            "recipient@example.com", "5.1.1", "smtp; 550 5.1.1 User unknown", null,
            originalMessageId: "<original-abc123@leadmine.local>"));

        var id = DsnMessageParser.TryFindOriginalMessageId(message);

        // MimeKit's MessageId getter returns the bare id, without the angle brackets.
        Assert.Equal("original-abc123@leadmine.local", id);
    }

    [Fact]
    public void TryFindOriginalMessageId_NoEmbeddedMessage_ReturnsNull()
    {
        var message = LoadMessage(PlainMessage("Hello", "No attachment here."));

        Assert.Null(DsnMessageParser.TryFindOriginalMessageId(message));
    }

    [Fact]
    public void TryFindOriginalMessageId_TextRfc822HeadersPart_ExtractsMessageId()
    {
        // Gmail's shape for its own "message blocked" self-rejection: no full
        // message/rfc822 attachment (nothing to attach — it never left
        // Gmail), just the original headers as text/rfc822-headers.
        var message = LoadMessage(DsnWithRfc822HeadersPart("<abc123@leadmine.local>"));

        var id = DsnMessageParser.TryFindOriginalMessageId(message);

        Assert.Equal("abc123@leadmine.local", id);
    }

    // --- Gmail's real "Message blocked" shape (policy rejection, X-Original-Message-ID) --

    [Fact]
    public void TryParseStructured_GmailMessageBlocked_IsPolicyRejectionWithOriginalMessageId()
    {
        // The exact shape confirmed live: Status: 5.7.1, a Diagnostic-Code with
        // no bare SMTP code at all (just prose + a support link), and the
        // original id only in X-Original-Message-ID since nothing was attached.
        var message = LoadMessage(GmailMessageBlockedDsn(
            recipient: "someone@example.com",
            originalMessageId: "<JHXERULR7UU4.YG4MCX8LC5W53@win-ng2j237qd6j>"));

        var parsed = DsnMessageParser.TryParseStructured(message);

        Assert.NotNull(parsed);
        Assert.Equal("someone@example.com", parsed!.FinalRecipient);
        Assert.Equal("5.7.1", parsed.Status);
        Assert.Equal("JHXERULR7UU4.YG4MCX8LC5W53@win-ng2j237qd6j", parsed.OriginalMessageId);

        var smtpClass = SmtpResponseClassifier.Classify(parsed.SmtpCode ?? 0, parsed.DsnCode);
        Assert.Equal(SmtpResponseClass.PolicyRejection, smtpClass);
        Assert.Equal(EmailBounceType.PolicyRejection, SmtpResponseClassifier.ToBounceType(smtpClass));
    }

    // --- Heuristic fallback for unstructured bounces (the real Gmail "Address not found" shape) --

    [Fact]
    public void LooksLikeBounce_GmailAddressNotFoundStyle_IsDetected()
    {
        var message = LoadMessage(PlainMessageFrom(
            "Mail Delivery Subsystem", "mailer-daemon@googlemail.com",
            "Delivery Status Notification (Failure)",
            "Your message wasn't delivered to 927-2740info@franklinsbrewery.net because the address couldn't be found."));

        var looksLikeBounce = DsnMessageParser.LooksLikeBounce(message, out var reason);

        Assert.True(looksLikeBounce);
        Assert.Equal("Delivery Status Notification (Failure)", reason);
    }

    [Fact]
    public void LooksLikeBounce_OrdinaryMessage_IsNotDetected()
    {
        var message = LoadMessage(PlainMessage("Meeting tomorrow", "See you at 10am."));

        Assert.False(DsnMessageParser.LooksLikeBounce(message, out _));
    }

    [Fact]
    public void FindAddressInText_LocatesPendingAddressMentionedInBody()
    {
        var message = LoadMessage(PlainMessageFrom(
            "Mail Delivery Subsystem", "mailer-daemon@googlemail.com",
            "Address not found",
            "Your message wasn't delivered to 927-2740info@franklinsbrewery.net because the address couldn't be found."));

        var candidates = new[] { "someone-else@example.com", "927-2740info@franklinsbrewery.net" };

        var found = DsnMessageParser.FindAddressInText(message, candidates);

        Assert.Equal("927-2740info@franklinsbrewery.net", found);
    }

    [Fact]
    public void FindAddressInText_NoCandidateMentioned_ReturnsNull()
    {
        var message = LoadMessage(PlainMessageFrom(
            "Mail Delivery Subsystem", "mailer-daemon@googlemail.com", "Address not found",
            "Some other address entirely bounced."));

        var found = DsnMessageParser.FindAddressInText(message, new[] { "nobody@example.com" });

        Assert.Null(found);
    }

    [Fact]
    public void BuildHeuristicBounce_PullsSmtpAndDsnCodesFromBodyText()
    {
        var message = LoadMessage(PlainMessageFrom(
            "Mail Delivery Subsystem", "mailer-daemon@googlemail.com", "Address not found",
            "Technical details: smtp; 550 5.1.1 User unknown"));

        var bounce = DsnMessageParser.BuildHeuristicBounce("recipient@example.com", "Address not found", message);

        Assert.Equal("recipient@example.com", bounce.FinalRecipient);
        Assert.False(bounce.IsStructured);
        Assert.Equal(550, bounce.SmtpCode);
        Assert.Equal("5.1.1", bounce.DsnCode);
    }

    // --- Test fixtures -----------------------------------------------------------

    private static MimeMessage LoadMessage(string raw)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(raw));
        return MimeMessage.Load(stream);
    }

    private static string PlainMessage(string subject, string body) =>
        PlainMessageFrom("Someone", "someone@example.com", subject, body);

    private static string PlainMessageFrom(string fromName, string fromAddress, string subject, string body) =>
        $"""
         From: {fromName} <{fromAddress}>
         To: recipient@example.com
         Subject: {subject}
         MIME-Version: 1.0
         Content-Type: text/plain; charset=UTF-8

         {body}
         """.ReplaceLineEndings("\r\n");

    /// <summary>
    /// Builds a realistic multipart/report (RFC 3464) DSN — a human-readable
    /// part, a message/delivery-status part with the fields under test, and an
    /// optional embedded message/rfc822 part carrying the original Message-Id.
    /// </summary>
    private static string StructuredDsn(
        string finalRecipient,
        string status,
        string? diagnosticCode,
        string? remoteMta,
        string action = "failed",
        string? originalMessageId = null)
    {
        var diagnosticLine = diagnosticCode is null ? "" : $"Diagnostic-Code: {diagnosticCode}\r\n";
        var remoteMtaLine = remoteMta is null ? "" : $"Remote-MTA: {remoteMta}\r\n";

        var originalMessagePart = originalMessageId is null ? "" : $"""

            --BOUNDARY
            Content-Type: message/rfc822

            From: sender@leadmine.local
            To: {finalRecipient}
            Subject: Original message
            Message-Id: {originalMessageId}
            Content-Type: text/plain

            Original body.

            """;

        return $"""
            From: Mail Delivery Subsystem <mailer-daemon@example.com>
            To: sender@leadmine.local
            Subject: Delivery Status Notification (Failure)
            MIME-Version: 1.0
            Content-Type: multipart/report; report-type=delivery-status; boundary="BOUNDARY"

            --BOUNDARY
            Content-Type: text/plain; charset=UTF-8

            Delivery to the following recipient failed permanently.

            --BOUNDARY
            Content-Type: message/delivery-status

            Reporting-MTA: dns; mx.leadmine.local
            Arrival-Date: Thu, 3 Sep 2026 09:00:00 +0000

            Final-Recipient: rfc822; {finalRecipient}
            Action: {action}
            Status: {status}
            {remoteMtaLine}{diagnosticLine}
            {originalMessagePart}
            --BOUNDARY--
            """.ReplaceLineEndings("\r\n");
    }

    /// <summary>A DSN whose only trace of the original message is a bare text/rfc822-headers part (no message/delivery-status at all) — the fallback path.</summary>
    private static string DsnWithRfc822HeadersPart(string originalMessageId) =>
        $"""
         From: Mail Delivery Subsystem <mailer-daemon@example.com>
         To: sender@leadmine.local
         Subject: Delivery Status Notification (Failure)
         MIME-Version: 1.0
         Content-Type: multipart/report; report-type=delivery-status; boundary="BOUNDARY"

         --BOUNDARY
         Content-Type: text/plain; charset=UTF-8

         Delivery failed.

         --BOUNDARY
         Content-Type: text/rfc822-headers

         From: sender@leadmine.local
         To: recipient@example.com
         Subject: Original message
         Message-ID: {originalMessageId}

         --BOUNDARY--
         """.ReplaceLineEndings("\r\n");

    /// <summary>
    /// The exact shape confirmed live against a real "Message blocked" bounce
    /// from googlemail.com: Status 5.7.1, a Diagnostic-Code that's prose plus
    /// a support link (no bare SMTP code in it at all), and the original
    /// Message-Id only in the per-message X-Original-Message-ID field —
    /// nothing is attached, since the message never left Gmail's network.
    /// </summary>
    private static string GmailMessageBlockedDsn(string recipient, string originalMessageId) =>
        $"""
         From: Mail Delivery Subsystem <mailer-daemon@googlemail.com>
         To: sender@leadmine.local
         Subject: Delivery Status Notification (Failure)
         MIME-Version: 1.0
         Content-Type: multipart/report; report-type=delivery-status; boundary="BOUNDARY"

         --BOUNDARY
         Content-Type: text/plain; charset=UTF-8

         ** Message blocked **

         Your message to {recipient} has been blocked.

         --BOUNDARY
         Content-Type: message/delivery-status

         Reporting-MTA: dns; googlemail.com
         Received-From-MTA: dns; sender@leadmine.local
         Arrival-Date: Thu, 03 Sep 2026 03:24:05 -0700 (PDT)
         X-Original-Message-ID: {originalMessageId}

         Final-Recipient: rfc822; {recipient}
         Action: failed
         Status: 5.7.1
         Diagnostic-Code: smtp; Message rejected. For more information, go to https://support.google.com/mail/answer/69585
         Last-Attempt-Date: Thu, 03 Sep 2026 03:24:07 -0700 (PDT)

         --BOUNDARY--
         """.ReplaceLineEndings("\r\n");
}
