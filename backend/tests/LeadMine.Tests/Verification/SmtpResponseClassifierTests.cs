using LeadMine.Domain.Enums;
using LeadMine.Infrastructure.Scraping.Verification;

namespace LeadMine.Tests.Verification;

public class SmtpResponseClassifierTests
{
    // --- 2xx: accepted, but never proof of existence -------------------------

    [Theory]
    [InlineData(250)]
    [InlineData(251)]
    [InlineData(252)]
    public void Classify_2xx_IsAccepted(int code)
    {
        Assert.Equal(SmtpResponseClass.Accepted, SmtpResponseClassifier.Classify(code, null));
    }

    // --- 4xx: temporary, worth a controlled retry -----------------------------

    [Theory]
    [InlineData(421)]
    [InlineData(450)]
    [InlineData(451)]
    [InlineData(452)]
    [InlineData(400)]
    [InlineData(499)]
    public void Classify_4xx_IsTempFailure(int code)
    {
        Assert.Equal(SmtpResponseClass.TempFailure, SmtpResponseClassifier.Classify(code, null));
    }

    [Fact]
    public void Classify_4xxWithDsnCode_IsStillTempFailure()
    {
        // A 4.x.x extended code paired with a 4xx reply — the class digit
        // agrees with the bare code, so this must not somehow read as a
        // policy or hard failure.
        Assert.Equal(SmtpResponseClass.TempFailure, SmtpResponseClassifier.Classify(450, "4.2.1"));
    }

    // --- 5xx hard failures: the address itself doesn't exist ------------------

    [Theory]
    [InlineData(550)]
    [InlineData(551)]
    [InlineData(553)]
    public void Classify_BareHardFailureCodes_NoDsnCode_IsHardFailure(int code)
    {
        Assert.Equal(SmtpResponseClass.HardFailure, SmtpResponseClassifier.Classify(code, null));
    }

    [Theory]
    [InlineData("5.1.1")]
    [InlineData("5.1.10")]
    [InlineData("5.1.3")]
    public void Classify_HardFailureDsnCodes_IsHardFailure(string dsnCode)
    {
        // Even paired with a code outside {550,551,553} — a 550-adjacent 5xx
        // carrying one of these extended codes is still "no such mailbox".
        Assert.Equal(SmtpResponseClass.HardFailure, SmtpResponseClassifier.Classify(554, dsnCode));
    }

    [Fact]
    public void Classify_51x_MailboxExplicitlyValid_IsNotHardFailure()
    {
        // 5.1.5 means "Destination mailbox address valid" — the opposite of a
        // hard failure. A naive "starts with 5.1" rule would get this wrong.
        Assert.NotEqual(SmtpResponseClass.HardFailure, SmtpResponseClassifier.Classify(550, "5.1.5"));
    }

    // --- 5.7.x: policy/security, never a statement about existence -----------

    [Theory]
    [InlineData(550, "5.7.1")]
    [InlineData(554, "5.7.1")]
    [InlineData(571, "5.7.1")]
    public void Classify_57x_IsPolicyRejection_EvenWithHardFailureSmtpCode(int smtpCode, string dsnCode)
    {
        // The critical ordering case: 550 is in the bare hard-failure set, but
        // a 5.7.x extended code must win — this is a policy/spam decision,
        // not "no such mailbox".
        Assert.Equal(SmtpResponseClass.PolicyRejection, SmtpResponseClassifier.Classify(smtpCode, dsnCode));
    }

    [Theory]
    [InlineData("5.7.1")]
    [InlineData("5.7.26")]
    [InlineData("5.7.0")]
    public void Classify_Any57xSubcode_IsPolicyRejection(string dsnCode)
    {
        Assert.Equal(SmtpResponseClass.PolicyRejection, SmtpResponseClassifier.Classify(550, dsnCode));
    }

    // --- Everything else: a real rejection, but not a confident verdict ------

    [Theory]
    [InlineData(552)] // mailbox full / quota — permanent-coded, but not "doesn't exist"
    [InlineData(554)] // generic transaction failed, no DSN code given
    [InlineData(500)] // syntax error
    public void Classify_OtherPermanentFailures_IsInconclusive(int code)
    {
        Assert.Equal(SmtpResponseClass.Inconclusive, SmtpResponseClassifier.Classify(code, null));
    }

    [Fact]
    public void Classify_UnparseableOrZeroCode_IsInconclusive()
    {
        Assert.Equal(SmtpResponseClass.Inconclusive, SmtpResponseClassifier.Classify(0, null));
    }

    // --- ToBounceType mapping --------------------------------------------------

    [Theory]
    [InlineData(SmtpResponseClass.HardFailure, EmailBounceType.HardBounce)]
    [InlineData(SmtpResponseClass.TempFailure, EmailBounceType.SoftBounce)]
    [InlineData(SmtpResponseClass.PolicyRejection, EmailBounceType.PolicyRejection)]
    [InlineData(SmtpResponseClass.Inconclusive, EmailBounceType.Unknown)]
    [InlineData(SmtpResponseClass.Accepted, EmailBounceType.Unknown)]
    public void ToBounceType_MapsEachClassCorrectly(SmtpResponseClass smtpClass, EmailBounceType expected)
    {
        Assert.Equal(expected, SmtpResponseClassifier.ToBounceType(smtpClass));
    }

    // --- Extraction helpers -----------------------------------------------------

    [Theory]
    [InlineData("550 5.1.1 User unknown", "5.1.1")]
    [InlineData("smtp; 550 5.1.1 User unknown", "5.1.1")]
    [InlineData("The following address failed: user@example.com 5.7.1 blocked", "5.7.1")]
    [InlineData("421-4.3.2 Service not available", "4.3.2")]
    public void TryExtractDsnCode_FindsCodeInText(string text, string expected)
    {
        var found = SmtpResponseClassifier.TryExtractDsnCode(text, out var code);

        Assert.True(found);
        Assert.Equal(expected, code);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("no codes in here at all")]
    [InlineData("just a version number 1.2.3, not a status code")]
    public void TryExtractDsnCode_MalformedOrAbsent_ReturnsFalse(string? text)
    {
        var found = SmtpResponseClassifier.TryExtractDsnCode(text, out var code);

        Assert.False(found);
        Assert.Null(code);
    }

    [Fact]
    public void TryExtractDsnCode_VersionLikeNumber_IsNotMisreadAsDsnCode()
    {
        // "1.2.3" starts with a digit that isn't 2/4/5 — must not match.
        var found = SmtpResponseClassifier.TryExtractDsnCode("version 1.2.3 rejected", out _);
        Assert.False(found);
    }

    [Theory]
    [InlineData("smtp; 550 5.1.1 User unknown", 550)]
    [InlineData("The server said: 421 Service busy", 421)]
    public void TryExtractSmtpCode_FindsCodeInText(string text, int expected)
    {
        var found = SmtpResponseClassifier.TryExtractSmtpCode(text, out var code);

        Assert.True(found);
        Assert.Equal(expected, code);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("250 OK, accepted for delivery")] // 2xx deliberately excluded — never opens a diagnostic line
    public void TryExtractSmtpCode_MalformedOrExcluded_ReturnsFalse(string? text)
    {
        var found = SmtpResponseClassifier.TryExtractSmtpCode(text, out var code);

        Assert.False(found);
        Assert.Equal(0, code);
    }
}
