import { getCategoryLabel } from '@/lib/constants/categories';
import { toErrorMessage } from '@/lib/errors';
import { enrichmentService } from '@/services/enrichment/enrichment.service';
import { ingestLeads } from '@/services/leads/lead-sink';
import { geocodingService } from '@/services/search/geocoding.service';
import { resolveProvider } from '@/services/search/provider-registry';
import type { ProviderContext } from '@/services/search/provider';
import { verificationService } from '@/services/verification/verification.service';
import { settingsRepository } from '@/repositories/settings.repository';
import type { BusinessRecord } from '@/types/business';
import type { ProviderBusiness, ResolvedLocation, SearchRequest } from '@/types/search';
import { RateLimiter, mapWithConcurrency, sleep } from '@/utils/async';
import { createId } from '@/utils/id';

import type { ClaimedJob, JobClient } from './job-client';

/** Leads buffered before a write to the database. */
const SAVE_BATCH = 25;

/** Live results kept for the progress panel. */
const MAX_RESULTS = 200;

interface Task {
  key: string;
  city: string;
  category: string;
  categoryLabel: string;
}

/**
 * Executes one claimed search run.
 *
 * The unit of progress is a (city × category) task. Each one is checkpointed to
 * the database as it finishes, so if this process dies the next worker skips
 * what is already done rather than re-scraping it.
 */
