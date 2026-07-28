import { handle, ok } from '@/lib/api/response';
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
      storage: process.env.NETLIFY ? 'netlify-blobs' : 'filesystem',
      records: stats.total,
      activeJobId: active?.id ?? null,
      providers: listProviders(settings),
      timestamp: new Date().toISOString(),
    });
  });
}
