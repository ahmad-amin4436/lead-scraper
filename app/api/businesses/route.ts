import { fail, handle, ok, parseJson, parseQuery } from '@/lib/api/response';
import { toBackendQuery, toBusinessPage } from '@/lib/backend/business-mapper';
import { backendRequest, problemMessage } from '@/lib/backend/server';
import type { BackendBusiness, BackendPagedResult, BackendProblem } from '@/lib/backend/types';
import { businessDeleteSchema, businessQuerySchema } from '@/lib/validation/business.schema';

export const dynamic = 'force-dynamic';

/** Filtered, sorted, paged lead list — reads straight from SQL Server via the .NET API. */
export function GET(request: Request): Promise<Response> {
  return handle(async () => {
    const query = parseQuery(request, businessQuerySchema);

    const { response } = await backendRequest('/api/businesses', {
      query: toBackendQuery(query),
    });

    if (!response.ok) {
      return fail('Could not load leads.', 'backend_error', response.status);
    }

    const page = (await response.json()) as BackendPagedResult<BackendBusiness>;
    return ok(toBusinessPage(page));
  });
}

/** Bulk delete by id. */
export function DELETE(request: Request): Promise<Response> {
  return handle(async () => {
    const { ids } = await parseJson(request, businessDeleteSchema);

    const { response } = await backendRequest('/api/businesses/bulk-delete', {
      method: 'POST',
      body: JSON.stringify({ ids }),
    });

    if (!response.ok) {
      const problem = (await response.json().catch(() => null)) as BackendProblem | null;
      return fail(problemMessage(problem, 'Could not delete those leads.'), 'backend_error', response.status);
    }

    return ok((await response.json()) as { removed: number });
  });
}
