import { fail, handle, ok, parseJson } from '@/lib/api/response';
import { toPeopleSearchJobSnapshot } from '@/lib/backend/people-search-job-mapper';
import type { BackendSearchJob } from '@/lib/backend/search-mapper';
import { backendRequest, problemMessage } from '@/lib/backend/server';
import type { BackendProblem } from '@/lib/backend/types';
import { jobCommandSchema } from '@/lib/validation/search.schema';

export const dynamic = 'force-dynamic';

interface RouteParams {
  params: Promise<{ jobId: string }>;
}

async function readJob(path: string, notFound: string): Promise<Response> {
  const { response } = await backendRequest(path);

  if (!response.ok) {
    const problem = (await response.json().catch(() => null)) as BackendProblem | null;
    const status = problem?.status ?? response.status;

    return fail(
      problemMessage(problem, notFound),
      status === 404 ? 'not_found' : 'backend_error',
      status,
    );
  }

  return ok(toPeopleSearchJobSnapshot((await response.json()) as BackendSearchJob));
}

/** The live snapshot the People Search page polls. */
export function GET(_request: Request, { params }: RouteParams): Promise<Response> {
  return handle(async () => {
    const { jobId } = await params;
    return readJob(`/api/linkedin/people-search-jobs/${jobId}`, 'Search not found.');
  });
}

/**
 * Asks a run to stop.
 *
 * Cooperative: since a people search is one atomic browser operation rather
 * than a per-item loop, the worker only notices at the end of that operation
 * — the response says "stopping" rather than "stopped" while a live worker
 * still holds the lease.
 */
export function POST(request: Request, { params }: RouteParams): Promise<Response> {
  return handle(async () => {
    const { jobId } = await params;
    await parseJson(request, jobCommandSchema);

    return stopJob(jobId);
  });
}

export function DELETE(_request: Request, { params }: RouteParams): Promise<Response> {
  return handle(async () => {
    const { jobId } = await params;
    return stopJob(jobId);
  });
}

async function stopJob(jobId: string): Promise<Response> {
  const { response } = await backendRequest(`/api/linkedin/people-search-jobs/${jobId}/stop`, {
    method: 'POST',
  });

  if (!response.ok) {
    const problem = (await response.json().catch(() => null)) as BackendProblem | null;
    const status = problem?.status ?? response.status;

    return fail(problemMessage(problem, 'Could not stop the search.'), 'backend_error', status);
  }

  return ok(toPeopleSearchJobSnapshot((await response.json()) as BackendSearchJob));
}
