import { handle, ok, parseJson } from '@/lib/api/response';
import { searchRequestSchema } from '@/lib/validation/search.schema';
import { settingsRepository } from '@/repositories/settings.repository';
import { jobManager } from '@/services/jobs/job-manager';
import { runSearchJob } from '@/services/jobs/search-runner';
import { resolveProvider } from '@/services/search/provider-registry';
import type { SearchRequest } from '@/types/search';

export const dynamic = 'force-dynamic';

/** Starts a search run and returns immediately with the job snapshot. */
export function POST(request: Request): Promise<Response> {
  return handle(async () => {
    const input = await parseJson(request, searchRequestSchema);
    const settings = await settingsRepository.get();

    // Throws a 400 when an explicitly requested provider isn't configured.
    const provider = resolveProvider(input.provider, settings);

    const searchRequest: SearchRequest = { ...input, provider: provider.id };
    const job = jobManager.create(searchRequest);

    // Fire and forget: the runner drives the job and streams progress over SSE.
    void runSearchJob(job, provider, settings).catch((error: unknown) => {
      console.error('[api/search] job runner crashed:', error);
    });

    return ok(job.snapshot, { status: 202 });
  });
}

export function GET(): Promise<Response> {
  return handle(async () => ok(jobManager.list()));
}
