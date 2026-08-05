import { fail, handle, ok, parseJson } from '@/lib/api/response';
import { toPeopleSearchJobSnapshot } from '@/lib/backend/people-search-job-mapper';
import type { BackendSearchJob } from '@/lib/backend/search-mapper';
import { backendRequest, problemMessage } from '@/lib/backend/server';
import type { BackendProblem } from '@/lib/backend/types';
import { startPeopleSearchSchema } from '@/lib/validation/linkedin.schema';

export const dynamic = 'force-dynamic';

/**
 * Queues a company-scoped LinkedIn people search.
 *
 * Queuing only writes a row; the scraper worker inside the .NET API claims it
 * and executes it — same reason enrichment batches and searches are
 * fire-and-forget rather than blocking: a real LinkedIn browser session
 * cannot reliably finish inside this route's own request lifetime.
 */
export function POST(request: Request): Promise<Response> {
  return handle(async () => {
    const input = await parseJson(request, startPeopleSearchSchema);

    const { response } = await backendRequest('/api/linkedin/people-search-jobs', {
      method: 'POST',
      body: JSON.stringify(input),
    });

    if (!response.ok) {
      const problem = (await response.json().catch(() => null)) as BackendProblem | null;
      const status = problem?.status ?? response.status;

      return fail(
        problemMessage(problem, 'Could not start the search.'),
        status === 409 ? 'conflict' : 'backend_error',
        status,
      );
    }

    const job = (await response.json()) as BackendSearchJob;
    return ok(toPeopleSearchJobSnapshot(job), { status: 202 });
  });
}

/** Runs that are queued, running or stopping — the caller's own. */
export function GET(): Promise<Response> {
  return handle(async () => {
    const { response } = await backendRequest('/api/linkedin/people-search-jobs/active');

    if (!response.ok) {
      return fail('Could not load active people-search runs.', 'backend_error', response.status);
    }

    const jobs = (await response.json()) as BackendSearchJob[];
    return ok(jobs.map(toPeopleSearchJobSnapshot));
  });
}
