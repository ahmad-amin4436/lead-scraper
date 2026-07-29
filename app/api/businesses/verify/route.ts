import { handle, ok, parseJson } from '@/lib/api/response';
import { businessRepository } from '@/repositories/business.repository';
import { settingsRepository } from '@/repositories/settings.repository';
import { logger } from '@/services/logging/logger.service';
import { verificationService } from '@/services/verification/verification.service';
import type { BusinessRecord } from '@/types/business';
import { mapWithConcurrency } from '@/utils/async';
import { z } from 'zod';

export const dynamic = 'force-dynamic';

/**
 * Cap per call so the request finishes inside a serverless function's timeout.
 * The client re-invokes until `remaining` reaches zero.
 */
const MAX_BATCH = 400;

const verifyRequestSchema = z.object({
  /** Re-check records that already carry a status. */
  force: z.boolean().default(false),
  limit: z.number().int().min(1).max(MAX_BATCH).default(MAX_BATCH),
});

/**
 * Backfills email deliverability and WhatsApp reachability for stored leads.
 *
 * Runs in bounded batches and reports how many are left, so a large database is
 * processed by calling this repeatedly rather than one long request that a
 * function timeout would kill mid-way.
 */
export function POST(request: Request): Promise<Response> {
  return handle(async () => {
    const { force, limit } = await parseJson(request, verifyRequestSchema);
    const settings = await settingsRepository.get();
    const started = Date.now();

    const all = await businessRepository.getAll();

    // Only rows that have something worth checking.
    const candidates = all.filter((record) => {
      if (!record.email && !record.phone) return false;
      if (force) return true;
      return (
        (record.emailStatus ?? 'unverified') === 'unverified' ||
        (record.whatsappStatus ?? 'unverified') === 'unverified'
      );
    });

    const batch = candidates.slice(0, limit);
    const updates: BusinessRecord[] = [];

    await mapWithConcurrency(
      batch,
      Math.max(2, Math.min(settings.concurrency, 8)),
      async (record) => {
        // `applyTo` mutates a copy; the repository persists it below.
        const clone: BusinessRecord = { ...record };
        await verificationService.applyTo(clone);
        updates.push(clone);
      },
      (error, record) => {
        console.error(`[api/businesses/verify] failed for ${record.id}:`, error);
      },
    );

    const saved = await businessRepository.updateMany(updates);
    const remaining = Math.max(0, candidates.length - batch.length);

    await logger.info(
      'database.written',
      `Verified ${saved} record(s); ${remaining} remaining`,
      { elapsedMs: Date.now() - started, context: { verified: saved, remaining, force } },
    );

    return ok({
      verified: saved,
      remaining,
      /** Total records that still qualify for checking, before this batch. */
      candidates: candidates.length,
      elapsedMs: Date.now() - started,
    });
  });
}
