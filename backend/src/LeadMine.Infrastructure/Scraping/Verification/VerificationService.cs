using LeadMine.Application.DTOs;
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
    public async Task<VerificationOutcome> ApplyToAsync(IngestLead lead, CancellationToken ct = default)
    {
        var published = string.IsNullOrWhiteSpace(lead.WhatsApp) ? null : lead.WhatsApp;

        var outcome = await VerifyAsync(lead.Email, lead.Phone, lead.Country, published, ct);

        lead.EmailStatus = outcome.EmailStatus;
        lead.WhatsAppStatus = outcome.WhatsAppStatus;

        // Only fill the WhatsApp column when it's empty — never overwrite a link
        // the business published with one we derived.
        if (published is null && outcome.WhatsAppLink.Length > 0)
        {
            lead.WhatsApp = outcome.WhatsAppLink;
        }

        return outcome;
    }
}