export async function runQueuedSearch(client: JobClient, job: ClaimedJob): Promise<void> {
  const request = parseRequest(job.requestJson);

  if (!request) {
    await client.complete(job.id, 'Failed', 'The stored search request could not be read.');
    return;
  }

  if (!job.ownerUserId) {
    await client.complete(job.id, 'Failed', 'This run has no owner, so its leads could not be saved.');
    return;
  }

  const settings = await settingsRepository.get();
  const controller = new AbortController();

  const counters = {
    found: job.found,
    saved: job.saved,
    duplicates: job.duplicates,
    enriched: job.enriched,
    enrichmentFailed: job.enrichmentFailed,
    emailsVerified: job.emailsVerified,
    whatsAppReachable: job.whatsAppReachable,
    skipped: job.skipped,
    failed: job.failed,
  };

  const recentResults: unknown[] = [];
  let currentTask = 'Preparing';

  /**
   * True once the lease is gone. The job then belongs to someone else, so this
   * worker must not report an outcome for it — doing so would truncate a run
   * that is about to be resumed elsewhere.
   */
  let leaseLost = false;

  /** Pushes progress and picks up a stop request from the response. */
  const beat = async (completedTaskKey?: string): Promise<boolean> => {
    const result = await client.heartbeat(job.id, {
      currentTask,
      completedTaskKey,
      totalTasks: tasks.length,
      ...counters,
      recentResultsJson: JSON.stringify(recentResults.slice(0, MAX_RESULTS)),
    });

    if (!result.leaseValid) {
      console.warn(`[worker] lost the lease on ${job.id}; another worker has it now`);
      leaseLost = true;
      controller.abort();
      return false;
    }

    if (result.stopRequested) {
      controller.abort();
      return false;
    }

    return true;
  };

  /** Reports the outcome, unless the lease was lost. */
  const finish = async (status: 'Completed' | 'Failed' | 'Stopped', error?: string): Promise<void> => {
    if (leaseLost) return;

    try {
      await client.complete(job.id, status, error);
    } catch (completionError) {
      // The API rejects a completion from a worker without the lease. That is
      // the correct outcome, not something to retry.
      console.warn(`[worker] could not finish ${job.id}:`, toErrorMessage(completionError));
    }
  };

  let provider;
  try {
    provider = resolveProvider(request.provider, settings);
  } catch (error) {
    await finish('Failed', toErrorMessage(error));
    return;
  }

  const context: ProviderContext = {
    settings,
    rateLimiter: RateLimiter.perMinute(settings.rateLimitPerMinute),
    signal: controller.signal,
    jobId: job.id,
  };

  const tasks: Task[] = request.cities.flatMap((city) =>
    request.categories.map((category) => ({
      key: `${city}|${category}`,
      city,
      category,
      categoryLabel: getCategoryLabel(category),
    })),
  );

  // The resume checkpoint: everything already finished is skipped.
  const done = new Set(job.completedTaskKeys);
  const remaining = tasks.filter((task) => !done.has(task.key));

  if (remaining.length === 0) {
    await finish('Completed');
    return;
  }

  const pending: BusinessRecord[] = [];

  const flush = async (): Promise<void> => {
    if (pending.length === 0) return;
    const batch = pending.splice(0, pending.length);

    try {
      const outcome = await ingestLeads(batch, {
        ownerUserId: job.ownerUserId!,
        searchJobId: job.id,
        skipDuplicates: request.skipDuplicates,
      });

      counters.saved += outcome.saved;
      counters.duplicates += outcome.duplicates;
    } catch (error) {
      // Never drop scraped leads silently: count the failure so it is visible
      // in the run summary.
      counters.failed += batch.length;
      console.error(`[worker] failed to save ${batch.length} lead(s):`, toErrorMessage(error));
    }
  };

  try {
    // Geocode up front so a bad city fails before any crawling happens.
    const locations = new Map<string, ResolvedLocation>();

    for (const city of new Set(remaining.map((task) => task.city))) {
      if (controller.signal.aborted) break;

      currentTask = `Locating ${city}`;
      if (!(await beat())) break;

      try {
        const resolved = await geocodingService.resolve(
          { country: request.country, state: request.state, city },
          context,
        );
        if (resolved) locations.set(city, resolved);
      } catch (error) {
        if (controller.signal.aborted) break;
        console.warn(`[worker] geocoding failed for ${city}:`, toErrorMessage(error));
      }
    }

    if (locations.size === 0 && !controller.signal.aborted) {
      await finish('Failed', 'None of the selected cities could be located. Check the spelling and try again.');
      return;
    }

    for (const task of remaining) {
      if (controller.signal.aborted) break;

      const location = locations.get(task.city);
      if (!location) {
        counters.skipped += 1;
        await beat(task.key);
        continue;
      }

      currentTask = `Searching ${task.categoryLabel} in ${task.city}`;
      if (!(await beat())) break;

      let businesses: ProviderBusiness[] = [];

      try {
        businesses = await provider.search(
          {
            category: task.category,
            categoryLabel: task.categoryLabel,
            location,
            radiusMeters: request.radiusMeters,
            maxResults: request.maxResults,
          },
          context,
        );

        counters.found += businesses.length;
      } catch (error) {
        if (controller.signal.aborted) break;

        counters.failed += 1;
        console.error(`[worker] ${task.key} failed:`, toErrorMessage(error));

        // Checkpoint anyway: a category with no results should not be retried
        // forever on the next resume.
        await beat(task.key);
        continue;
      }

      const filtered = businesses.filter((business) => {
        if (request.minRating !== undefined && (business.rating ?? 0) < request.minRating) return false;
        if (request.minReviews !== undefined && (business.reviewCount ?? 0) < request.minReviews) return false;
        return true;
      });

      counters.skipped += businesses.length - filtered.length;

      const records = filtered.map((business) => toRecord(business, location));

      if (request.enrichContacts && records.length > 0) {
        await enrichRecords(records, settings, controller.signal, counters, (done, total) => {
          currentTask = `Enriching ${task.categoryLabel} in ${task.city} (${done}/${total})`;
        });
      }

      for (const record of records) {
        try {
          const outcome = await verificationService.applyTo(record);
          if (outcome.emailStatus === 'valid') counters.emailsVerified += 1;
          if (outcome.whatsappStatus === 'confirmed' || outcome.whatsappStatus === 'likely') {
            counters.whatsAppReachable += 1;
          }
        } catch {
          // Verification is best-effort; the lead is still worth keeping.
        }

        if (!matchesLeadKind(record, request.leadKind)) {
          counters.skipped += 1;
          continue;
        }

        pending.push(record);
        recentResults.unshift({ ...record, duplicate: false });
      }

      if (pending.length >= SAVE_BATCH) await flush();

      // Save before checkpointing, so a crash between the two re-runs the task
      // rather than losing its leads.
      await flush();
      if (!(await beat(task.key))) break;

      if (settings.delayMs > 0 && !controller.signal.aborted) {
        await sleep(settings.delayMs, controller.signal).catch(() => undefined);
      }
    }

    await flush();

    if (controller.signal.aborted) {
      await finish('Stopped');
    } else {
      await finish('Completed');
    }
  } catch (error) {
    await flush();

    if (controller.signal.aborted) {
      await finish('Stopped');
    } else {
      await finish('Failed', toErrorMessage(error));
    }
  }
}

