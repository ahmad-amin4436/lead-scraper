import { fail, handle, ok } from '@/lib/api/response';
import { toBusinessStats } from '@/lib/backend/business-mapper';
import { backendRequest } from '@/lib/backend/server';
import type { BackendBusinessStats } from '@/lib/backend/types';

export const dynamic = 'force-dynamic';

export function GET(): Promise<Response> {
  return handle(async () => {
    const { response } = await backendRequest('/api/businesses/stats');

    if (!response.ok) {
      return fail('Could not load lead statistics.', 'backend_error', response.status);
    }

    return ok(toBusinessStats((await response.json()) as BackendBusinessStats));
  });
}
