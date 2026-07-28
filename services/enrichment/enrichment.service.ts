import 'server-only';

import * as cheerio from 'cheerio';

import type { BusinessContact, BusinessStatus } from '@/types/business';
import type { AppSettings } from '@/types/settings';
import { sleep, withRetry } from '@/utils/async';
import { normalizeHost, normalizePhone } from '@/utils/normalize';
import {
  buildCrawlerUserAgent,
  createHttpClient,
  describeHttpError,
  isRetryableError,
} from '../http/http-client';
import {
  canonicalWhatsApp,
  extractEmails,
  extractPhones,
  extractSocialLinks,
  extractStructuredContacts,
  findContactFormUrl,
  findContactPageLinks,
  type SocialLinks,
} from './extractors';
import { getRobotsChecker } from './robots';
import { checkCrawlTarget } from './url-guard';

const MAX_HTML_BYTES = 3 * 1024 * 1024;

export interface EnrichmentResult {
  contact: BusinessContact;
  status: BusinessStatus;
  pagesVisited: number;
  pagesBlocked: number;
  elapsedMs: number;
  error: string | null;
  notes: string;
}

function emptyContact(): BusinessContact {
  return {
    email: null,
    additionalEmails: [],
    whatsapp: null,
    facebook: null,
    instagram: null,
    linkedin: null,
    twitter: null,
    youtube: null,
    contactFormUrl: null,
    websitePhones: [],
  };
}

function emptyResult(status: BusinessStatus, notes: string, error: string | null = null): EnrichmentResult {
  return {
    contact: emptyContact(),
    status,
    pagesVisited: 0,
    pagesBlocked: 0,
    elapsedMs: 0,
    error,
    notes,
  };
}

interface PageHarvest {
  emails: string[];
  phones: string[];
  social: SocialLinks;
  contactFormUrl: string | null;
  contactLinks: string[];
}

/**
 * Discovers publicly listed contact details from a business website.
 *
 * Scope is deliberately narrow: it fetches the homepage and a small number of
 * linked contact/about pages over plain HTTP, reads what the site publishes, and
 * stops. It honours robots.txt, sends an identifying User-Agent, paces requests,
 * and never attempts to authenticate, submit forms, or work around any access
 * control. Pages behind a login are simply not read.
 */
