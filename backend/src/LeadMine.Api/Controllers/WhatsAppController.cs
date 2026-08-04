using LeadMine.Application.Authorization;
using LeadMine.Application.Common;
using LeadMine.Application.DTOs;
using LeadMine.Application.Interfaces;
using LeadMine.Infrastructure.Authorization;
using LeadMine.Infrastructure.Email;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LeadMine.Api.Controllers;

/// <summary>
/// Click-to-chat WhatsApp links for leads. There is no send endpoint here —
/// automated WhatsApp delivery is out of scope, so the caller opens the
/// generated link themselves.
/// </summary>
[Authorize]
[Route("api/whatsapp")]
public sealed class WhatsAppController(IWhatsAppService whatsApp) : ApiControllerBase
{
    /// <summary>
    /// Renders an approved preset into a wa.me link per lead. Recipients are
    /// resolved server-side from the lead records the caller is allowed to see.
    /// </summary>
    [HttpPost("links")]
    [HasPermission(Permissions.WhatsApp.Send)]
    [ProducesResponseType(typeof(GenerateWhatsAppLinksResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<GenerateWhatsAppLinksResultDto>> GenerateLinks(
        GenerateWhatsAppLinksRequest request,
        CancellationToken ct)
        => FromResult(await whatsApp.GenerateLinksAsync(request, ct));

    /// <summary>Renders a preset against a lead without recording anything.</summary>
    [HttpPost("preview")]
    [HasPermission(Permissions.WhatsApp.Send)]
    [ProducesResponseType(typeof(WhatsAppPreviewDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<WhatsAppPreviewDto>> Preview(WhatsAppPreviewRequest request, CancellationToken ct)
        => FromResult(await whatsApp.PreviewAsync(request, ct));

    /// <summary>
    /// Records that a generated link was opened. Called by the frontend at the
    /// moment the user clicks through to WhatsApp.
    /// </summary>
    [HttpPost("mark-contacted")]
    [HasPermission(Permissions.WhatsApp.Send)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> MarkContacted(MarkWhatsAppContactedRequest request, CancellationToken ct)
    {
        var result = await whatsApp.MarkContactedAsync(request, ct);
        return result.Succeeded ? NoContent() : Problem(result);
    }

    /// <summary>Placeholders an admin may use when authoring a preset.</summary>
    [HttpGet("tokens")]
    [HasPermission(Permissions.WhatsApp.ViewTemplates)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<object> Tokens() =>
        Ok(TemplateRenderer.AvailableTokens
            .Where(t => t.Token is not "{{sender_email}}")
            .Select(t => new { token = t.Token, description = t.Description }));
}

/// <summary>Admin-managed WhatsApp presets.</summary>
[Authorize]
[Route("api/whatsapp/templates")]
public sealed class WhatsAppTemplatesController(IWhatsAppTemplateService templates) : ApiControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.WhatsApp.ViewTemplates)]
    [ProducesResponseType(typeof(IReadOnlyList<WhatsAppTemplateDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<WhatsAppTemplateDto>>> GetAll(
        [FromQuery] bool activeOnly = false,
        CancellationToken ct = default)
        => Ok(await templates.GetTemplatesAsync(activeOnly, ct));

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.WhatsApp.ViewTemplates)]
    [ProducesResponseType(typeof(WhatsAppTemplateDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<WhatsAppTemplateDto>> GetById(Guid id, CancellationToken ct)
        => FromResult(await templates.GetTemplateAsync(id, ct));

    [HttpPost]
    [HasPermission(Permissions.WhatsApp.ManageTemplates)]
    [ProducesResponseType(typeof(WhatsAppTemplateDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<WhatsAppTemplateDto>> Create(
        CreateWhatsAppTemplateRequest request,
        CancellationToken ct)
    {
        var result = await templates.CreateTemplateAsync(request, ct);
        if (!result.Succeeded) return Problem(result);

        return CreatedAtAction(nameof(GetById), new { id = result.Value!.Id }, result.Value);
    }

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.WhatsApp.ManageTemplates)]
    [ProducesResponseType(typeof(WhatsAppTemplateDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<WhatsAppTemplateDto>> Update(
        Guid id,
        UpdateWhatsAppTemplateRequest request,
        CancellationToken ct)
        => FromResult(await templates.UpdateTemplateAsync(id, request, ct));

    /// <summary>
    /// Deletes a preset, or retires it when it already has contact history so
    /// the audit trail stays intact.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [HasPermission(Permissions.WhatsApp.ManageTemplates)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var result = await templates.DeleteTemplateAsync(id, ct);
        return result.Succeeded ? NoContent() : Problem(result);
    }
}
