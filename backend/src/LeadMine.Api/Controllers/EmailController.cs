using LeadMine.Application.Authorization;
using LeadMine.Application.Common;
using LeadMine.Application.DTOs;
using LeadMine.Application.Interfaces;
using LeadMine.Infrastructure.Authorization;
using LeadMine.Infrastructure.Email;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LeadMine.Api.Controllers;

/// <summary>Sending mail to leads, and the history of what was sent.</summary>
[Authorize]
[Route("api/email")]
public sealed class EmailController(IEmailService email) : ApiControllerBase
{
    /// <summary>
    /// Sends an approved preset to one or more leads. Recipients are resolved
    /// server-side from the lead records the caller is allowed to see.
    /// </summary>
    [HttpPost("send")]
    [HasPermission(Permissions.Email.Send)]
    [ProducesResponseType(typeof(SendEmailResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<SendEmailResultDto>> Send(SendEmailRequest request, CancellationToken ct)
        => FromResult(await email.SendAsync(request, ct));

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
}

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
