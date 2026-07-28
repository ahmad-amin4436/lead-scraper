import 'server-only';

import { KEYS } from '@/lib/paths';
import { DEFAULT_SETTINGS, type AppSettings, type PublicAppSettings } from '@/types/settings';
import { JsonStore } from './json-store';

const store = new JsonStore<AppSettings>(
  KEYS.settings,
  () => ({ ...DEFAULT_SETTINGS }),
  (raw) => ({ ...DEFAULT_SETTINGS, ...(raw as Partial<AppSettings>) }),
);

/** `GOOGLE_PLACES_API_KEY` wins over the stored value and locks the UI field. */
function envApiKey(): string {
  return (process.env.GOOGLE_PLACES_API_KEY ?? process.env.GOOGLE_MAPS_API_KEY ?? '').trim();
}

export function isApiKeyFromEnv(): boolean {
  return envApiKey().length > 0;
}

function maskKey(key: string): string {
  if (!key) return '';
  if (key.length <= 8) return '•'.repeat(key.length);
  return `${key.slice(0, 4)}${'•'.repeat(Math.max(4, key.length - 8))}${key.slice(-4)}`;
}

export const settingsRepository = {
  /** Server-side settings, with the effective API key resolved. */
  async get(): Promise<AppSettings> {
    const stored = await store.read();
    const fromEnv = envApiKey();
    return fromEnv ? { ...stored, googleApiKey: fromEnv } : stored;
  },

  /** Client-safe projection: the key is masked, never transmitted raw. */
  async getPublic(): Promise<PublicAppSettings> {
    const settings = await this.get();
    const { googleApiKey, ...rest } = settings;
    return {
      ...rest,
      googleApiKeyMasked: maskKey(googleApiKey),
      googleApiKeyConfigured: googleApiKey.length > 0,
      googleApiKeyFromEnv: isApiKeyFromEnv(),
    };
  },

  /**
   * Merges a partial update. An `undefined` or empty `googleApiKey` preserves
   * the stored key so the UI can submit the form without re-entering it.
   */
  async update(patch: Partial<AppSettings>): Promise<AppSettings> {
    const next = await store.update((current) => {
      const merged: AppSettings = {
        ...current,
        ...patch,
        updatedAt: new Date().toISOString(),
      };

      if (patch.googleApiKey === undefined || patch.googleApiKey.trim() === '') {
        merged.googleApiKey = current.googleApiKey;
      } else {
        merged.googleApiKey = patch.googleApiKey.trim();
      }

      return merged;
    });

    const fromEnv = envApiKey();
    return fromEnv ? { ...next, googleApiKey: fromEnv } : next;
  },

  async clearApiKey(): Promise<AppSettings> {
    return store.update((current) => ({
      ...current,
      googleApiKey: '',
      updatedAt: new Date().toISOString(),
    }));
  },
};
