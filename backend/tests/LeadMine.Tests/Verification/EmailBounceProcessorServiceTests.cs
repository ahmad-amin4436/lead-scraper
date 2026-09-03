using LeadMine.Domain.Entities;
using LeadMine.Domain.Enums;
using LeadMine.Infrastructure.Scraping.Verification;

namespace LeadMine.Tests.Verification;

/// <summary>
/// Covers <c>EmailBounceProcessorService.ApplyBounceToLead</c> — the
/// suppress/retry/review decision table each bounce type drives on a
/// <see cref="Business"/>. This is the part of the pipeline the user-facing
/// behavior actually hinges on: get this wrong and either a dead address
/// keeps being sent to, or a good one gets silently blocked.
/// </summary>
public class EmailBounceProcessorServiceTests
{
    [Fact]
    public void HardBounce_SuppressesAndInvalidatesTheLead()
    {
        var business = new Business { Email = "nobody@example.com", EmailStatus = EmailStatus.Valid, EmailConfidence = 90 };

        EmailBounceProcessorService.ApplyBounceToLead(
            business, EmailBounceType.HardBounce, 550, "5.1.1", "User unknown", DateTimeOffset.UtcNow);

        Assert.Equal(EmailBounceStatus.Suppressed, business.EmailBounceStatus);
        Assert.Equal(EmailStatus.Invalid, business.EmailStatus);
        Assert.Equal(0, business.EmailConfidence);
        Assert.Equal(EmailBounceType.HardBounce, business.EmailBounceType);
        Assert.Equal("User unknown", business.EmailBounceReason);
        Assert.Equal(550, business.EmailSmtpCode);
        Assert.Equal("5.1.1", business.EmailDsnCode);
    }

    [Fact]
    public void SoftBounce_SchedulesRetry_ButDoesNotTouchEmailStatus()
    {
        var business = new Business { Email = "someone@example.com", EmailStatus = EmailStatus.Valid, EmailRetryCount = 0 };

        EmailBounceProcessorService.ApplyBounceToLead(
            business, EmailBounceType.SoftBounce, 450, "4.2.1", "Mailbox temporarily full", DateTimeOffset.UtcNow);

        Assert.Equal(EmailBounceStatus.RetryScheduled, business.EmailBounceStatus);
        // A temporary problem is not evidence the address doesn't exist —
        // the whole point of distinguishing soft from hard bounces.
        Assert.Equal(EmailStatus.Valid, business.EmailStatus);
        Assert.Equal(1, business.EmailRetryCount);
    }

    [Fact]
    public void SoftBounce_IncrementsRetryCountAcrossRepeatedBounces()
    {
        var business = new Business { Email = "someone@example.com", EmailRetryCount = 2 };

        EmailBounceProcessorService.ApplyBounceToLead(
            business, EmailBounceType.SoftBounce, 421, "4.3.2", "Service busy", DateTimeOffset.UtcNow);

        Assert.Equal(3, business.EmailRetryCount);
    }

    [Fact]
    public void PolicyRejection_NeedsReview_AndNeverAutoInvalidates()
    {
        var business = new Business { Email = "someone@example.com", EmailStatus = EmailStatus.Valid };

        EmailBounceProcessorService.ApplyBounceToLead(
            business, EmailBounceType.PolicyRejection, 550, "5.7.1", "Message blocked by policy", DateTimeOffset.UtcNow);

        Assert.Equal(EmailBounceStatus.UnderReview, business.EmailBounceStatus);
        Assert.Equal(EmailStatus.Valid, business.EmailStatus);
    }

    [Fact]
    public void UnknownBounce_AlsoRoutesToReview_NotSuppression()
    {
        var business = new Business { Email = "someone@example.com", EmailStatus = EmailStatus.Valid };

        EmailBounceProcessorService.ApplyBounceToLead(
            business, EmailBounceType.Unknown, 552, null, "Mailbox full", DateTimeOffset.UtcNow);

        Assert.Equal(EmailBounceStatus.UnderReview, business.EmailBounceStatus);
        Assert.Equal(EmailStatus.Valid, business.EmailStatus);
    }

    [Fact]
    public void LastBounceAt_IsAlwaysStamped()
    {
        var business = new Business();
        var now = DateTimeOffset.UtcNow;

        EmailBounceProcessorService.ApplyBounceToLead(business, EmailBounceType.SoftBounce, 450, "4.2.1", "busy", now);

        Assert.Equal(now, business.EmailLastBounceAt);
    }
}
