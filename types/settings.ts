import type { BusinessSource } from './business';

export interface AppSettings {
  /** Google Places / Geocoding API key. Never sent to the client in full. */
  googleApiKey: string;
  defaultProvider: BusinessSource;
  /** Contact email advertised in the enrichment crawler's User-Agent. */
  crawlerContactEmail: string;
  /** Parallel website fetches during enrichment. */
  concurrency: number;
  /** Delay applied between outbound requests to the same host, ms. */
  delayMs: number;
  /** Max requests per minute against a search provider. */
  rateLimitPerMinute: number;
  retryAttempts: number;
  requestTimeoutMs: number;
  /** Max pages crawled per website during enrichment (homepage included). */
  maxPagesPerSite: number;
  respectRobotsTxt: boolean;
  enrichmentEnabledByDefault: boolean;
  skipDuplicatesByDefault: boolean;
  defaultRadiusMeters: number;
  defaultMaxResults: number;
  updatedAt: string;
}

/** Shape returned to the browser: the key is masked, never raw. */
export interface PublicAppSettings extends Omit<AppSettings, 'googleApiKey'> {
  googleApiKeyMasked: string;
  googleApiKeyConfigured: boolean;
  /** True when the key comes from an env var and cannot be edited in the UI. */
  googleApiKeyFromEnv: boolean;
}

export const DEFAULT_SETTINGS: AppSettings = {
  googleApiKey: '',
  defaultProvider: 'openstreetmap',
  crawlerContactEmail: '',
  concurrency: 4,
  delayMs: 400,
  rateLimitPerMinute: 120,
  retryAttempts: 3,
  requestTimeoutMs: 15000,
  maxPagesPerSite: 4,
  respectRobotsTxt: true,
  enrichmentEnabledByDefault: true,
  skipDuplicatesByDefault: true,
  defaultRadiusMeters: 10000,
  defaultMaxResults: 60,
  updatedAt: new Date(0).toISOString(),
};
