import 'server-only';

import type { CheerioAPI } from 'cheerio';

import { normalizeHost, normalizePhone, normalizeUrl } from '@/utils/normalize';

const EMAIL_PATTERN = /[a-zA-Z0-9._%+-]+@[a-zA-Z0-9](?:[a-zA-Z0-9-]*[a-zA-Z0-9])?(?:\.[a-zA-Z0-9](?:[a-zA-Z0-9-]*[a-zA-Z0-9])?)+/g;

/** Asset extensions that the email regex picks up out of filenames like `logo@2x.png`. */
const FILE_EXTENSIONS =
  /\.(png|jpe?g|gif|svg|webp|avif|ico|css|js|mjs|json|xml|pdf|zip|woff2?|ttf|eot|mp4|webm|mp3)$/i;

/** Placeholder and third-party-service addresses that are never real leads. */
const BLOCKED_DOMAINS = new Set([
  'example.com', 'example.org', 'example.net', 'domain.com', 'yourdomain.com',
  'email.com', 'yoursite.com', 'mysite.com', 'test.com', 'company.com',
  'sentry.io', 'sentry-next.wixpress.com', 'wixpress.com', 'wix.com',
  'squarespace.com', 'godaddy.com', 'shopify.com', 'cloudflare.com',
  'schema.org', 'w3.org', 'googlemail.com', 'jquery.com', 'fontawesome.com',
]);

const BLOCKED_LOCAL_PARTS = new Set([
  'you', 'your', 'name', 'email', 'user', 'username', 'someone', 'somebody',
  'firstname', 'lastname', 'test', 'example', 'sample', 'noreply', 'no-reply',
  'donotreply', 'do-not-reply', 'wordpress', 'sentry',
]);

/** Mailbox prefixes worth promoting to the record's primary email, best first. */
const PREFERRED_PREFIXES = [
  'sales', 'enquiries', 'enquiry', 'inquiries', 'inquiry', 'contact',
  'hello', 'hi', 'info', 'office', 'admin', 'reception', 'bookings',
  'support', 'help', 'team', 'mail',
];

const SOCIAL_MATCHERS: { key: SocialKey; test: (host: string, url: string) => boolean }[] = [
  {
    key: 'facebook',
    test: (host) => host === 'facebook.com' || host === 'fb.com' || host.endsWith('.facebook.com'),
  },
  { key: 'instagram', test: (host) => host === 'instagram.com' || host.endsWith('.instagram.com') },
  { key: 'linkedin', test: (host) => host === 'linkedin.com' || host.endsWith('.linkedin.com') },
  {
    key: 'twitter',
    test: (host) => host === 'twitter.com' || host === 'x.com' || host.endsWith('.twitter.com'),
  },
  { key: 'youtube', test: (host) => host === 'youtube.com' || host === 'youtu.be' || host.endsWith('.youtube.com') },
  {
    key: 'whatsapp',
    test: (host, url) =>
      host === 'wa.me' ||
      host === 'chat.whatsapp.com' ||
      host === 'api.whatsapp.com' ||
      (host.endsWith('whatsapp.com') && url.includes('send')),
  },
];

export type SocialKey =
  | 'facebook'
  | 'instagram'
  | 'linkedin'
  | 'twitter'
  | 'youtube'
  | 'whatsapp';

export type SocialLinks = Partial<Record<SocialKey, string>>;

/** Social profile roots that carry no business identity. */
const SOCIAL_NOISE = new Set([
  '/', '/home', '/login', '/signup', '/share', '/sharer', '/sharer.php',
  '/share.php', '/intent/tweet', '/shareArticle', '/dialog/feed',
]);

function isValidEmail(candidate: string): boolean {
  const email = candidate.toLowerCase();
  if (email.length > 254 || FILE_EXTENSIONS.test(email)) return false;

  const [localPart, domain] = email.split('@');
  if (!localPart || !domain) return false;
  if (BLOCKED_DOMAINS.has(domain) || BLOCKED_LOCAL_PARTS.has(localPart)) return false;

  // Cache-busting hashes and minified asset names produce long hex local parts.
  if (localPart.length > 24 && /^[0-9a-f]+$/.test(localPart)) return false;
  if (/^[0-9a-f]{32,}$/.test(domain.split('.')[0])) return false;

  const tld = domain.split('.').pop() ?? '';
  return tld.length >= 2 && /^[a-z]+$/.test(tld);
}

