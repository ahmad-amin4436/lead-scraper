import '@/lib/server-guard';

import { backendBaseUrl } from '@/lib/backend/config';
import type { BusinessRecord } from '@/types/business';

export interface IngestOutcome {
  saved: number;
  duplicates: number;
  rejected: number;
}

/**
 * Writes scraped leads into SQL Server through the .NET API.
 *
 * The scraper runs as a background worker with no user session, so it cannot
 * present a JWT. It authenticates with `LEADMINE_SERVICE_KEY` and names the
 * owner explicitly — the backend's ingest endpoint is the only place a caller
 * may do that, and it is unreachable without the key.
 *
 * This replaced writing to Netlify Blobs / a JSON file: leads are relational
 * data that need per-user scoping, indexing and joins, none of which a blob can
 * provide.
 */
export function isLeadSinkConfigured(): boolean {
  return Boolean(process.env.LEADMINE_SERVICE_KEY?.trim());
}

/** Enum names the .NET API expects, mapped from the local lowercase values. */
const EMAIL_STATUS: Record<string, string> = {
  unverified: 'Unverified',
  valid: 'Valid',
  risky: 'Risky',
  invalid: 'Invalid',
  unknown: 'Unknown',
};

const WHATSAPP_STATUS: Record<string, string> = {
  unverified: 'Unverified',
  confirmed: 'Confirmed',
  likely: 'Likely',
  unlikely: 'Unlikely',
  none: 'None',
};

const SOURCE: Record<string, string> = {
  manual: 'Manual',
  'google-places': 'GooglePlaces',
  openstreetmap: 'OpenStreetMap',
};

const STATUS: Record<string, string> = {
  new: 'New',
  enriched: 'Enriched',
  partial: 'Partial',
  'no-website': 'NoWebsite',
  'enrichment-failed': 'EnrichmentFailed',
};

function toIngestLead(record: BusinessRecord): Record<string, unknown> {
  return {
    name: record.name,
    category: record.category,
    country: record.country,
    state: record.state,
    city: record.city,
    address: record.address,
    phone: record.phone,
    website: record.website,
    email: record.email,
    emailStatus: EMAIL_STATUS[record.emailStatus] ?? 'Unverified',
    whatsApp: record.whatsapp,
    whatsAppStatus: WHATSAPP_STATUS[record.whatsappStatus] ?? 'Unverified',
    facebook: record.facebook,
    instagram: record.instagram,
    linkedIn: record.linkedin,
    latitude: record.latitude,
    longitude: record.longitude,
    rating: record.rating,
    reviewCount: record.reviewCount,
    mapsUrl: record.mapsUrl,
    notes: record.notes,
    source: SOURCE[record.source] ?? 'Manual',
    status: STATUS[record.status] ?? 'New',
  };
}

/**
 * Sends one batch. Throws on failure so the caller can decide whether to retry
 * or record the loss — silently dropping scraped leads would be worse than a
 * visible error.
 */
export async function ingestLeads(
  records: readonly BusinessRecord[],
  options: { ownerUserId: string; searchJobId?: string; skipDuplicates?: boolean },
): Promise<IngestOutcome> {
  if (records.length === 0) return { saved: 0, duplicates: 0, rejected: 0 };

  const serviceKey = process.env.LEADMINE_SERVICE_KEY?.trim();
  if (!serviceKey) {
    throw new Error(
      'LEADMINE_SERVICE_KEY is not set, so scraped leads cannot be saved to the database.',
    );
  }

  const response = await fetch(`${backendBaseUrl()}/api/ingest/leads`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      'X-Service-Key': serviceKey,
    },
    body: JSON.stringify({
      ownerUserId: options.ownerUserId,
      searchJobId: options.searchJobId,
      skipDuplicates: options.skipDuplicates ?? true,
      leads: records.map(toIngestLead),
    }),
  });

  if (!response.ok) {
    const body = await response.text().catch(() => '');
    throw new Error(`Lead ingest failed (HTTP ${response.status}). ${body.slice(0, 200)}`);
  }

  const result = (await response.json()) as IngestOutcome;
  return {
    saved: result.saved ?? 0,
    duplicates: result.duplicates ?? 0,
    rejected: result.rejected ?? 0,
  };
}
