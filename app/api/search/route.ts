import { headers } from 'next/headers';

import { handle, ok, parseJson } from '@/lib/api/response';
import { searchRequestSchema } from '@/lib/validation/search.schema';
import { settingsRepository } from '@/repositories/settings.repository';
import { jobManager } from '@/services/jobs/job-manager';
import { runQueuedJob } from '@/services/jobs/run-queued-job';
import { resolveProvider } from '@/services/search/provider-registry';
import type { SearchRequest } from '@/types/search';

export const dynamic = 'force-dynamic';

/**
 * Attempts to hand the run to the Netlify Background Function.
 *
 * Returns true only on a `202 Accepted`, which is what a background function
 * replies with when it has taken ownership of the work. Anything else — a 404
 * because we are not on Netlify, or a network error — means nobody picked it up.
 *
 * This probes the capability instead of reading `process.env.NETLIFY`, which is
 * set at build time but NOT inside the Next.js function runtime. That mismatch
 * meant production believed it was running locally: the search executed inline
 * in the request handler and was killed by the function timeout part-way
 * through, leaving the job stranded.
 */
async function triggerBackgroundFunction(jobId: string): Promise<boolean> {
  const host = (await headers()).get('host');
  if (!host) return false;

  const proto = host.startsWith('localhost') || host.startsWith('127.') ? 'http' : 'https';
  const url = `${proto}://${host}/.netlify/functions/run-search-background`;

  try {
    const response = await fetch(url, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ jobId }),
    });
    return response.status === 202;
  } catch (error) {
    console.warn('[api/search] background function unreachable:', error);
    return false;
  }
}

/**
 * Starts the search run.
 *
 * Preferred path is the Background Function (up to 15 minutes). When that isn't
 * available — `next dev`, `next start`, a container — the run executes in-process
 * fire-and-forget, which is safe on a long-lived host.
 */
async function startRun(jobId: string): Promise<void> {
  if (await triggerBackgroundFunction(jobId)) return;

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
