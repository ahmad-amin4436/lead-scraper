import type { Config } from '@netlify/functions';

import { runQueuedJob } from '@/services/jobs/run-queued-job';

/**
 * Netlify Background Function that executes a queued search run.
 *
 * Background functions run for up to 15 minutes and return an immediate 202,
 * which is exactly what a long lead-scraping sweep needs — a regular function
 * (or Next.js `after`) would be killed at the ~10–26s route timeout.
 *
 * The `POST /api/search` route triggers this with `{ jobId }`; all state
 * (progress, results, the stop flag) flows through Netlify Blobs, so this worker
 * only needs the id. Netlify's function bundler resolves the `@/` tsconfig alias,
 * so it shares the app's own job/runner code with no duplication.
 */
const handler = async (req: Request): Promise<Response> => {
  let jobId: string | undefined;
  try {
    const body = (await req.json()) as { jobId?: string };
    jobId = body.jobId;
  } catch {
    return new Response('Invalid JSON body', { status: 400 });
  }

  if (!jobId) {
    return new Response('Missing jobId', { status: 400 });
  }

  try {
    await runQueuedJob(jobId);
  } catch (error) {
    // The run marks the job failed on its own; log for observability.
    console.error(`[run-search-background] job "${jobId}" crashed:`, error);
  }

  return new Response('ok');
};

export default handler;

export const config: Config = {
  // The `-background` suffix already makes this a background function; declaring
  // it explicitly documents intent and is honoured by newer runtimes.
  path: '/.netlify/functions/run-search-background',
};
