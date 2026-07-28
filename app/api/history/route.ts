import { handle, ok } from '@/lib/api/response';
import { historyRepository } from '@/repositories/history.repository';

export const dynamic = 'force-dynamic';

export function GET(request: Request): Promise<Response> {
  return handle(async () => {
    const limitParam = new URL(request.url).searchParams.get('limit');
    const parsed = limitParam ? Number.parseInt(limitParam, 10) : NaN;
    const limit = Number.isFinite(parsed) && parsed > 0 ? Math.min(parsed, 500) : undefined;

    return ok(await historyRepository.list(limit));
  });
}

export function DELETE(): Promise<Response> {
  return handle(async () => {
    await historyRepository.clear();
    return ok({ cleared: true });
  });
}
