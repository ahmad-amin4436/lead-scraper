using LeadMine.Application.DTOs;
using LeadMine.Domain.Entities;
using LeadMine.Domain.Enums;

namespace LeadMine.Infrastructure.Scraping.Verification;

/// <param name="WhatsAppLink">Canonical wa.me link when one can be derived; empty leaves the field alone.</param>
public sealed record VerificationOutcome(
    EmailStatus EmailStatus,
    WhatsAppStatus WhatsAppStatus,
    string WhatsAppLink,
    IReadOnlyList<string> Notes);

/// <summary>
/// Applies email deliverability and WhatsApp reachability checks to a lead.
/// <para>
/// Both checks are non-destructive: <see cref="ApplyToAsync"/> decides what gets
/// written back. See <see cref="EmailVerifier"/> and
/// <see cref="WhatsAppClassifier"/> for exactly what each status does and does
/// not prove.
/// </para>
/// </summary>
public sealed class VerificationService(EmailVerifier emailVerifier)
{
    public async Task<VerificationOutcome> VerifyAsync(
        string? email,
        string? phone,
        string? country,
        string? publishedWhatsAppLink,
        CancellationToken ct = default)
    {
        var emailResult = await emailVerifier.VerifyAsync(email, ct);

        // A wa.me/chat.whatsapp.com link found during enrichment is direct
        // evidence; anything else is inferred from the number's line type.
        var whatsapp = WhatsAppClassifier.Assess(phone, country, publishedWhatsAppLink);

        var notes = new List<string>();

        if (!string.IsNullOrWhiteSpace(email) && emailResult.Reason.Length > 0)
        {
            notes.Add($"Email: {emailResult.Reason}");
        }

        if (whatsapp.Reason.Length > 0) notes.Add($"WhatsApp: {whatsapp.Reason}");

        return new VerificationOutcome(emailResult.Status, whatsapp.Status, whatsapp.Link, notes);
    }

    /// <summary>Verifies a scraped lead and writes the results onto it in place.</summary>
    public Task<VerificationOutcome> ApplyToAsync(IngestLead lead, CancellationToken ct = default) =>
        ApplyToAsync(
            lead.Email, lead.Phone, lead.Country, lead.WhatsApp,
            status => lead.EmailStatus = status,
            status => lead.WhatsAppStatus = status,
            link => lead.WhatsApp = link,
            ct);

    /// <summary>
    /// Verifies a stored lead and writes the results onto it in place.
    /// <para>
    /// Separate overload rather than a shared base type: a freshly scraped
    /// <see cref="IngestLead"/> and a persisted <see cref="Business"/> row have
    /// no common ancestor worth introducing just for this one call.
    /// </para>
    /// </summary>
    public Task<VerificationOutcome> ApplyToAsync(Business lead, CancellationToken ct = default) =>
        ApplyToAsync(
            lead.Email, lead.Phone, lead.Country, lead.WhatsApp,
            status => lead.EmailStatus = status,
            status => lead.WhatsAppStatus = status,
            link => lead.WhatsApp = link,
            ct);

    private async Task<VerificationOutcome> ApplyToAsync(
        string email,
        string phone,
        string country,
        string whatsApp,
        Action<EmailStatus> setEmailStatus,
        Action<WhatsAppStatus> setWhatsAppStatus,
        Action<string> setWhatsAppLink,
        CancellationToken ct)
    {
        var published = string.IsNullOrWhiteSpace(whatsApp) ? null : whatsApp;

        var outcome = await VerifyAsync(email, phone, country, published, ct);

        setEmailStatus(outcome.EmailStatus);
        setWhatsAppStatus(outcome.WhatsAppStatus);

        // Only fill the WhatsApp column when it's empty — never overwrite a link
        // the business published with one we derived.
        if (published is null && outcome.WhatsAppLink.Length > 0)
        {
            setWhatsAppLink(outcome.WhatsAppLink);
        }

        return outcome;
    }
}
