import { fail, handle, ok, parseJson } from '@/lib/api/response';
import { toJobSnapshot, type BackendSearchJob } from '@/lib/backend/search-mapper';
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

  return ok(toJobSnapshot((await response.json()) as BackendSearchJob));
}

/** The live snapshot the progress panel polls. */
export function GET(_request: Request, { params }: RouteParams): Promise<Response> {
  return handle(async () => {
    const { jobId } = await params;
    return readJob(`/api/searches/${jobId}`, 'Search run not found.');
  });
}

/**
 * Asks a run to stop.
 *
 * Cooperative: the worker notices at its next checkpoint, so the response says
 * "stopping" rather than "stopped" while a live worker still holds the lease.
 */
export function POST(request: Request, { params }: RouteParams): Promise<Response> {
  return handle(async () => {
    const { jobId } = await params;
    // Parsed for validation — `stop` is currently the only command, and an
    // unrecognised one should be rejected here rather than reaching the backend.
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
  const { response } = await backendRequest(`/api/searches/${jobId}/stop`, { method: 'POST' });

  if (!response.ok) {
    const problem = (await response.json().catch(() => null)) as BackendProblem | null;
    const status = problem?.status ?? response.status;

    return fail(problemMessage(problem, 'Could not stop the run.'), 'backend_error', status);
  }

  return ok(toJobSnapshot((await response.json()) as BackendSearchJob));
}
