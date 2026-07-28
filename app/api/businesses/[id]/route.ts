import { NotFoundError } from '@/lib/errors';
import { handle, ok, parseJson } from '@/lib/api/response';
import { businessUpdateSchema } from '@/lib/validation/business.schema';
import { businessRepository } from '@/repositories/business.repository';

export const dynamic = 'force-dynamic';

interface RouteParams {
  params: Promise<{ id: string }>;
}

export function GET(_request: Request, { params }: RouteParams): Promise<Response> {
  return handle(async () => {
    const { id } = await params;
    const record = await businessRepository.getById(id);
    if (!record) throw new NotFoundError(`No record with id "${id}"`);
    return ok(record);
  });
}

export function PATCH(request: Request, { params }: RouteParams): Promise<Response> {
  return handle(async () => {
    const { id } = await params;
    const patch = await parseJson(request, businessUpdateSchema);

    const updated = await businessRepository.update(id, patch);
    if (!updated) throw new NotFoundError(`No record with id "${id}"`);

    return ok(updated);
  });
}

export function DELETE(_request: Request, { params }: RouteParams): Promise<Response> {
  return handle(async () => {
    const { id } = await params;
    const removed = await businessRepository.deleteMany([id]);
    if (removed === 0) throw new NotFoundError(`No record with id "${id}"`);
    return ok({ removed });
  });
}
