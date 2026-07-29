import { handle, ok } from '@/lib/api/response';
import { blobStore } from '@/lib/storage/blob-store';
import { businessRepository } from '@/repositories/business.repository';
import { settingsRepository } from '@/repositories/settings.repository';
import { jobManager } from '@/services/jobs/job-manager';
import { listProviders } from '@/services/search/provider-registry';

export const dynamic = 'force-dynamic';

export function GET(): Promise<Response> {
  return handle(async () => {
    const settings = await settingsRepository.get();
    const stats = await businessRepository.stats();
    const active = await jobManager.getActive();

    return ok({
      status: 'ok',
      // The backend actually in use, not a guess from env vars. `filesystem` on
      // a serverless host means data will not survive between invocations.
      storage: blobStore.kind,
      storageDurable: blobStore.kind === 'netlify-blobs',
      records: stats.total,
      activeJobId: active?.id ?? null,
      providers: listProviders(settings),
      timestamp: new Date().toISOString(),
    });
  });
}
