import { handle, ok } from '@/lib/api/response';
import { backendBaseUrl } from '@/lib/backend/config';
import { blobStore } from '@/lib/storage/blob-store';
import { businessRepository } from '@/repositories/business.repository';
import { settingsRepository } from '@/repositories/settings.repository';
import { listProviders } from '@/services/search/provider-registry';

export const dynamic = 'force-dynamic';

/**
 * Liveness for the front end and its local stores.
 *
 * It deliberately says nothing about running searches. Those live in the .NET
 * API's job table now, and reporting a second, staler answer from here would
 * invite someone to trust the wrong one.
 */
export function GET(): Promise<Response> {
  return handle(async () => {
    const settings = await settingsRepository.get();
    const stats = await businessRepository.stats();

    let backend: 'ok' | 'unreachable' = 'unreachable';
    try {
      const response = await fetch(`${backendBaseUrl()}/api/health`, { cache: 'no-store' });
      if (response.ok) backend = 'ok';
    } catch {
      // Left as unreachable — this endpoint reports status, it does not fail on it.
    }

    return ok({
      status: 'ok',
      // Where searches and leads actually live.
      backend,
      // The local backend actually in use, not a guess from env vars.
      // `filesystem` on a serverless host means data will not survive between
      // invocations.
      storage: blobStore.kind,
      storageDurable: blobStore.kind === 'netlify-blobs',
      records: stats.total,
      providers: listProviders(settings),
      timestamp: new Date().toISOString(),
    });
  });
}
