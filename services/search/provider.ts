import '@/lib/server-guard';

import type { BusinessSource } from '@/types/business';
import type { ProviderBusiness, ProviderQuery, ResolvedLocation } from '@/types/search';
import type { AppSettings } from '@/types/settings';
import type { RateLimiter } from '@/utils/async';

export interface ProviderContext {
  settings: AppSettings;
  rateLimiter: RateLimiter;
  signal: AbortSignal;
  jobId: string | null;
}

export interface SearchProvider {
  readonly id: BusinessSource;
  readonly label: string;
  /** Human-readable reason the provider can't run, or null when it's ready. */
  readiness(settings: AppSettings): string | null;
  search(query: ProviderQuery, context: ProviderContext): Promise<ProviderBusiness[]>;
}

export interface GeocodingProvider {
  resolve(
    query: { country: string; state?: string; city: string },
    context: ProviderContext,
  ): Promise<ResolvedLocation | null>;
}

/** Google Maps deep link built from a name + address (works without a place id). */
export function buildMapsSearchUrl(name: string, address: string): string {
  const query = [name, address].filter(Boolean).join(', ');
  return `https://www.google.com/maps/search/?api=1&query=${encodeURIComponent(query)}`;
}
