import 'server-only';

import { ConfigurationError } from '@/lib/errors';
import type { BusinessSource } from '@/types/business';
import type { AppSettings } from '@/types/settings';
import { googlePlacesProvider } from './google-places.provider';
import { openStreetMapProvider } from './openstreetmap.provider';
import type { SearchProvider } from './provider';

const PROVIDERS: Record<Exclude<BusinessSource, 'manual'>, SearchProvider> = {
  'google-places': googlePlacesProvider,
  openstreetmap: openStreetMapProvider,
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

export function listProviders(settings: AppSettings): {
  id: BusinessSource;
  label: string;
  ready: boolean;
  reason: string | null;
}[] {
  return Object.values(PROVIDERS).map((provider) => {
    const reason = provider.readiness(settings);
    return { id: provider.id, label: provider.label, ready: reason === null, reason };
  });
}
