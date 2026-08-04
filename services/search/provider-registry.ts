  import '@/lib/server-guard';

import { ConfigurationError } from '@/lib/errors';
import type { BusinessSource } from '@/types/business';
import type { AppSettings } from '@/types/settings';
import { googlePlacesProvider } from './google-places.provider';
import { openStreetMapProvider } from './openstreetmap.provider';
import type { SearchProvider } from './provider';

/**
 * Google Maps (Apify) and its OSM-parallel variant run entirely in the .NET
 * backend's ProviderRegistry — actual scraping, the merge, and readiness
 * (whether Scraper:ApifyApiToken is configured) all happen there. This
 * Next.js-native registry has no visibility into that config, so `search`
 * here is intentionally unreachable: the real execution path never calls it
 * (see app/api/search/route.ts, which forwards the raw request to the .NET
 * API rather than running a provider in this process). `readiness` reports
 * optimistically; an unconfigured token surfaces as a specific run failure
 * instead of a pre-flight warning in this dropdown.
 */
function apifyBackedProvider(id: 'apify' | 'apify-parallel', label: string): SearchProvider {
  return {
    id,
    label,
    readiness: () => null,
    search: () => {
      throw new ConfigurationError(`${label} runs in the .NET backend, not this Next.js process.`);
    },
  };
}

const PROVIDERS: Record<Exclude<BusinessSource, 'manual'>, SearchProvider> = {
  'google-places': googlePlacesProvider,
  openstreetmap: openStreetMapProvider,
  apify: apifyBackedProvider('apify', 'Google Maps (Apify)'),
  'apify-parallel': apifyBackedProvider('apify-parallel', 'Google Maps + OpenStreetMap (parallel)'),
};

export function getProvider(id: BusinessSource): SearchProvider {
  const provider = id === 'manual' ? undefined : PROVIDERS[id];
  if (!provider) throw new ConfigurationError(`Unknown search provider "${id}"`);
  return provider;
}

/**
 * Picks the provider for a run: the explicit choice, else the configured
 * default, falling back to OpenStreetMap when Google has no key.
 */
export function resolveProvider(
  requested: BusinessSource | undefined,
  settings: AppSettings,
): SearchProvider {
  const candidate = requested ?? settings.defaultProvider;
  const provider = getProvider(candidate === 'manual' ? 'openstreetmap' : candidate);

  if (provider.readiness(settings) === null) return provider;

  // An explicit request should surface the configuration error rather than
  // silently searching a different data source.
  if (requested) {
    throw new ConfigurationError(provider.readiness(settings) ?? 'Provider unavailable');
  }

  return openStreetMapProvider;
}

/**
 * Data sources not offered in the "Data source" dropdown — not because they
 * don't work, but because the current search pipeline uses Google Places as
 * the sole discovery engine, with Google Maps/LinkedIn as opt-in enrichment
 * toggles instead (see the "Enrichment (Apify)" section of the search form).
 * Picking one of these as the *discovery* provider would double up with that
 * design rather than complement it. The backend still understands both ids,
 * so this is a presentation choice, not a removal.
 */
const HIDDEN_FROM_DROPDOWN = new Set<BusinessSource>(['apify', 'apify-parallel']);

export function listProviders(settings: AppSettings): {
  id: BusinessSource;
  label: string;
  ready: boolean;
  reason: string | null;
}[] {
  return Object.values(PROVIDERS)
    .filter((provider) => !HIDDEN_FROM_DROPDOWN.has(provider.id))
    .map((provider) => {
      const reason = provider.readiness(settings);
      return { id: provider.id, label: provider.label, ready: reason === null, reason };
    });
}
