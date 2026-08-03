import { fail, handle, ok } from '@/lib/api/response';
import { toHistoryEntry, type BackendSearchJob } from '@/lib/backend/search-mapper';
import { backendRequest } from '@/lib/backend/server';
import type { BackendPagedResult } from '@/lib/backend/types';

export const dynamic = 'force-dynamic';

function parsePositiveInt(value: string | null, fallback: number, max: number): number {
  const parsed = value ? Number.parseInt(value, 10) : NaN;
  return Number.isFinite(parsed) && parsed > 0 ? Math.min(parsed, max) : fallback;
}

/**
 * Past search runs, paged.
 *
 * Read straight from the search-job table rather than a separate history
 * store: a run and its history entry were always the same thing, and keeping
 * two copies meant they could disagree about whether a run had finished. The
 * backend already excludes runs still in flight, so `total`/`pageCount` here
 * are accurate for real pagination rather than an estimate.
 */
export function GET(request: Request): Promise<Response> {
  return handle(async () => {
    const params = new URL(request.url).searchParams;
    const page = parsePositiveInt(params.get('page'), 1, 100_000);
    const pageSize = parsePositiveInt(params.get('pageSize'), 25, 100);

    const { response } = await backendRequest('/api/searches', {
      query: { page, pageSize },
    });

    if (!response.ok) {
      return fail('Could not load search history.', 'backend_error', response.status);
    }

    const result = (await response.json()) as BackendPagedResult<BackendSearchJob>;

    return ok({
      items: result.items.map(toHistoryEntry),
      total: result.total,
      page: result.page,
      pageSize: result.pageSize,
      pageCount: result.pageCount,
    });
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
