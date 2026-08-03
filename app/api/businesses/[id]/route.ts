import { fail, handle, ok, parseJson } from '@/lib/api/response';
import { STATUS_TO_BACKEND, toBusinessRecord } from '@/lib/backend/business-mapper';
import { backendRequest, problemMessage } from '@/lib/backend/server';
import type { BackendBusiness, BackendProblem } from '@/lib/backend/types';
import { businessUpdateSchema } from '@/lib/validation/business.schema';

export const dynamic = 'force-dynamic';

interface RouteParams {
  params: Promise<{ id: string }>;
}

export function GET(_request: Request, { params }: RouteParams): Promise<Response> {
  return handle(async () => {
    const { id } = await params;
    const { response } = await backendRequest(`/api/businesses/${id}`);

    if (!response.ok) {
      const problem = (await response.json().catch(() => null)) as BackendProblem | null;
      return fail(
        problemMessage(problem, `No record with id "${id}"`),
        response.status === 404 ? 'not_found' : 'backend_error',
        response.status,
      );
    }

    return ok(toBusinessRecord((await response.json()) as BackendBusiness));
  });
}

export function PATCH(request: Request, { params }: RouteParams): Promise<Response> {
  return handle(async () => {
    const { id } = await params;
    const patch = await parseJson(request, businessUpdateSchema);

    const body: Record<string, unknown> = {};
    if (patch.status !== undefined) body.status = STATUS_TO_BACKEND[patch.status];
    if (patch.notes !== undefined) body.notes = patch.notes;
    if (patch.email !== undefined) body.email = patch.email;
    if (patch.phone !== undefined) body.phone = patch.phone;

    const { response } = await backendRequest(`/api/businesses/${id}`, {
      method: 'PUT',
      body: JSON.stringify(body),
    });

    if (!response.ok) {
      const problem = (await response.json().catch(() => null)) as BackendProblem | null;
      return fail(
        problemMessage(problem, `No record with id "${id}"`),
        response.status === 404 ? 'not_found' : 'backend_error',
        response.status,
      );
    }

    return ok(toBusinessRecord((await response.json()) as BackendBusiness));
  });
}

export function DELETE(_request: Request, { params }: RouteParams): Promise<Response> {
  return handle(async () => {
    const { id } = await params;
    const { response } = await backendRequest(`/api/businesses/${id}`, { method: 'DELETE' });

    if (response.status === 404) {
      return fail(`No record with id "${id}"`, 'not_found', 404);
    }

    if (!response.ok) {
      return fail('Could not delete that lead.', 'backend_error', response.status);
    }

    return ok({ removed: 1 });
  });
}
