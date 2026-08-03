using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using DnsClient;
using DnsClient.Protocol;
using LeadMine.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace LeadMine.Infrastructure.Scraping.Verification;

public sealed record EmailVerification(EmailStatus Status, string Reason);

/// <summary>
/// Email deliverability checking.
/// <para>
/// WHAT THIS PROVES: the address is well-formed and its domain publishes a mail
/// server, so mail can actually be routed there.
/// </para>
/// <para>
/// WHAT IT DOES NOT PROVE: that the specific mailbox exists. Confirming that
/// requires an SMTP <c>RCPT TO</c> probe, which is deliberately not implemented:
/// major providers refuse or lie about it, catch-all domains accept every
/// address, greylisting produces false negatives, and probing at volume from one
/// IP gets that IP blacklisted — which would damage the deliverability of the
/// campaigns this data feeds.
/// </para>
/// <para>
/// <see cref="EmailStatus.Valid"/> therefore means "safe to attempt", not
/// "guaranteed to land".
/// </para>
/// </summary>
public sealed partial class EmailVerifier(ILogger<EmailVerifier> logger)
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(6);
    private static readonly TimeSpan DnsTimeout = TimeSpan.FromSeconds(5);

    /// <summary>Cheap structural check; the DNS lookup is the expensive part.</summary>
    [GeneratedRegex(@"^[^\s@]+@[^\s@.]+(\.[^\s@.]+)+$", RegexOptions.None, 500)]
    private static partial Regex Syntax();

    /// <summary>
    /// Throwaway inbox providers. Mail routes fine, but the recipient is
    /// transient, so these are flagged risky rather than valid.
    /// </summary>
    private static readonly HashSet<string> DisposableDomains = new(StringComparer.OrdinalIgnoreCase)
    {
        "mailinator.com", "guerrillamail.com", "guerrillamail.net", "10minutemail.com",
        "tempmail.com", "temp-mail.org", "throwawaymail.com", "yopmail.com",
        "trashmail.com", "sharklasers.com", "getnada.com", "dispostable.com",
        "maildrop.cc", "fakeinbox.com", "mintemail.com", "mytemp.email",
        "spamgourmet.com", "mailnesia.com", "tempinbox.com", "emailondeck.com",
        "moakt.com", "burnermail.io", "anonaddy.com", "simplelogin.io",
    };

    /// <summary>
    /// Shared-mailbox prefixes. Perfectly deliverable and usually the *right*
    /// address for B2B outreach, so they stay valid — the reason string just
    /// notes that no individual person is behind them.
    /// </summary>
    private static readonly HashSet<string> RolePrefixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "info", "contact", "sales", "support", "help", "admin", "office", "hello",
        "enquiries", "enquiry", "inquiries", "inquiry", "reservations", "bookings",
        "team", "mail", "reception", "accounts", "billing", "service", "orders",
    };

    /// <summary>Cached per domain — most lead lists repeat the same few.</summary>
    private static readonly ConcurrentDictionary<string, (EmailVerification Result, DateTimeOffset ExpiresAt)> DomainCache =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Collapses concurrent lookups for one domain into a single query.</summary>
    private static readonly ConcurrentDictionary<string, Task<EmailVerification>> InFlight =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly LookupClient Dns = new(new LookupClientOptions
    {
        Timeout = DnsTimeout,
        UseCache = true,
        // One retry: a lost UDP packet is common, but a run cannot stall on DNS.
        Retries = 1,
        ThrowDnsErrors = false,
    });

    /// <summary>Verifies one address. An empty input is left unverified.</summary>
    public async Task<EmailVerification> VerifyAsync(string? email, CancellationToken ct = default)
    {
        var address = (email ?? string.Empty).Trim().ToLowerInvariant();

        if (address.Length == 0) return new EmailVerification(EmailStatus.Unverified, string.Empty);

        if (address.Length > 254 || !Syntax().IsMatch(address))
        {
            return new EmailVerification(EmailStatus.Invalid, "Malformed email address");
        }

        var atIndex = address.LastIndexOf('@');
        var localPart = address[..atIndex];
        var domain = address[(atIndex + 1)..];

        if (localPart.Length > 64)
        {
            return new EmailVerification(EmailStatus.Invalid, "Local part exceeds 64 characters");
        }

        if (DisposableDomains.Contains(domain))
        {
            return new EmailVerification(EmailStatus.Risky, "Disposable email provider");
        }

        var domainResult = await VerifyDomainAsync(domain, ct);

        // Role mailboxes are usually the correct B2B contact, so they stay valid;
        // the reason line just makes the distinction visible.
        if (domainResult.Status == EmailStatus.Valid && RolePrefixes.Contains(localPart))
        {
            return new EmailVerification(EmailStatus.Valid, "Deliverable shared/role mailbox");
        }

        return domainResult;
    }

    private async Task<EmailVerification> VerifyDomainAsync(string domain, CancellationToken ct)
    {
        if (DomainCache.TryGetValue(domain, out var cached) && cached.ExpiresAt > DateTimeOffset.UtcNow)
        {
            return cached.Result;
        }

        var lookup = InFlight.GetOrAdd(domain, key => LookupDomainAsync(key, ct));

        try
        {
            return await lookup;
        }
        finally
        {
            InFlight.TryRemove(domain, out _);
        }
    }

    private async Task<EmailVerification> LookupDomainAsync(string domain, CancellationToken ct)
    {
        EmailVerification result;

        try
        {
            var response = await Dns.QueryAsync(domain, QueryType.MX, cancellationToken: ct);

            var usable = response.Answers
                .OfType<MxRecord>()
                .Where(r => !string.IsNullOrWhiteSpace(r.Exchange.Value) && r.Exchange.Value != ".")
                .ToList();

            if (usable.Count > 0)
            {
                result = new EmailVerification(EmailStatus.Valid, "Domain accepts mail (MX record found)");
            }
            else if (response.Header.ResponseCode == DnsHeaderResponseCode.NotExistentDomain)
            {
                result = new EmailVerification(EmailStatus.Invalid, "Domain does not exist");
            }
            else if (response.Answers.OfType<MxRecord>().Any())
            {
                // An explicit "null MX" (RFC 7505) means the domain refuses all mail.
                result = new EmailVerification(EmailStatus.Invalid, "Domain explicitly accepts no mail (null MX)");
            }
            else
            {
                // No MX at all. RFC 5321 permits falling back to the A record, so a
                // domain that still resolves might accept mail — downgrade rather
                // than reject outright.
                result = await DomainResolvesAsync(domain, ct) switch
                {
                    true => new EmailVerification(EmailStatus.Risky, "No MX record; mail may still route via the A record"),
                    false => new EmailVerification(EmailStatus.Invalid, "Domain does not exist"),
                    null => new EmailVerification(EmailStatus.Unknown, "Domain lookup was inconclusive"),
                };
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "MX lookup failed for {Domain}", domain);

            // Absence of evidence, not evidence of absence — never mark a lead
            // dead on an inconclusive lookup.
            result = await DomainResolvesAsync(domain, ct) switch
            {
                false => new EmailVerification(EmailStatus.Invalid, "Domain does not exist"),
                true => new EmailVerification(
                    EmailStatus.Risky,
                    "Domain resolves, but its mail records could not be checked here"),
                null => new EmailVerification(EmailStatus.Unknown, "Domain lookup was inconclusive"),
            };
        }

        DomainCache[domain] = (result, DateTimeOffset.UtcNow.Add(CacheTtl));
        return result;
    }

    /// <summary>
    /// Does the domain resolve at all?
    /// <para>
    /// Goes through the OS resolver rather than querying a DNS server directly.
    /// Some hosts block outbound port 53 while the platform resolver still
    /// works, so this is the fallback that keeps verification useful there.
    /// Null means the answer was genuinely inconclusive.
    /// </para>
    /// </summary>
    private static async Task<bool?> DomainResolvesAsync(string domain, CancellationToken ct)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(DnsTimeout);

            var addresses = await System.Net.Dns.GetHostAddressesAsync(domain, timeout.Token);
            return addresses.Length > 0;
        }
        catch (SocketException ex) when (ex.SocketErrorCode is SocketError.HostNotFound or SocketError.NoData)
        {
            return false;
        }
        catch
        {
            // TryAgain and friends are transient, not proof of absence.
            return null;
        }
    }

    public static void ClearCache() => DomainCache.Clear();
}
