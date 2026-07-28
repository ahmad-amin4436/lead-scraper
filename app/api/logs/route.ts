import { handle, ok, parseQuery } from '@/lib/api/response';
import { logQuerySchema } from '@/lib/validation/log.schema';
import { logRepository } from '@/repositories/log.repository';

export const dynamic = 'force-dynamic';

export function GET(request: Request): Promise<Response> {
  return handle(async () => {
    const query = parseQuery(request, logQuerySchema);
    return ok(await logRepository.query(query));
  });
}

export function DELETE(): Promise<Response> {
  return handle(async () => {
    await logRepository.clear();
    return ok({ cleared: true });
  });
}
