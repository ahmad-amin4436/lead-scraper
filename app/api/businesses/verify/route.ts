import { z } from 'zod';

import { fail, handle, ok, parseJson } from '@/lib/api/response';
import { backendRequest } from '@/lib/backend/server';

export const dynamic = 'force-dynamic';

const MAX_BATCH = 400;

const verifyRequestSchema = z.object({
  /** Re-check records that already carry a status. */
  force: z.boolean().default(false),
  limit: z.number().int().min(1).max(MAX_BATCH).default(MAX_BATCH),
});

interface VerifyLeadsResult {
  verified: number;
  remaining: number;
  candidates: number;
  elapsedMs: number;
}

/**
 * Backfills email deliverability and WhatsApp reachability for stored leads.
 *
 * Runs in bounded batches on the .NET API and reports how many are left, so a
 * large database is processed by calling this repeatedly rather than one long
 * request.
 */
export function POST(request: Request): Promise<Response> {
  return handle(async () => {
    const { force, limit } = await parseJson(request, verifyRequestSchema);

    const { response } = await backendRequest('/api/businesses/verify', {
      method: 'POST',
      body: JSON.stringify({ force, limit }),
    });

    if (!response.ok) {
      return fail('Verification failed.', 'backend_error', response.status);
    }

    return ok((await response.json()) as VerifyLeadsResult);
  });
}
