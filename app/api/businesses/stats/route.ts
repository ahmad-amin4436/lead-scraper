import { handle, ok } from '@/lib/api/response';
import { businessRepository } from '@/repositories/business.repository';

export const dynamic = 'force-dynamic';

export function GET(): Promise<Response> {
  return handle(async () => ok(await businessRepository.stats()));
}
