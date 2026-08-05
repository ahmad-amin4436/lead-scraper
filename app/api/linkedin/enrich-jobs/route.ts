import { fail, handle, ok, parseJson } from '@/lib/api/response';
import { toLinkedInJobSnapshot } from '@/lib/backend/linkedin-job-mapper';
import type { BackendSearchJob } from '@/lib/backend/search-mapper';
import { backendRequest, problemMessage } from '@/lib/backend/server';
import type { BackendProblem } from '@/lib/backend/types';
import { startLinkedInEnrichmentSchema } from '@/lib/validation/linkedin.schema';

export const dynamic = 'force-dynamic';

/**
 * Queues a LinkedIn enrichment batch.
 *
 * Queuing only writes a row; the scraper worker inside the .NET API claims it
 * and executes it — same reason `POST /api/search` is fire-and-forget rather
 * than blocking: enriching up to 25 leads means up to 25 real LinkedIn browser
 * sessions, which cannot reliably finish inside this route's own request
 * lifetime, let alone a proxy in front of it.
 */
export function POST(request: Request): Promise<Response> {
  return handle(async () => {
    const input = await parseJson(request, startLinkedInEnrichmentSchema);

    const { response } = await backendRequest('/api/linkedin/enrich-jobs', {
      method: 'POST',
      body: JSON.stringify(input),
    });

    if (!response.ok) {
      const problem = (await response.json().catch(() => null)) as BackendProblem | null;
      const status = problem?.status ?? response.status;

      return fail(
        problemMessage(problem, 'Could not start the LinkedIn enrichment batch.'),
        status === 409 ? 'conflict' : 'backend_error',
        status,
      );
    }

    const job = (await response.json()) as BackendSearchJob;
    return ok(toLinkedInJobSnapshot(job), { status: 202 });
  });
}

/** Batches that are queued, running or stopping — the caller's own. */
export function GET(): Promise<Response> {
  return handle(async () => {
    const { response } = await backendRequest('/api/linkedin/enrich-jobs/active');

    if (!response.ok) {
      return fail('Could not load active LinkedIn enrichment batches.', 'backend_error', response.status);
    }

    const jobs = (await response.json()) as BackendSearchJob[];
    return ok(jobs.map(toLinkedInJobSnapshot));
  });
}
