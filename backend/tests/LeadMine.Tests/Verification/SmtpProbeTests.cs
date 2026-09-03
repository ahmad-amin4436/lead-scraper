using LeadMine.Domain.Enums;
using LeadMine.Infrastructure.Scraping.Verification;
using Microsoft.Extensions.Logging.Abstractions;

namespace LeadMine.Tests.Verification;

/// <summary>
/// Exercises <see cref="SmtpProbe"/> against a local, scripted fake SMTP
/// server (<see cref="FakeSmtpServer"/>) rather than a real mail host — real
/// socket I/O, real response parsing, real retry timing, just no network
/// dependency and no risk to a real mailbox's reputation.
/// </summary>
public class SmtpProbeTests
{
    private static readonly IReadOnlyList<string> LoopbackHost = ["127.0.0.1"];

    private static SmtpProbe CreateProbe(int port, TimeSpan? retryDelay = null) =>
        new(NullLogger<SmtpProbe>.Instance)
        {
            Port = port,
            TempFailureRetryDelay = retryDelay ?? TimeSpan.FromMilliseconds(50),
        };

    [Fact]
    public async Task ProbeAsync_2xxResponse_IsAccepted()
    {
        await using var server = FakeSmtpServer.Start("250 2.1.5 OK");
        var probe = CreateProbe(server.Port);

        var outcome = await probe.ProbeAsync(LoopbackHost, "someone@example.com", 3000, CancellationToken.None);

        Assert.Equal(SmtpProbeResult.Accepted, outcome.Result);
        Assert.Equal(SmtpResponseClass.Accepted, outcome.Class);
        Assert.Equal(250, outcome.SmtpCode);
    }

    [Fact]
    public async Task ProbeAsync_5xxHardRejection_IsRejected()
    {
        await using var server = FakeSmtpServer.Start("550 5.1.1 User unknown");
        var probe = CreateProbe(server.Port);

        var outcome = await probe.ProbeAsync(LoopbackHost, "nobody@example.com", 3000, CancellationToken.None);

        Assert.Equal(SmtpProbeResult.Rejected, outcome.Result);
        Assert.Equal(SmtpResponseClass.HardFailure, outcome.Class);
        Assert.Equal(550, outcome.SmtpCode);
        Assert.Equal("5.1.1", outcome.DsnCode);
    }

    [Fact]
    public async Task ProbeAsync_4xxThenClearsOnRetry_IsAccepted()
    {
        // First connection gets a temp failure, the retry's fresh connection
        // gets a clean accept — proves the bounded retry actually happens and
        // that its result is the one that counts.
        await using var server = FakeSmtpServer.Start("450 4.2.1 mailbox temporarily unavailable", "250 OK");
        var probe = CreateProbe(server.Port);

        var outcome = await probe.ProbeAsync(LoopbackHost, "someone@example.com", 5000, CancellationToken.None);

        Assert.Equal(SmtpProbeResult.Accepted, outcome.Result);
        Assert.Equal(SmtpResponseClass.Accepted, outcome.Class);
    }

    [Fact]
    public async Task ProbeAsync_4xxStillFailingAfterRetry_IsInconclusiveTempFailure()
    {
        await using var server = FakeSmtpServer.Start(
            "450 4.2.1 mailbox temporarily unavailable",
            "421 4.3.2 service not available");
        var probe = CreateProbe(server.Port);

        var outcome = await probe.ProbeAsync(LoopbackHost, "someone@example.com", 5000, CancellationToken.None);

        // Never "invalid": a temp failure, even after the one controlled
        // retry, must stay inconclusive rather than becoming a hard verdict.
        Assert.Equal(SmtpProbeResult.Inconclusive, outcome.Result);
        Assert.Equal(SmtpResponseClass.TempFailure, outcome.Class);
    }

    [Fact]
    public async Task ProbeAsync_PolicyRejection_IsInconclusiveNeverInvalid()
    {
        await using var server = FakeSmtpServer.Start("550 5.7.1 message rejected due to policy");
        var probe = CreateProbe(server.Port);

        var outcome = await probe.ProbeAsync(LoopbackHost, "someone@example.com", 3000, CancellationToken.None);

        Assert.Equal(SmtpProbeResult.Inconclusive, outcome.Result);
        Assert.Equal(SmtpResponseClass.PolicyRejection, outcome.Class);
        Assert.Equal("5.7.1", outcome.DsnCode);
    }

    [Fact]
    public async Task ProbeAsync_MalformedResponse_IsInconclusive()
    {
        await using var server = FakeSmtpServer.Start("NOT-AN-SMTP-RESPONSE-AT-ALL");
        var probe = CreateProbe(server.Port);

        var outcome = await probe.ProbeAsync(LoopbackHost, "someone@example.com", 3000, CancellationToken.None);

        Assert.Equal(SmtpProbeResult.Inconclusive, outcome.Result);
        Assert.Equal(SmtpResponseClass.Inconclusive, outcome.Class);
    }

    [Fact]
    public async Task ProbeAsync_ServerNeverResponds_TimesOutAsInconclusive()
    {
        await using var server = FakeSmtpServer.StartSilent();
        var probe = CreateProbe(server.Port);

        var started = DateTime.UtcNow;
        var outcome = await probe.ProbeAsync(LoopbackHost, "someone@example.com", 800, CancellationToken.None);
        var elapsed = DateTime.UtcNow - started;

        Assert.Equal(SmtpProbeResult.Inconclusive, outcome.Result);
        // Must actually respect the timeout budget rather than hanging.
        Assert.True(elapsed < TimeSpan.FromSeconds(5), $"Probe took {elapsed} — timeout was not honored");
    }

    [Fact]
    public async Task ProbeAsync_ConnectionRefused_IsInconclusive()
    {
        // Nothing listening on this port at all.
        var probe = CreateProbe(1);

        var outcome = await probe.ProbeAsync(LoopbackHost, "someone@example.com", 2000, CancellationToken.None);

        Assert.Equal(SmtpProbeResult.Inconclusive, outcome.Result);
    }

    // --- Catch-all detection -----------------------------------------------------

    [Fact]
    public async Task IsCatchAllAsync_AcceptsRandomAddress_ReturnsTrue()
    {
        await using var server = FakeSmtpServer.Start("250 OK");
        var probe = CreateProbe(server.Port);

        var isCatchAll = await probe.IsCatchAllAsync(LoopbackHost, "example.com", 3000, CancellationToken.None);

        Assert.True(isCatchAll);
    }

    [Fact]
    public async Task IsCatchAllAsync_RejectsRandomAddress_ReturnsFalse()
    {
        await using var server = FakeSmtpServer.Start("550 5.1.1 User unknown");
        var probe = CreateProbe(server.Port);

        var isCatchAll = await probe.IsCatchAllAsync(LoopbackHost, "example.com", 3000, CancellationToken.None);

        Assert.False(isCatchAll);
    }

    [Fact]
    public async Task IsCatchAllAsync_Inconclusive_ReturnsNull()
    {
        await using var server = FakeSmtpServer.StartSilent();
        var probe = CreateProbe(server.Port);

        var isCatchAll = await probe.IsCatchAllAsync(LoopbackHost, "example.com", 500, CancellationToken.None);

        Assert.Null(isCatchAll);
    }
}
