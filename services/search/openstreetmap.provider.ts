import '@/lib/server-guard';

import { ProviderError } from '@/lib/errors';
import { getCategory } from '@/lib/constants/categories';
import { getCountryName } from '@/lib/constants/locations';
import type { ProviderBusiness, ProviderQuery, ResolvedLocation } from '@/types/search';
import { RateLimiter, withRetry } from '@/utils/async';
import { normalizeUrl } from '@/utils/normalize';
import { createHttpClient, describeHttpError, isRetryableError } from '../http/http-client';
import { buildMapsSearchUrl, type GeocodingProvider, type ProviderContext, type SearchProvider } from './provider';

const OVERPASS_ENDPOINTS = [
  'https://overpass-api.de/api/interpreter',
  'https://overpass.kumi.systems/api/interpreter',
];
const NOMINATIM_URL = 'https://nominatim.openstreetmap.org/search';

/**
 * Nominatim's usage policy allows at most 1 request/second, regardless of the
 * app's own rate-limit setting. Enforced process-wide.
 */
const nominatimLimiter = new RateLimiter(1100);

interface OverpassElement {
  type: 'node' | 'way' | 'relation';
  id: number;
  lat?: number;
  lon?: number;
  center?: { lat: number; lon: number };
  tags?: Record<string, string>;
}

interface OverpassResponse {
  elements?: OverpassElement[];
  remark?: string;
}

interface NominatimResult {
  lat?: string;
  lon?: string;
  display_name?: string;
  address?: Record<string, string>;
}

function tagValue(tags: Record<string, string>, ...keys: string[]): string {
  for (const key of keys) {
    const value = tags[key];
    if (value && value.trim()) return value.trim();
  }
  return '';
}

/** Assembles a street address from the OSM `addr:*` tag family. */
function buildAddress(tags: Record<string, string>): string {
  const street = [tagValue(tags, 'addr:housenumber'), tagValue(tags, 'addr:street')]
    .filter(Boolean)
    .join(' ');

  return [
    street,
    tagValue(tags, 'addr:suburb', 'addr:neighbourhood'),
    tagValue(tags, 'addr:city', 'addr:town', 'addr:village'),
    tagValue(tags, 'addr:postcode'),
  ]
    .filter(Boolean)
    .join(', ');
}

export class OpenStreetMapProvider implements SearchProvider, GeocodingProvider {
  readonly id = 'openstreetmap' as const;
  readonly label = 'OpenStreetMap';

  /** Community endpoints need no key, so this provider is always available. */
  readiness(): string | null {
    return null;
  }

  async search(query: ProviderQuery, context: ProviderContext): Promise<ProviderBusiness[]> {
    const { settings } = context;
    const category = getCategory(query.category);
    const filters = category?.osmFilters ?? [];
    if (filters.length === 0) {
      throw new ProviderError(`No OpenStreetMap mapping for category "${query.category}"`);
    }

    const overpassQuery = this.buildOverpassQuery(filters, query);
    const client = createHttpClient({
      timeoutMs: Math.max(settings.requestTimeoutMs, 30_000),
      headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
    });

    let lastError: unknown;

    // Community mirrors rate-limit aggressively; fall through to the next one.
    for (const endpoint of OVERPASS_ENDPOINTS) {
      context.signal.throwIfAborted();
      await context.rateLimiter.acquire(context.signal);

      try {
        const response = await withRetry(
          async () =>
            client.post<OverpassResponse>(
              endpoint,
              new URLSearchParams({ data: overpassQuery }).toString(),
              { signal: context.signal },
            ),
          {
            attempts: settings.retryAttempts,
            baseDelayMs: 1500,
            signal: context.signal,
            shouldRetry: isRetryableError,
          },
        );

        if (response.status === 429 || response.status === 504) {
          lastError = new ProviderError(`Overpass endpoint busy (HTTP ${response.status})`);
          continue;
        }
        if (response.status >= 400) {
          lastError = new ProviderError(`Overpass error: HTTP ${response.status}`);
          continue;
        }

        return this.toBusinesses(response.data.elements ?? [], query);
      } catch (error) {
        lastError = error;
        if (error instanceof DOMException && error.name === 'AbortError') throw error;
      }
    }

    throw new ProviderError(
      `OpenStreetMap search failed: ${describeHttpError(lastError)}. Community Overpass servers throttle heavy use — retry shortly or switch to Google Places.`,
    );
  }