/** Ranks candidates so the most contactable mailbox becomes the primary email. */
function scoreEmail(email: string, siteHost: string): number {
  const [localPart, domain] = email.split('@');
  let score = 0;

  const prefixIndex = PREFERRED_PREFIXES.findIndex(
    (prefix) => localPart === prefix || localPart.startsWith(`${prefix}.`),
  );
  if (prefixIndex !== -1) score += 100 - prefixIndex;

  // An address on the business's own domain beats a gmail.com fallback.
  if (siteHost && (domain === siteHost || domain.endsWith(`.${siteHost}`))) score += 40;
  if (localPart.includes('webmaster') || localPart.includes('postmaster')) score -= 20;

  return score;
}

export function extractEmails($: CheerioAPI, pageUrl: string): string[] {
  const found = new Set<string>();

  $('a[href^="mailto:"]').each((_, element) => {
    const href = $(element).attr('href') ?? '';
    const address = href.slice('mailto:'.length).split('?')[0].trim();
    if (address) {
      for (const part of address.split(',')) {
        const email = decodeURIComponent(part).trim().toLowerCase();
        if (isValidEmail(email)) found.add(email);
      }
    }
  });

  // Strip script/style so inline JS and CSS URLs don't pollute the text scan.
  const body = $.root().clone();
  body.find('script, style, noscript, svg').remove();
  const text = body.text();

  for (const match of text.matchAll(EMAIL_PATTERN)) {
    const email = match[0].toLowerCase();
    if (isValidEmail(email)) found.add(email);
  }

  const siteHost = normalizeHost(pageUrl);
  return [...found].sort((a, b) => scoreEmail(b, siteHost) - scoreEmail(a, siteHost));
}

export function extractPhones($: CheerioAPI): string[] {
  const found = new Set<string>();

  $('a[href^="tel:"]').each((_, element) => {
    const href = $(element).attr('href') ?? '';
    const raw = decodeURIComponent(href.slice('tel:'.length)).trim();
    const phone = normalizePhone(raw);
    // Below 7 digits it's an extension or a false positive, not a phone number.
    if (phone.replace(/\D/g, '').length >= 7) found.add(phone);
  });

  return [...found];
}

