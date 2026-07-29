import '@/lib/server-guard';

import dns from 'node:dns/promises';
import net from 'node:net';

/**
 * SSRF guard for the enrichment crawler.
 *
 * Website URLs originate from third-party search providers, so they are
 * untrusted input. Before fetching, the hostname is resolved and every returned
 * address is checked against private, loopback, link-local and reserved ranges
 * so the crawler can never be aimed at internal infrastructure or cloud
 * metadata endpoints.
 */

function isPrivateIPv4(ip: string): boolean {
  const parts = ip.split('.').map(Number);
  if (parts.length !== 4 || parts.some((p) => !Number.isInteger(p) || p < 0 || p > 255)) {
    return true;
  }

  const [a, b] = parts;

  if (a === 0) return true; // "this network"
  if (a === 10) return true; // RFC1918
  if (a === 127) return true; // loopback
  if (a === 169 && b === 254) return true; // link-local, incl. cloud metadata
  if (a === 172 && b >= 16 && b <= 31) return true; // RFC1918
  if (a === 192 && b === 168) return true; // RFC1918
  if (a === 192 && b === 0) return true; // IETF protocol assignments
  if (a === 100 && b >= 64 && b <= 127) return true; // CGNAT
  if (a === 198 && (b === 18 || b === 19)) return true; // benchmarking
  if (a >= 224) return true; // multicast + reserved

  return false;
}

function isPrivateIPv6(ip: string): boolean {
  const address = ip.toLowerCase().split('%')[0];

  if (address === '::' || address === '::1') return true;
  if (address.startsWith('fe80') || address.startsWith('fec0')) return true; // link/site-local
  if (/^f[cd]/.test(address)) return true; // unique local
  if (address.startsWith('ff')) return true; // multicast

  // IPv4-mapped (::ffff:10.0.0.1) inherits the IPv4 rules.
  const mapped = address.match(/^::ffff:(\d+\.\d+\.\d+\.\d+)$/);
  if (mapped) return isPrivateIPv4(mapped[1]);

  return false;
}

export function isPrivateAddress(ip: string): boolean {
  const version = net.isIP(ip);
  if (version === 4) return isPrivateIPv4(ip);
  if (version === 6) return isPrivateIPv6(ip);
  return true;
}

const BLOCKED_HOST_SUFFIXES = ['.local', '.localhost', '.internal', '.home.arpa', '.onion'];

export interface CrawlTargetCheck {
  safe: boolean;
  /** Human-readable reason when `safe` is false, for the record's notes. */
  reason: string | null;
}

const SAFE: CrawlTargetCheck = { safe: true, reason: null };

/**
 * Resolves `hostname` and reports whether every address is publicly routable.
 *
 * Note this leaves a small TOCTOU window between the check and the fetch. It is
 * acceptable here because the crawler only ever reads public marketing pages and
 * discards the body, but a stricter deployment should pin the resolved address.
 */
export async function checkPubliclyRoutable(hostname: string): Promise<CrawlTargetCheck> {
  const host = hostname.toLowerCase().replace(/\.$/, '');

  if (!host || host === 'localhost') {
    return { safe: false, reason: 'Host is not publicly routable' };
  }
  if (BLOCKED_HOST_SUFFIXES.some((suffix) => host.endsWith(suffix))) {
    return { safe: false, reason: 'Host uses an internal-only domain suffix' };
  }

  // Literal IPs skip DNS entirely.
  if (net.isIP(host)) {
    return isPrivateAddress(host)
      ? { safe: false, reason: 'Host is a private or reserved IP address' }
      : SAFE;
  }

  let addresses: { address: string }[];
  try {
    addresses = await dns.lookup(host, { all: true, verbatim: true });
  } catch (error) {
    // A DNS failure is a dead or unreachable site, not a blocked one — say so,
    // because the two need very different follow-up.
    const code = (error as NodeJS.ErrnoException).code ?? 'DNS error';
    return { safe: false, reason: `Website domain could not be resolved (${code})` };
  }

  if (addresses.length === 0) {
    return { safe: false, reason: 'Website domain could not be resolved' };
  }
  if (addresses.some((entry) => isPrivateAddress(entry.address))) {
    return { safe: false, reason: 'Host resolves to a private or reserved IP address' };
  }

  return SAFE;
}

/** Validates a candidate crawl target: http(s) scheme on a public host. */
export async function checkCrawlTarget(url: string): Promise<CrawlTargetCheck> {
  let parsed: URL;
  try {
    parsed = new URL(url);
  } catch {
    return { safe: false, reason: 'Invalid website URL' };
  }

  if (parsed.protocol !== 'http:' && parsed.protocol !== 'https:') {
    return { safe: false, reason: 'Website URL is not HTTP or HTTPS' };
  }

  // Credentials in a URL are a redirect-abuse signal, not something a business site needs.
  if (parsed.username || parsed.password) {
    return { safe: false, reason: 'Website URL contains embedded credentials' };
  }

  return checkPubliclyRoutable(parsed.hostname);
}
