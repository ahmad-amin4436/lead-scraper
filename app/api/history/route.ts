import { fail, handle, ok } from '@/lib/api/response';
import { toHistoryEntry, type BackendSearchJob } from '@/lib/backend/search-mapper';
import { backendRequest } from '@/lib/backend/server';
import type { BackendPagedResult } from '@/lib/backend/types';

export const dynamic = 'force-dynamic';

/** Statuses that belong in history; anything else is still in flight. */
const FINISHED = new Set(['Completed', 'Failed', 'Stopped']);

/**
 * Past search runs.
 *
 * Read straight from the search-job table rather than a separate history store:
 * a run and its history entry were always the same thing, and keeping two copies
 * meant they could disagree about whether a run had finished.
 */
export function GET(request: Request): Promise<Response> {
  return handle(async () => {
    const limitParam = new URL(request.url).searchParams.get('limit');
    const parsed = limitParam ? Number.parseInt(limitParam, 10) : NaN;
    const limit = Number.isFinite(parsed) && parsed > 0 ? Math.min(parsed, 100) : 50;

    const { response } = await backendRequest('/api/searches', {
      query: { page: 1, pageSize: limit },
    });

    if (!response.ok) {
      return fail('Could not load search history.', 'backend_error', response.status);
    }

    const page = (await response.json()) as BackendPagedResult<BackendSearchJob>;

    return ok(page.items.filter((job) => FINISHED.has(job.status)).map(toHistoryEntry));
  });
}

/** Clears every finished run. Active runs are left alone. */
export function DELETE(): Promise<Response> {
  return handle(async () => {
    const { response } = await backendRequest('/api/searches', { method: 'DELETE' });

    if (!response.ok) {
      return fail('Could not clear search history.', 'backend_error', response.status);
    }

    return ok({ cleared: (await response.json()) as number });
  });
}
