import { handle, ok, parseJson, parseQuery } from '@/lib/api/response';
import { businessDeleteSchema, businessQuerySchema } from '@/lib/validation/business.schema';
import { businessRepository } from '@/repositories/business.repository';

export const dynamic = 'force-dynamic';

export function GET(request: Request): Promise<Response> {
  return handle(async () => {
    const query = parseQuery(request, businessQuerySchema);
    return ok(await businessRepository.query(query));
  });
}

/** Bulk delete by id. */
export function DELETE(request: Request): Promise<Response> {
  return handle(async () => {
    const { ids } = await parseJson(request, businessDeleteSchema);
    const removed = await businessRepository.deleteMany(ids);
    return ok({ removed });
  });
}