async function enrichRecords(
  records: BusinessRecord[],
  settings: Awaited<ReturnType<typeof settingsRepository.get>>,
  signal: AbortSignal,
  counters: { enriched: number; enrichmentFailed: number },
  onProgress: (done: number, total: number) => void,
): Promise<void> {
  const withSite = records.filter((record) => record.website);
  if (withSite.length === 0) return;

  let completed = 0;

  await mapWithConcurrency(
    withSite,
    settings.concurrency,
    async (record) => {
      signal.throwIfAborted();

      const result = await enrichmentService.enrich(record.website, settings, signal);

      record.email = result.contact.email ?? '';
      record.whatsapp = result.contact.whatsapp ?? '';
      record.facebook = result.contact.facebook ?? '';
      record.instagram = result.contact.instagram ?? '';
      record.linkedin = result.contact.linkedin ?? '';
      record.status = result.status;

      if (!record.phone && result.contact.websitePhones.length > 0) {
        record.phone = result.contact.websitePhones[0];
      }

      if (result.status === 'enrichment-failed') counters.enrichmentFailed += 1;
      else counters.enriched += 1;

      completed += 1;
      onProgress(completed, withSite.length);
    },
    (_error, record) => {
      counters.enrichmentFailed += 1;
      record.status = 'enrichment-failed';
    },
  );
}

/** Mirrors `LeadKind` on the backend so run-time and query-time agree. */
function matchesLeadKind(record: BusinessRecord, kind: SearchRequest['leadKind']): boolean {
  switch (kind) {
    case undefined:
    case 'Any':
      return true;
    case 'New':
      return record.status === 'new';
    case 'Enriched':
      return record.status === 'enriched';
    case 'NoWebsite':
      return record.status === 'no-website';
    case 'Partial':
      return record.status === 'partial';
    case 'WhatsAppOnly':
      return record.whatsappStatus === 'confirmed' || record.whatsappStatus === 'likely';
    case 'EmailOnly':
      return Boolean(record.email);
    case 'DeliverableEmail':
      return Boolean(record.email) && record.emailStatus === 'valid';
    default:
      return true;
  }
}

function parseRequest(json: string): SearchRequest | null {
  try {
    const parsed = JSON.parse(json) as SearchRequest;
    if (!Array.isArray(parsed.categories) || !Array.isArray(parsed.cities)) return null;
    return parsed;
  } catch {
    return null;
  }
}

function toRecord(business: ProviderBusiness, location: ResolvedLocation): BusinessRecord {
  return {
    id: createId('biz'),
    name: business.name,
    category: business.category,
    country: business.country || location.country,
    state: business.state || location.state,
    city: business.city || location.city,
    address: business.address,
    phone: business.phone,
    website: business.website,
    email: '',
    emailStatus: 'unverified',
    whatsapp: '',
    whatsappStatus: 'unverified',
    facebook: '',
    instagram: '',
    linkedin: '',
    latitude: business.latitude,
    longitude: business.longitude,
    rating: business.rating,
    reviewCount: business.reviewCount,
    mapsUrl: business.mapsUrl,
    source: business.source,
    dateAdded: new Date().toISOString(),
    status: business.website ? 'new' : 'no-website',
    notes: '',
  };
}
