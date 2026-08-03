import { fail, handle, ok, parseJson } from '@/lib/api/response';
import {
  toCreateJobRequest,
  toJobSnapshot,
  type BackendSearchJob,
} from '@/lib/backend/search-mapper';
import { backendRequest, problemMessage } from '@/lib/backend/server';
import type { BackendProblem } from '@/lib/backend/types';
import { searchRequestSchema } from '@/lib/validation/search.schema';
import type { SearchRequest } from '@/types/search';

export const dynamic = 'force-dynamic';

/**
 * Queues a search run.
 *
 * Queuing only writes a row; the scraper inside the .NET API claims it and
 * executes it. Nothing runs in this Next.js process, which is what lets the
 * browser close, the page navigate away, or this front end redeploy without
 * affecting a sweep in progress.
 */
export function POST(request: Request): Promise<Response> {
  return handle(async () => {
    const input = await parseJson(request, searchRequestSchema);
    const searchRequest = input as SearchRequest;

    const { response } = await backendRequest('/api/searches', {
      method: 'POST',
      body: JSON.stringify(toCreateJobRequest(searchRequest)),
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
    return ok(toJobSnapshot(job), { status: 202 });
  });
}

/** Runs that are queued, running or stopping — the caller's own. */
export function GET(): Promise<Response> {
  return handle(async () => {
    const { response } = await backendRequest('/api/searches/active');

    if (!response.ok) {
      return fail('Could not load active searches.', 'backend_error', response.status);
    }

    const jobs = (await response.json()) as BackendSearchJob[];
    return ok(jobs.map(toJobSnapshot));
  });
}