  private buildOverpassQuery(filters: readonly string[], query: ProviderQuery): string {
    const { latitude, longitude } = query.location;
    const radius = Math.min(50_000, Math.max(1, query.radiusMeters));

    const clauses = filters
      .map((filter) => {
        const [key, value] = filter.split('=');
        // `nwr` matches nodes, ways and relations in one pass.
        return `  nwr["${key}"="${value}"](around:${radius},${latitude},${longitude});`;
      })
      .join('\n');

    // Request extra rows so post-filtering (unnamed POIs) still fills the quota.
    const limit = Math.min(500, Math.max(query.maxResults * 3, 50));

    return `[out:json][timeout:60];\n(\n${clauses}\n);\nout center tags ${limit};`;
  }

  private toBusinesses(
    elements: readonly OverpassElement[],
    query: ProviderQuery,
  ): ProviderBusiness[] {
    const results: ProviderBusiness[] = [];

    for (const element of elements) {
      if (results.length >= query.maxResults) break;

      const tags = element.tags ?? {};
      const name = tagValue(tags, 'name', 'brand', 'operator');
      // An unnamed POI is not a usable lead.
      if (!name) continue;

      const latitude = element.lat ?? element.center?.lat ?? null;
      const longitude = element.lon ?? element.center?.lon ?? null;
      const address = buildAddress(tags);

      results.push({
        externalId: `${element.type}/${element.id}`,
        name,
        category: query.categoryLabel,
        address: address || query.location.label,
        country:
          tagValue(tags, 'addr:country') || query.location.country,
        state: tagValue(tags, 'addr:state', 'addr:province') || query.location.state,
        city:
          tagValue(tags, 'addr:city', 'addr:town', 'addr:village') || query.location.city,
        phone: tagValue(tags, 'phone', 'contact:phone', 'contact:mobile'),
        website: normalizeUrl(tagValue(tags, 'website', 'contact:website', 'url')),
        latitude,
        longitude,
        // OSM carries no ratings; the columns stay empty rather than faked.
        rating: null,
        reviewCount: null,
        mapsUrl: buildMapsSearchUrl(name, address || query.location.label),
        source: 'openstreetmap',
      });
    }

    return results;
  }

  async resolve(
    query: { country: string; state?: string; city: string },
    context: ProviderContext,
  ): Promise<ResolvedLocation | null> {
    const client = createHttpClient({ timeoutMs: context.settings.requestTimeoutMs });

    await nominatimLimiter.acquire(context.signal);

    const response = await withRetry(
      async () =>
        client.get<NominatimResult[]>(NOMINATIM_URL, {
          signal: context.signal,
          params: {
            city: query.city,
            state: query.state || undefined,
            country: getCountryName(query.country),
            format: 'jsonv2',
            limit: 1,
            addressdetails: 1,
          },
        }),
      {
        attempts: context.settings.retryAttempts,
        baseDelayMs: 1200,
        signal: context.signal,
        shouldRetry: isRetryableError,
      },
    ).catch((error: unknown) => {
      throw new ProviderError(`Nominatim geocoding failed: ${describeHttpError(error)}`);
    });

    const first = response.data?.[0];
    if (!first?.lat || !first?.lon) return null;

    const latitude = Number.parseFloat(first.lat);
    const longitude = Number.parseFloat(first.lon);
    if (!Number.isFinite(latitude) || !Number.isFinite(longitude)) return null;

    const address = first.address ?? {};

    return {
      label: first.display_name ?? `${query.city}, ${query.country}`,
      country: query.country,
      state: address.state ?? address.region ?? query.state ?? '',
      city: address.city ?? address.town ?? address.village ?? query.city,
      latitude,
      longitude,
    };
  }
}

export const openStreetMapProvider = new OpenStreetMapProvider();
