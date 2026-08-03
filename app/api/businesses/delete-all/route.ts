import { fail, handle, ok, parseJson } from '@/lib/api/response';
import { toBackendQuery } from '@/lib/backend/business-mapper';
import { backendRequest, problemMessage } from '@/lib/backend/server';
import type { BackendProblem } from '@/lib/backend/types';
import { businessJsonFilterSchema } from '@/lib/validation/business.schema';

export const dynamic = 'force-dynamic';

/**
 * Deletes every lead matching the given filters, not just a page of them.
 *
 * A POST with a JSON body rather than a DELETE with a query string: the filter
 * set can include free-text search, and this keeps the request shape identical
 * to the export route's "everything matching these filters" convention instead
 * of inventing a second way to describe the same criteria.
 */
export function POST(request: Request): Promise<Response> {
  return handle(async () => {
    const filters = await parseJson(request, businessJsonFilterSchema);

    const { response } = await backendRequest('/api/businesses/all', {
      method: 'DELETE',
      query: toBackendQuery(filters),
    });

    if (!response.ok) {
      const problem = (await response.json().catch(() => null)) as BackendProblem | null;
      return fail(problemMessage(problem, 'Could not delete those leads.'), 'backend_error', response.status);
    }

    return ok((await response.json()) as { removed: number });
  });
}