class EnrichmentService {
  async enrich(
    website: string,
    settings: AppSettings,
    signal: AbortSignal,
  ): Promise<EnrichmentResult> {
    const startedAt = Date.now();

    if (!website) return emptyResult('no-website', 'No website listed');

    let origin: string;
    let homepage: string;
    try {
      const parsed = new URL(website);
      origin = parsed.origin;
      homepage = parsed.toString();
    } catch {
      return emptyResult('enrichment-failed', 'Invalid website URL', 'Invalid URL');
    }

    const targetCheck = await checkCrawlTarget(homepage);
    if (!targetCheck.safe) {
      const reason = targetCheck.reason ?? 'Website host is not reachable';
      return emptyResult('enrichment-failed', reason, reason);
    }

    const userAgent = buildCrawlerUserAgent(settings.crawlerContactEmail);
    const client = createHttpClient({
      timeoutMs: settings.requestTimeoutMs,
      userAgent,
      maxContentLength: MAX_HTML_BYTES,
      maxRedirects: 4,
    });

    const robots = settings.respectRobotsTxt
      ? await getRobotsChecker(origin, userAgent, settings.requestTimeoutMs, signal)
      : { isAllowed: () => true, crawlDelayMs: null };

    // A site asking for a slower crawl gets it, even if our own delay is lower.
    const delayMs = Math.max(settings.delayMs, robots.crawlDelayMs ?? 0);

    const contact = emptyContact();
    const emails: string[] = [];
    const phones = new Set<string>();
    const social: SocialLinks = {};

    let pagesVisited = 0;
    let pagesBlocked = 0;
    let firstError: string | null = null;
    const notes: string[] = [];

    const visit = async (url: string): Promise<PageHarvest | null> => {
      signal.throwIfAborted();

      if (!robots.isAllowed(url)) {
        pagesBlocked += 1;
        return null;
      }

      try {
        const response = await withRetry(
          async () => client.get<string>(url, {
            signal,
            responseType: 'text',
            transformResponse: [(data: unknown) => data],
          }),
          {
            attempts: settings.retryAttempts,
            baseDelayMs: 600,
            signal,
            shouldRetry: isRetryableError,
          },
        );

        // 401/403 means the page is not public; respect that and move on.
        if (response.status === 401 || response.status === 403) {
          pagesBlocked += 1;
          return null;
        }
        if (response.status >= 400) {
          firstError ??= `HTTP ${response.status}`;
          return null;
        }

        const contentType = String(response.headers['content-type'] ?? '');
        if (contentType && !contentType.includes('html')) return null;
        if (typeof response.data !== 'string') return null;

        pagesVisited += 1;
        const $ = cheerio.load(response.data);
        const structured = extractStructuredContacts($);

        return {
          emails: [...structured.emails, ...extractEmails($, url)],
          phones: [...structured.phones, ...extractPhones($)],
          social: extractSocialLinks($, url),
          contactFormUrl: findContactFormUrl($, url),
          contactLinks: findContactPageLinks($, url, settings.maxPagesPerSite),
        };
      } catch (error) {
        if (error instanceof DOMException && error.name === 'AbortError') throw error;
        firstError ??= describeHttpError(error);
        return null;
      }
    };

    const absorb = (harvest: PageHarvest): void => {
      for (const email of harvest.emails) {
        if (!emails.includes(email)) emails.push(email);
      }
      for (const phone of harvest.phones) phones.add(phone);
      for (const [key, value] of Object.entries(harvest.social) as [keyof SocialLinks, string][]) {
        social[key] ??= value;
      }
      contact.contactFormUrl ??= harvest.contactFormUrl;
    };

    const home = await visit(homepage);
    if (home) absorb(home);

    if (!home && pagesBlocked > 0) {
      return {
        ...emptyResult('enrichment-failed', 'Blocked by robots.txt', firstError),
        pagesBlocked,
        elapsedMs: Date.now() - startedAt,
      };
    }

    // Follow a few contact/about pages — that's where addresses usually live.
    const budget = Math.max(0, settings.maxPagesPerSite - 1);
    const candidates = (home?.contactLinks ?? []).slice(0, budget);

    for (const candidate of candidates) {
      if (delayMs > 0) await sleep(delayMs, signal);
      const harvest = await visit(candidate);
      if (harvest) absorb(harvest);
    }

    if (pagesBlocked > 0) notes.push(`${pagesBlocked} page(s) not publicly accessible`);

    const siteHost = normalizeHost(homepage);
    const [primaryEmail, ...rest] = emails;

    contact.email = primaryEmail ?? null;
    contact.additionalEmails = rest.slice(0, 5);
    contact.websitePhones = [...phones].map(normalizePhone).slice(0, 5);
    contact.facebook = social.facebook ?? null;
    contact.instagram = social.instagram ?? null;
    contact.linkedin = social.linkedin ?? null;
    contact.twitter = social.twitter ?? null;
    contact.youtube = social.youtube ?? null;
    contact.whatsapp = social.whatsapp ? canonicalWhatsApp(social.whatsapp) : null;

    const foundAnything =
      Boolean(contact.email) ||
      contact.websitePhones.length > 0 ||
      Object.keys(social).length > 0 ||
      Boolean(contact.contactFormUrl);

    let status: BusinessStatus;
    if (pagesVisited === 0) status = 'enrichment-failed';
    else if (contact.email) status = 'enriched';
    else if (foundAnything) status = 'partial';
    else status = 'partial';

    if (pagesVisited === 0 && !firstError) firstError = 'No readable pages';
    if (siteHost) notes.push(`Crawled ${pagesVisited} page(s) on ${siteHost}`);

    return {
      contact,
      status,
      pagesVisited,
      pagesBlocked,
      elapsedMs: Date.now() - startedAt,
      error: pagesVisited === 0 ? firstError : null,
      notes: notes.join('; '),
    };
  }
}

export const enrichmentService = new EnrichmentService();