export function extractSocialLinks($: CheerioAPI, pageUrl: string): SocialLinks {
  const links: SocialLinks = {};

  $('a[href]').each((_, element) => {
    const href = $(element).attr('href');
    if (!href) return;

    const absolute = normalizeUrl(href, pageUrl);
    if (!absolute) return;

    const host = normalizeHost(absolute);
    if (!host) return;

    for (const matcher of SOCIAL_MATCHERS) {
      if (links[matcher.key] || !matcher.test(host, absolute)) continue;

      try {
        const parsed = new URL(absolute);
        const path = parsed.pathname.replace(/\/+$/, '') || '/';
        // Skip share widgets and bare platform homepages.
        if (SOCIAL_NOISE.has(path) || SOCIAL_NOISE.has(parsed.pathname)) continue;
        if (matcher.key === 'linkedin' && !/^\/(company|in|school)\//.test(parsed.pathname)) continue;
      } catch {
        continue;
      }

      links[matcher.key] = absolute;
    }
  });

  return links;
}

const CONTACT_HINTS = [
  'contact', 'contact-us', 'contactus', 'get-in-touch', 'reach-us', 'enquiry',
  'enquiries', 'about', 'about-us', 'aboutus', 'imprint', 'impressum',
  'kontakt', 'contacto', 'contatti', 'nous-contacter', 'legal-notice',
  'support', 'help', 'team', 'locations', 'find-us',
];

/**
 * Finds internal pages most likely to carry contact details, ranked by how
 * strongly the URL and link text suggest a contact page.
 */
export function findContactPageLinks($: CheerioAPI, pageUrl: string, limit: number): string[] {
  const siteHost = normalizeHost(pageUrl);
  const scored = new Map<string, number>();

  $('a[href]').each((_, element) => {
    const href = $(element).attr('href');
    if (!href) return;

    const absolute = normalizeUrl(href, pageUrl);
    if (!absolute || normalizeHost(absolute) !== siteHost) return;

    let parsed: URL;
    try {
      parsed = new URL(absolute);
    } catch {
      return;
    }

    const path = parsed.pathname.toLowerCase();
    if (path === '/' || FILE_EXTENSIONS.test(path)) return;

    const linkText = ($(element).text() || '').toLowerCase().trim().slice(0, 80);
    let score = 0;

    for (const [index, hint] of CONTACT_HINTS.entries()) {
      const weight = CONTACT_HINTS.length - index;
      if (path.includes(hint)) score += weight * 2;
      if (linkText.includes(hint.replace(/-/g, ' '))) score += weight;
    }

    if (score === 0) return;
    // Prefer shallow URLs: /contact beats /blog/2019/contact-form-tips.
    score -= (path.split('/').filter(Boolean).length - 1) * 3;

    const existing = scored.get(absolute) ?? 0;
    if (score > existing) scored.set(absolute, score);
  });

  return [...scored.entries()]
    .sort((a, b) => b[1] - a[1])
    .slice(0, limit)
    .map(([url]) => url);
}

/** Locates a public contact form (a form with an email/message field). */
export function findContactFormUrl($: CheerioAPI, pageUrl: string): string | null {
  let result: string | null = null;

  $('form').each((_, element) => {
    if (result) return;

    const form = $(element);
    const hasEmailField =
      form.find('input[type="email"], input[name*="email" i], input[id*="email" i]').length > 0;
    const hasMessageField =
      form.find('textarea, input[name*="message" i], input[name*="enquiry" i]').length > 0;

    // A search box has neither; a login form lacks the message field.
    if (!hasEmailField || !hasMessageField) return;
    if (form.find('input[type="password"]').length > 0) return;

    const action = form.attr('action');
    result = action ? normalizeUrl(action, pageUrl) || pageUrl : pageUrl;
  });

  return result;
}

/** Reads emails out of JSON-LD `Organization`/`LocalBusiness` blocks. */
export function extractStructuredContacts($: CheerioAPI): { emails: string[]; phones: string[] } {
  const emails = new Set<string>();
  const phones = new Set<string>();

  const visit = (node: unknown, depth = 0): void => {
    if (depth > 6 || node === null || typeof node !== 'object') return;

    if (Array.isArray(node)) {
      for (const item of node) visit(item, depth + 1);
      return;
    }

    for (const [key, value] of Object.entries(node as Record<string, unknown>)) {
      if (typeof value === 'string') {
        const lowerKey = key.toLowerCase();
        if (lowerKey === 'email') {
          const email = value.replace(/^mailto:/i, '').trim().toLowerCase();
          if (isValidEmail(email)) emails.add(email);
        } else if (lowerKey === 'telephone' || lowerKey === 'phone') {
          const phone = normalizePhone(value);
          if (phone.replace(/\D/g, '').length >= 7) phones.add(phone);
        }
      } else {
        visit(value, depth + 1);
      }
    }
  };

  $('script[type="application/ld+json"]').each((_, element) => {
    const raw = $(element).contents().text().trim();
    if (!raw) return;
    try {
      visit(JSON.parse(raw) as unknown);
    } catch {
      // Malformed JSON-LD is common; ignore it.
    }
  });

  return { emails: [...emails], phones: [...phones] };
}

/** Normalises a WhatsApp link to the canonical `wa.me/<number>` form. */
export function canonicalWhatsApp(url: string): string {
  try {
    const parsed = new URL(url);
    const fromQuery = parsed.searchParams.get('phone');
    const fromPath = parsed.pathname.replace(/\//g, '');

    // chat.whatsapp.com/<invite> is a group link, not a number — keep as-is.
    if (parsed.hostname === 'chat.whatsapp.com') return url;

    const digits = (fromQuery ?? fromPath).replace(/\D/g, '');
    return digits.length >= 7 ? `https://wa.me/${digits}` : url;
  } catch {
    return url;
  }
}
