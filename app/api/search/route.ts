import { headers } from 'next/headers';

import { handle, ok, parseJson } from '@/lib/api/response';
import { searchRequestSchema } from '@/lib/validation/search.schema';
import { settingsRepository } from '@/repositories/settings.repository';
import { jobManager } from '@/services/jobs/job-manager';
import { runQueuedJob } from '@/services/jobs/run-queued-job';
import { resolveProvider } from '@/services/search/provider-registry';
import type { SearchRequest } from '@/types/search';

export const dynamic = 'force-dynamic';

/** True on Netlify, where a background function runs the search out-of-band. */
const ON_NETLIFY = Boolean(process.env.NETLIFY);

/**
 * Starts the search run.
 *
 * On Netlify the search executes in a Background Function (up to 15 min), so the
 * request just triggers it and returns. Locally (`next dev`, `next start`) there
 * is no background function, so the run executes in-process — fire-and-forget —
 * which is safe on a long-lived host and keeps dev behaviour identical.
 */
async function startRun(jobId: string): Promise<void> {
  if (ON_NETLIFY) {
    const host = (await headers()).get('host');
    const proto = host?.startsWith('localhost') || host?.startsWith('127.') ? 'http' : 'https';
    const url = `${proto}://${host}/.netlify/functions/run-search-background`;

    // Fire the background function. It returns 202 immediately and continues on
    // its own; we don't await its completion.
    await fetch(url, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ jobId }),
    }).catch((error: unknown) => {
      console.error('[api/search] failed to trigger background function:', error);
    });
    return;
  }

  // Local / long-lived host: run inline without blocking the response.
  void runQueuedJob(jobId).catch((error: unknown) => {
    console.error('[api/search] inline job runner crashed:', error);
  });
}

/** Starts a search run and returns immediately with the job snapshot. */
export function POST(request: Request): Promise<Response> {
  return handle(async () => {
    const input = await parseJson(request, searchRequestSchema);
    const settings = await settingsRepository.get();

    // Throws a 400 when an explicitly requested provider isn't configured.
    const provider = resolveProvider(input.provider, settings);

    const searchRequest: SearchRequest = { ...input, provider: provider.id };
    const job = await jobManager.create(searchRequest);

    await startRun(job.id);

    return ok(job.snapshot, { status: 202 });
  });
}

export function GET(): Promise<Response> {
  return handle(async () => ok(await jobManager.list()));
}
