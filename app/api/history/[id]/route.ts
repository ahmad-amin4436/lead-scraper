import { fail, handle, ok } from '@/lib/api/response';
import { toHistoryEntry, type BackendSearchJob } from '@/lib/backend/search-mapper';
import { backendRequest, problemMessage } from '@/lib/backend/server';
import type { BackendProblem } from '@/lib/backend/types';

export const dynamic = 'force-dynamic';

interface RouteParams {
  params: Promise<{ id: string }>;
}

/**
 * One past run.
 *
 * The id is the search job's own id — history is a view over the job table, not
 * a separate record with its own lifetime.
 */
export function GET(_request: Request, { params }: RouteParams): Promise<Response> {
  return handle(async () => {
    const { id } = await params;

    const { response } = await backendRequest(`/api/searches/${id}`);

    if (!response.ok) {
      const problem = (await response.json().catch(() => null)) as BackendProblem | null;
      const status = problem?.status ?? response.status;

      return fail(
        problemMessage(problem, 'History entry not found.'),
        status === 404 ? 'not_found' : 'backend_error',
        status,
      );
    }

    return ok(toHistoryEntry((await response.json()) as BackendSearchJob));
  });
}

export function DELETE(_request: Request, { params }: RouteParams): Promise<Response> {
  return handle(async () => {
    const { id } = await params;

    const { response } = await backendRequest(`/api/searches/${id}`, { method: 'DELETE' });

    if (!response.ok) {
      const problem = (await response.json().catch(() => null)) as BackendProblem | null;
      const status = problem?.status ?? response.status;

      return fail(
        problemMessage(problem, 'Could not delete that run.'),
        status === 404 ? 'not_found' : 'backend_error',
        status,
      );
    }

    return ok({ removed: true });
  });
}
