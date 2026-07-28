import { NotFoundError } from '@/lib/errors';
import { handle, ok } from '@/lib/api/response';
import { historyRepository } from '@/repositories/history.repository';

export const dynamic = 'force-dynamic';

interface RouteParams {
  params: Promise<{ id: string }>;
}

export function GET(_request: Request, { params }: RouteParams): Promise<Response> {
  return handle(async () => {
    const { id } = await params;
    const entry = await historyRepository.getById(id);
    if (!entry) throw new NotFoundError('History entry not found');
    return ok(entry);
  });
}

export function DELETE(_request: Request, { params }: RouteParams): Promise<Response> {
  return handle(async () => {
    const { id } = await params;
    const removed = await historyRepository.remove(id);
    if (!removed) throw new NotFoundError('History entry not found');
    return ok({ removed: true });
  });
}
