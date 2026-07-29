import '@/lib/server-guard';

import type { ResolvedLocation } from '@/types/search';
import { googlePlacesProvider } from './google-places.provider';
import { openStreetMapProvider } from './openstreetmap.provider';
import type { ProviderContext } from './provider';

/** Cache key for a country/state/city triple. */
function cacheKey(country: string, state: string | undefined, city: string): string {
  return `${country}|${state ?? ''}|${city}`.toLowerCase();
}

const globalForGeocode = globalThis as unknown as {
  leadmineGeocodeCache?: Map<string, ResolvedLocation>;
};

/**
 * Process-wide cache. Geocoding the same city on every job wastes quota and,
 * on Nominatim, burns the 1 req/sec budget the crawl needs.
 */
const cache = globalForGeocode.leadmineGeocodeCache ?? new Map<string, ResolvedLocation>();
globalForGeocode.leadmineGeocodeCache = cache;

export const geocodingService = {
  /**
   * Resolves a city to coordinates, preferring Google when a key is configured
   * and falling back to Nominatim.
   */
  async resolve(
    query: { country: string; state?: string; city: string },
    context: ProviderContext,
  ): Promise<ResolvedLocation | null> {
    const key = cacheKey(query.country, query.state, query.city);
    const cached = cache.get(key);
    if (cached) return cached;

    const providers = context.settings.googleApiKey
      ? [googlePlacesProvider, openStreetMapProvider]
      : [openStreetMapProvider];

    let lastError: unknown;

    for (const provider of providers) {
      try {
        const resolved = await provider.resolve(query, context);
        if (resolved) {
          cache.set(key, resolved);
          return resolved;
        }
      } catch (error) {
        if (error instanceof DOMException && error.name === 'AbortError') throw error;
        lastError = error;
      }
    }

    if (lastError) throw lastError;
    return null;
  },

  clearCache(): void {
    cache.clear();
  },
};
