using System.Text.Json;
using LeadMine.Application.Authorization;
using LeadMine.Application.Common;
using LeadMine.Application.DTOs;
using LeadMine.Application.Interfaces;
using LeadMine.Domain.Enums;
using LeadMine.Infrastructure.Authorization;
using LeadMine.Infrastructure.Email;
using LeadMine.Infrastructure.Scraping.Verification;
using LeadMine.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LeadMine.Api.Controllers;

/// <summary>Sending mail to leads, and the history of what was sent.</summary>
[Authorize]
[Route("api/email")]
public sealed class EmailController(
    IEmailService email,
    EmailValidationPipeline validation,
    ISearchJobService jobs,
    ICurrentUser currentUser) : ApiControllerBase
{
    /// <summary>
    /// Sends an approved preset to one or more leads, synchronously, inline in
    /// this request. Kept for callers that genuinely want to block on a tiny
    /// batch, but the frontend no longer calls this — see the
    /// <c>send-jobs</c> endpoints below. Confirmed live: this blocking form
    /// returned <c>504</c> from the Next.js proxy (a standard Netlify
    /// Function, ~10-26s) once a real batch's paced sequential sends ran long
    /// enough, the same class of problem <c>enrich-jobs</c> already solved
    /// for LinkedIn enrichment.
    /// </summary>
    [HttpPost("send")]
    [HasPermission(Permissions.Email.Send)]
    [ProducesResponseType(typeof(SendEmailResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<SendEmailResultDto>> Send(SendEmailRequest request, CancellationToken ct)
        => FromResult(await email.SendAsync(request, ct));

    /// <summary>Queues a send batch. Returns immediately — a worker executes it. This is what the frontend uses.</summary>
    [HttpPost("send-jobs")]
    [HasPermission(Permissions.Email.Send)]
    [ProducesResponseType(typeof(SearchJobDto), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<SearchJobDto>> CreateSendJob(SendEmailRequest request, CancellationToken ct)
    {
        if (currentUser.UserId is null) return Unauthorized();

        var result = await jobs.CreateAsync(new CreateSearchJobRequest
        {
            Kind = JobKind.EmailSend,
            RequestJson = JsonSerializer.Serialize(request),
            TotalTasks = request.BusinessIds.Count,
        }, ct);

        if (!result.Succeeded) return Problem(result);

        return Accepted(result.Value);
    }

    /// <summary>The live snapshot the Send Email page polls.</summary>
    [HttpGet("send-jobs/{id:guid}")]
    [HasPermission(Permissions.Email.Send)]
    [ProducesResponseType(typeof(SearchJobDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SearchJobDto>> GetSendJob(Guid id, CancellationToken ct)
        => FromResult(await jobs.GetByIdAsync(id, ct));

    /// <summary>Runs that are queued, running or stopping — the caller's own.</summary>
    [HttpGet("send-jobs/active")]
    [HasPermission(Permissions.Email.Send)]
    [ProducesResponseType(typeof(IReadOnlyList<SearchJobDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<SearchJobDto>>> GetActiveSendJobs(CancellationToken ct)
        => Ok(await jobs.GetActiveAsync(JobKind.EmailSend, ct));

    /// <summary>Asks a batch to stop. A live worker notices at its next checkpoint.</summary>
    [HttpPost("send-jobs/{id:guid}/stop")]
    [HasPermission(Permissions.Email.Send)]
    [ProducesResponseType(typeof(SearchJobDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<SearchJobDto>> StopSendJob(Guid id, CancellationToken ct)
        => FromResult(await jobs.RequestStopAsync(id, ct));

    /// <summary>Renders a preset against a lead without sending it.</summary>
    [HttpPost("preview")]
    [HasPermission(Permissions.Email.Send)]
    [ProducesResponseType(typeof(EmailPreviewDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<EmailPreviewDto>> Preview(EmailPreviewRequest request, CancellationToken ct)
        => FromResult(await email.PreviewAsync(request, ct));

    /// <summary>
    /// Sent-email history. Callers without <c>email.view-all-logs</c> only ever
    /// see their own sends, whatever sender they ask for.
    /// </summary>
    [HttpGet("log")]
    [HasPermission(Permissions.Email.ViewOwnLog)]
    [ProducesResponseType(typeof(PagedResult<EmailLogDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<EmailLogDto>>> Log(
        [FromQuery] EmailLogQueryRequest request,
        CancellationToken ct)
        => Ok(await email.GetLogAsync(request, ct));

    /// <summary>Send totals over an optional date range.</summary>
    [HttpGet("stats")]
    [HasPermission(Permissions.Email.ViewOwnLog)]
    [ProducesResponseType(typeof(EmailStatsDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<EmailStatsDto>> Stats(
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        CancellationToken ct)
        => Ok(await email.GetStatsAsync(from, to, ct));

    /// <summary>Verifies the SMTP credentials without emailing a real lead.</summary>
    [HttpPost("test-connection")]
    [HasPermission(Permissions.Settings.Update)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> TestConnection(CancellationToken ct)
    {
        var result = await email.TestConnectionAsync(ct);
        return result.Succeeded ? NoContent() : Problem(result);
    }

    /// <summary>Placeholders an admin may use when authoring a preset.</summary>
    [HttpGet("tokens")]
    [HasPermission(Permissions.Email.ViewTemplates)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<object> Tokens() =>
        Ok(TemplateRenderer.AvailableTokens.Select(t => new { token = t.Token, description = t.Description }));

    /// <summary>
    /// Checks one address on demand and returns an immediate verdict, rather
    /// than waiting for the next background sweep
    /// (<c>EmailValidationWorkerService</c>) to reach it.
    /// <para>
    /// Always runs the SMTP probe for this one address, regardless of
    /// <c>EmailValidation:EnableSmtpProbe</c> — a single ad-hoc lookup a caller
    /// explicitly asked for carries none of the volume risk the background
    /// sweep's own conservative default protects against. See
    /// <c>EmailValidationPipeline</c>'s remarks, and the developer docs' §29,
    /// for what "deliverable" does and does not prove: a definitive SMTP
    /// rejection (550/551/553) is trustworthy, but Gmail, Microsoft 365 and
    /// most large mailbox providers accept RCPT TO for addresses that do not
    /// exist specifically to defeat this technique — for a domain hosted on
    /// one of those, "Valid" here means "nothing rejected it", not "confirmed
    /// to exist". A confirmed answer on those domains requires the separate,
    /// opt-in bounce-check pass (<c>EmailBounceCheckWorkerService</c>), which
    /// actually sends and watches for a bounce.
    /// </para>
    /// </summary>
    [HttpPost("verify")]
    [HasPermission(Permissions.Email.Send)]
    [ProducesResponseType(typeof(VerifyEmailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<VerifyEmailResponse>> Verify(VerifyEmailRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Email)) return BadRequest("Email is required.");

        var outcome = await validation.ValidateAsync(request.Email, ct, forceSmtpProbe: true);
        if (outcome is null) return BadRequest("Email is required.");

        return Ok(new VerifyEmailResponse(
            outcome.Details.NormalizedEmail,
            outcome.Status.ToString(),
            outcome.Confidence,
            outcome.IsDisposable,
            outcome.IsRoleAccount,
            outcome.IsCatchAll,
            outcome.Details.SmtpProbeResult,
            outcome.Details.Reason));
    }
}

public sealed record VerifyEmailRequest(string Email);

/// <param name="Email">The normalized address that was checked.</param>
/// <param name="Status">"Valid" | "Risky" | "Invalid" | "Unknown" — see EmailStatus.</param>
/// <param name="Confidence">0-100.</param>
/// <param name="IsDisposable">A known throwaway-mail provider.</param>
/// <param name="IsRoleAccount">A shared/team mailbox (info@, sales@, ...) rather than a named person.</param>
/// <param name="IsCatchAll">Null only if the SMTP probe itself could not run (e.g. no MX, or the recipient's mail port is unreachable from this host).</param>
/// <param name="SmtpProbeResult">"Accepted" | "Rejected" | "Inconclusive".</param>
/// <param name="Reason">Human-readable explanation of the DNS/MX-level verdict.</param>
public sealed record VerifyEmailResponse(
    string Email,
    string Status,
    int Confidence,
    bool IsDisposable,
    bool IsRoleAccount,
    bool? IsCatchAll,
    string? SmtpProbeResult,
    string Reason);

/// <summary>Admin-managed presets and signatures.</summary>
[Authorize]
[Route("api/email/templates")]
public sealed class EmailTemplatesController(IEmailTemplateService templates) : ApiControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.Email.ViewTemplates)]
    [ProducesResponseType(typeof(IReadOnlyList<EmailTemplateDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<EmailTemplateDto>>> GetAll(
        [FromQuery] bool activeOnly = false,
        CancellationToken ct = default)
        => Ok(await templates.GetTemplatesAsync(activeOnly, ct));

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.Email.ViewTemplates)]
    [ProducesResponseType(typeof(EmailTemplateDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<EmailTemplateDto>> GetById(Guid id, CancellationToken ct)
        => FromResult(await templates.GetTemplateAsync(id, ct));

    [HttpPost]
    [HasPermission(Permissions.Email.ManageTemplates)]
    [ProducesResponseType(typeof(EmailTemplateDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<EmailTemplateDto>> Create(
        CreateEmailTemplateRequest request,
        CancellationToken ct)
    {
        var result = await templates.CreateTemplateAsync(request, ct);
        if (!result.Succeeded) return Problem(result);

        return CreatedAtAction(nameof(GetById), new { id = result.Value!.Id }, result.Value);
    }

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.Email.ManageTemplates)]
    [ProducesResponseType(typeof(EmailTemplateDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<EmailTemplateDto>> Update(
        Guid id,
        UpdateEmailTemplateRequest request,
        CancellationToken ct)
        => FromResult(await templates.UpdateTemplateAsync(id, request, ct));

    /// <summary>
    /// Deletes a preset, or retires it when it already has send history so the
    /// audit trail stays intact.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [HasPermission(Permissions.Email.ManageTemplates)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var result = await templates.DeleteTemplateAsync(id, ct);
        return result.Succeeded ? NoContent() : Problem(result);
    }
}

[Authorize]
[Route("api/email/signatures")]
public sealed class EmailSignaturesController(IEmailTemplateService templates) : ApiControllerBase
{
    /// <summary>The caller's own signatures plus any shared ones.</summary>
    [HttpGet]
    [HasPermission(Permissions.Email.ViewTemplates)]
    [ProducesResponseType(typeof(IReadOnlyList<EmailSignatureDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<EmailSignatureDto>>> GetAll(CancellationToken ct)
        => Ok(await templates.GetSignaturesAsync(CurrentUserId, ct));

    [HttpPost]
    [HasPermission(Permissions.Email.ManageSignatures)]
    [ProducesResponseType(typeof(EmailSignatureDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<EmailSignatureDto>> Create(
        SaveEmailSignatureRequest request,
        CancellationToken ct)
        => FromResult(await templates.SaveSignatureAsync(null, request, ct));

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.Email.ManageSignatures)]
    [ProducesResponseType(typeof(EmailSignatureDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<EmailSignatureDto>> Update(
        Guid id,
        SaveEmailSignatureRequest request,
        CancellationToken ct)
        => FromResult(await templates.SaveSignatureAsync(id, request, ct));

    [HttpDelete("{id:guid}")]
    [HasPermission(Permissions.Email.ManageSignatures)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var result = await templates.DeleteSignatureAsync(id, ct);
        return result.Succeeded ? NoContent() : Problem(result);
    }
}
