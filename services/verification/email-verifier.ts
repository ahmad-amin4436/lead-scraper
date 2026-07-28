import 'server-only';

import dns from 'node:dns/promises';

import type { EmailStatus } from '@/types/business';
import { Mutex } from '@/utils/async';

/**
 * Email deliverability checking.
 *
 * WHAT THIS PROVES: the address is well-formed and its domain publishes a mail
 * server, so mail can actually be routed there.
 *
 * WHAT IT DOES NOT PROVE: that the specific mailbox exists. Confirming that
 * requires an SMTP `RCPT TO` probe, which is deliberately not implemented here:
 * major providers (Google, Microsoft, Yahoo) refuse or lie about it, catch-all
 * domains accept every address, greylisting produces false negatives, and
 * probing at volume from one IP gets that IP blacklisted — which would damage
 * the deliverability of the campaigns this data feeds.
 *
 * `valid` therefore means "safe to attempt", not "guaranteed to land".
 */

export interface EmailVerification {
  status: EmailStatus;
  /** Short human-readable explanation, stored alongside the record. */
  reason: string;
}

/** Cheap structural check; the DNS lookup is the expensive part. */
const SYNTAX = /^[^\s@]+@[^\s@.]+(\.[^\s@.]+)+$/;

/**
 * Throwaway inbox providers. Mail routes fine, but the recipient is transient,
 * so these are flagged `risky` rather than `valid`.
 */
const DISPOSABLE_DOMAINS = new Set([
  'mailinator.com', 'guerrillamail.com', 'guerrillamail.net', '10minutemail.com',
  'tempmail.com', 'temp-mail.org', 'throwawaymail.com', 'yopmail.com',
  'trashmail.com', 'sharklasers.com', 'getnada.com', 'dispostable.com',
  'maildrop.cc', 'fakeinbox.com', 'mintemail.com', 'mytemp.email',
  'spamgourmet.com', 'mailnesia.com', 'tempinbox.com', 'emailondeck.com',
  'moakt.com', 'burnermail.io', 'anonaddy.com', 'simplelogin.io',
]);

/**
 * Shared-mailbox prefixes. Perfectly deliverable and usually the *right*
 * address for B2B outreach, so they stay `valid` — the reason string just notes
 * that no individual person is behind them.
 */
const ROLE_PREFIXES = new Set([
  'info', 'contact', 'sales', 'support', 'help', 'admin', 'office', 'hello',
  'enquiries', 'enquiry', 'inquiries', 'inquiry', 'reservations', 'bookings',
  'team', 'mail', 'reception', 'accounts', 'billing', 'service', 'orders',
]);

interface CacheEntry {
  result: Omit<EmailVerification, 'reason'> & { reason: string };
  expiresAt: number;
}

const CACHE_TTL_MS = 6 * 60 * 60 * 1000;
const DNS_TIMEOUT_MS = 5_000;

const globalForEmail = globalThis as unknown as {
  leadmineEmailDomainCache?: Map<string, CacheEntry>;
  leadmineEmailDomainLocks?: Map<string, Promise<CacheEntry['result']>>;
};

/** Results are cached per *domain* — most lead lists repeat the same few. */
const domainCache =
  globalForEmail.leadmineEmailDomainCache ?? new Map<string, CacheEntry>();
globalForEmail.leadmineEmailDomainCache = domainCache;

const inFlight =
  globalForEmail.leadmineEmailDomainLocks ?? new Map<string, Promise<CacheEntry['result']>>();
globalForEmail.leadmineEmailDomainLocks = inFlight;

const cacheMutex = new Mutex();

function withTimeout<T>(promise: Promise<T>, ms: number): Promise<T> {
  return Promise.race([
    promise,
    new Promise<T>((_, reject) =>
      setTimeout(() => reject(new Error('DNS timeout')), ms).unref?.(),
    ),
  ]);
}

/** Resolves whether a domain publishes a usable mail route. */
async function verifyDomain(domain: string): Promise<EmailVerification> {
  const cached = domainCache.get(domain);
  if (cached && cached.expiresAt > Date.now()) return cached.result;

  // Collapse concurrent lookups for the same domain into one DNS query.
  const existing = inFlight.get(domain);
  if (existing) return existing;

  const lookup = (async (): Promise<EmailVerification> => {
    let result: EmailVerification;

    try {
      const records = await withTimeout(dns.resolveMx(domain), DNS_TIMEOUT_MS);
      const usable = records.filter((r) => r.exchange && r.exchange !== '.');

      if (usable.length > 0) {
        result = { status: 'valid', reason: 'Domain accepts mail (MX record found)' };
      } else {
        // An explicit "null MX" (RFC 7505) means the domain refuses all mail.
        result = { status: 'invalid', reason: 'Domain explicitly accepts no mail (null MX)' };
      }
    } catch (error) {
      const code = (error as NodeJS.ErrnoException).code;

      if (code === 'ENOTFOUND' || code === 'ENODATA') {
        // No MX. RFC 5321 permits falling back to the A record, so a domain that
        // resolves at all might still accept mail — downgrade rather than reject.
        try {
          await withTimeout(dns.resolve4(domain), DNS_TIMEOUT_MS);
          result = {
            status: 'risky',
            reason: 'No MX record; mail may still route via the A record',
          };
        } catch (fallbackError) {
          const fallbackCode = (fallbackError as NodeJS.ErrnoException).code;
          result =
            fallbackCode === 'ENOTFOUND' || fallbackCode === 'ENODATA'
              ? { status: 'invalid', reason: 'Domain does not exist' }
              : { status: 'unknown', reason: 'Domain lookup failed' };
        }
      } else {
        // SERVFAIL, timeout, network trouble — absence of evidence, not evidence
        // of absence. Never mark a lead invalid on an inconclusive lookup.
        result = { status: 'unknown', reason: 'Domain lookup was inconclusive' };
      }
    }

    await cacheMutex.run(async () => {
      domainCache.set(domain, { result, expiresAt: Date.now() + CACHE_TTL_MS });
    });

    return result;
  })();

  inFlight.set(domain, lookup);

  try {
    return await lookup;
  } finally {
    inFlight.delete(domain);
  }
}

export const emailVerifier = {
  /** Verifies one address. Returns `unverified` for an empty input. */
  async verify(email: string): Promise<EmailVerification> {
    const address = email.trim().toLowerCase();
    if (!address) return { status: 'unverified', reason: '' };

    if (address.length > 254 || !SYNTAX.test(address)) {
      return { status: 'invalid', reason: 'Malformed email address' };
    }

    const atIndex = address.lastIndexOf('@');
    const localPart = address.slice(0, atIndex);
    const domain = address.slice(atIndex + 1);

    if (localPart.length > 64) {
      return { status: 'invalid', reason: 'Local part exceeds 64 characters' };
    }
    if (DISPOSABLE_DOMAINS.has(domain)) {
      return { status: 'risky', reason: 'Disposable email provider' };
    }

    const domainResult = await verifyDomain(domain);

    // Role mailboxes are usually the correct B2B contact, so they stay valid;
    // the reason line just makes the distinction visible in the sheet.
    if (domainResult.status === 'valid' && ROLE_PREFIXES.has(localPart)) {
      return { status: 'valid', reason: 'Deliverable shared/role mailbox' };
    }

    return domainResult;
  },

  clearCache(): void {
    domainCache.clear();
  },
};
