import 'server-only';

import { getCategoryLabel } from '@/lib/constants/categories';
import { getCountryName } from '@/lib/constants/locations';
import { toErrorMessage } from '@/lib/errors';
import { businessRepository } from '@/repositories/business.repository';
import { historyRepository } from '@/repositories/history.repository';
import type { BusinessRecord } from '@/types/business';
import type {
  LiveResult,
  ProviderBusiness,
  ResolvedLocation,
  SearchHistoryEntry,
} from '@/types/search';
import type { AppSettings } from '@/types/settings';
import { RateLimiter, mapWithConcurrency, sleep } from '@/utils/async';
import { createId } from '@/utils/id';
import { enrichmentService } from '../enrichment/enrichment.service';
import { logger } from '../logging/logger.service';
import { geocodingService } from '../search/geocoding.service';
import type { ProviderContext, SearchProvider } from '../search/provider';
import { verificationService } from '../verification/verification.service';
import type { Job } from './job';

/** Records buffered before a workbook write. */
const SAVE_BATCH_SIZE = 25;

interface Task {
  city: string;
  category: string;
  categoryLabel: string;
}

function toRecord(business: ProviderBusiness, location: ResolvedLocation): BusinessRecord {
  return {
    id: createId('biz'),
    name: business.name,
    category: business.category,
    country: business.country || getCountryName(location.country),
    state: business.state || location.state,
    city: business.city || location.city,
    address: business.address,
    phone: business.phone,
    website: business.website,
    email: '',
    whatsapp: '',
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
    emailStatus: 'unverified',
    whatsappStatus: 'unverified',
    notes: '',
  };
}

/**
 * Executes a search job end to end.
 *
 * The run sweeps every (city x category) pair, converts provider hits into
 * records, drops duplicates, optionally enriches contacts from each website, and
 * persists to the Excel store in batches. It checks for pause/stop between every
 * unit of work so the controls stay responsive.
 */
export async function runSearchJob(
  job: Job,
  provider: SearchProvider,
  settings: AppSettings,
): Promise<void> {
  const { request } = job;
  const rateLimiter = RateLimiter.perMinute(settings.rateLimitPerMinute);
  const context: ProviderContext = {
    settings,
    rateLimiter,
    signal: job.signal,
    jobId: job.id,
  };

  const tasks: Task[] = request.cities.flatMap((city) =>
    request.categories.map((category) => ({
      city,
      category,
      categoryLabel: getCategoryLabel(category),
    })),
  );

  await job.markRunning(tasks.length);

  await logger.info('search.started', `Search started with ${provider.label}`, {
    jobId: job.id,
    context: {
      provider: provider.id,
      categories: request.categories.length,
      cities: request.cities.length,
      tasks: tasks.length,
      enrich: request.enrichContacts,
    },
  });

  const pending: BusinessRecord[] = [];

  const flushPending = async (): Promise<void> => {
    if (pending.length === 0) return;
    const batch = pending.splice(0, pending.length);

    try {
      const { inserted } = await businessRepository.insertMany(batch, false);
      await businessRepository.flush();
      job.addCount('saved', inserted.length);
      await logger.debug('database.written', `Saved ${inserted.length} record(s)`, {
        jobId: job.id,
        context: { count: inserted.length },
      });
    } catch (error) {
      job.addCount('failed', batch.length);
      await logger.error('search.failed', `Failed to save batch: ${toErrorMessage(error)}`, {
        jobId: job.id,
        context: { count: batch.length },
      });
    }

    await job.save();
  };

  try {
    // Geocode each city once up front so failures surface before any crawling.
    const locations = new Map<string, ResolvedLocation>();

    for (const city of request.cities) {
      await job.refreshStop();
      job.signal.throwIfAborted();
      job.setTask(`Locating ${city}`);
      await job.save();

      try {
        const resolved = await geocodingService.resolve(
          { country: request.country, state: request.state, city },
          context,
        );

        if (resolved) {
          locations.set(city, resolved);
        } else {
          await logger.warn('provider.failed', `Could not locate "${city}"`, {
            jobId: job.id,
            context: { city },
          });
        }
      } catch (error) {
        if (job.signal.aborted) throw error;
        await logger.warn(
          'provider.failed',
          `Geocoding failed for "${city}": ${toErrorMessage(error)}`,
          { jobId: job.id, context: { city } },
        );
      }
    }

    if (locations.size === 0) {
      throw new Error(
        'None of the selected cities could be located. Check the country/city spelling and try again.',
      );
    }

    for (const task of tasks) {
      await job.refreshStop();
      if (job.signal.aborted) break;

      const location = locations.get(task.city);
      if (!location) {
        job.addCount('skipped');
        job.completeTask();
        await job.save();
        continue;
      }

      job.setTask(`Searching ${task.categoryLabel} in ${task.city}`, 0.1);
      await job.save();

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

        job.addCount('found', businesses.length);
        await logger.info(
          'provider.query',
          `${task.categoryLabel} in ${task.city}: ${businesses.length} result(s)`,
          {
            jobId: job.id,
            context: { category: task.category, city: task.city, count: businesses.length },
          },
        );
      } catch (error) {
        if (job.signal.aborted) break;
        job.addCount('failed');
        await logger.error(
          'provider.failed',
          `${task.categoryLabel} in ${task.city} failed: ${toErrorMessage(error)}`,
          { jobId: job.id, context: { category: task.category, city: task.city } },
        );
        job.completeTask();
        continue;
      }

      // Apply quality filters before spending enrichment budget on a record.
      const filtered = businesses.filter((business) => {
        if (request.minRating !== undefined && (business.rating ?? 0) < request.minRating) {
          return false;
        }
        if (request.minReviews !== undefined && (business.reviewCount ?? 0) < request.minReviews) {
          return false;
        }
        return true;
      });

      job.addCount('skipped', businesses.length - filtered.length);

      const records: BusinessRecord[] = [];

      for (const business of filtered) {
        const record = toRecord(business, location);

        if (request.skipDuplicates) {
          const existing = await businessRepository.findDuplicate(record);
          if (existing) {
            job.addCount('duplicates');
            job.addResult({ ...record, duplicate: true } satisfies LiveResult);
            continue;
          }
        }

        records.push(record);
      }

      job.setTask(`Searching ${task.categoryLabel} in ${task.city}`, 0.4);

      if (request.enrichContacts && records.length > 0) {
        await enrichRecords(job, records, settings, task);
      }

      // Runs regardless of enrichment: a provider phone number can still be
      // classified for WhatsApp even when no website was crawled.
      if (records.length > 0) {
        await verifyRecords(job, records, settings, task);
      }

      for (const record of records) {
        pending.push(record);
        job.addResult({ ...record, duplicate: false } satisfies LiveResult);
      }

      if (pending.length >= SAVE_BATCH_SIZE) await flushPending();

      job.completeTask();
      await job.save();

      if (settings.delayMs > 0 && !job.signal.aborted) {
        await sleep(settings.delayMs, job.signal).catch(() => undefined);
      }
    }

    await flushPending();

    if (job.signal.aborted) {
      await job.finish('stopped');
      await logger.warn('search.stopped', 'Search stopped by user', {
        jobId: job.id,
        elapsedMs: job.elapsedMs,
        context: { saved: job.counters.saved },
      });
    } else {
      await job.finish('completed');
      await logger.info('search.completed', 'Search completed', {
        jobId: job.id,
        elapsedMs: job.elapsedMs,
        context: {
          found: job.counters.found,
          saved: job.counters.saved,
          duplicates: job.counters.duplicates,
          enriched: job.counters.enriched,
        },
      });
    }
  } catch (error) {
    await flushPending();

    const aborted = job.signal.aborted;
    const message = toErrorMessage(error);

    if (aborted) {
      await job.finish('stopped');
      await logger.warn('search.stopped', 'Search stopped by user', {
        jobId: job.id,
        elapsedMs: job.elapsedMs,
      });
    } else {
      await job.finish('failed', message);
      await logger.error('search.failed', `Search failed: ${message}`, {
        jobId: job.id,
        elapsedMs: job.elapsedMs,
      });
    }
  } finally {
    await recordHistory(job);
    await logger.flush();
  }
}

/** Enriches a batch of records in place, bounded by the concurrency setting. */
async function enrichRecords(
  job: Job,
  records: BusinessRecord[],
  settings: AppSettings,
  task: Task,
): Promise<void> {
  const withSite = records.filter((record) => record.website);
  if (withSite.length === 0) return;

  let completed = 0;

  await mapWithConcurrency(
    withSite,
    settings.concurrency,
    async (record) => {
      // Pull the stop flag from storage so a stop requested by another process
      // aborts in-flight crawls promptly, not just at the next task boundary.
      await job.refreshStop();
      job.signal.throwIfAborted();

      const result = await enrichmentService.enrich(record.website, settings, job.signal);

      record.email = result.contact.email ?? '';
      record.whatsapp = result.contact.whatsapp ?? '';
      record.facebook = result.contact.facebook ?? '';
      record.instagram = result.contact.instagram ?? '';
      record.linkedin = result.contact.linkedin ?? '';
      record.status = result.status;

      // Keep the provider phone as primary; a website number is a fallback.
      if (!record.phone && result.contact.websitePhones.length > 0) {
        record.phone = result.contact.websitePhones[0];
      }

      const noteParts = [result.notes];
      if (result.contact.additionalEmails.length > 0) {
        noteParts.push(`Other emails: ${result.contact.additionalEmails.join(', ')}`);
      }
      if (result.contact.contactFormUrl) {
        noteParts.push(`Contact form: ${result.contact.contactFormUrl}`);
      }
      if (result.error) noteParts.push(`Error: ${result.error}`);
      record.notes = noteParts.filter(Boolean).join(' | ').slice(0, 1000);

      if (result.status === 'enrichment-failed') {
        job.addCount('enrichmentFailed');
        await logger.debug('enrichment.failed', `Enrichment failed for ${record.name}`, {
          jobId: job.id,
          elapsedMs: result.elapsedMs,
          context: { website: record.website, error: result.error },
        });
      } else {
        job.addCount('enriched');
        await logger.debug(
          'enrichment.completed',
          `Enriched ${record.name}${result.contact.email ? ` (${result.contact.email})` : ''}`,
          {
            jobId: job.id,
            elapsedMs: result.elapsedMs,
            context: { website: record.website, pages: result.pagesVisited },
          },
        );
      }

      completed += 1;
      job.setTask(
        `Enriching ${task.categoryLabel} in ${task.city} (${completed}/${withSite.length})`,
        0.4 + 0.5 * (completed / withSite.length),
      );
      // Throttled write so a long enrichment pass keeps proving it's alive —
      // and so the UI's progress actually advances mid-task.
      await job.heartbeat();
    },
    (error, record) => {
      if (job.signal.aborted) return;
      job.addCount('enrichmentFailed');
      record.status = 'enrichment-failed';
      void logger.warn(
        'enrichment.failed',
        `Enrichment error for ${record.name}: ${toErrorMessage(error)}`,
        { jobId: job.id, context: { website: record.website } },
      );
    },
  );
}

/**
 * Verifies email deliverability and WhatsApp reachability for a batch.
 *
 * DNS results are cached per domain inside the verifier, so this is cheap even
 * across large runs. Failures are swallowed per record — a verification problem
 * must never cost us the lead itself.
 */
async function verifyRecords(
  job: Job,
  records: BusinessRecord[],
  settings: AppSettings,
  task: Task,
): Promise<void> {
  job.setTask(`Verifying contacts for ${task.categoryLabel} in ${task.city}`, 0.9);

  await mapWithConcurrency(
    records,
    settings.concurrency,
    async (record) => {
      job.signal.throwIfAborted();

      const outcome = await verificationService.applyTo(record);

      if (outcome.notes.length > 0) {
        record.notes = [record.notes, ...outcome.notes].filter(Boolean).join(' | ').slice(0, 1000);
      }

      if (outcome.emailStatus === 'valid') job.addCount('emailsVerified');
      if (outcome.whatsappStatus === 'confirmed' || outcome.whatsappStatus === 'likely') {
        job.addCount('whatsappReachable');
      }

      await job.heartbeat();
    },
    (error, record) => {
      if (job.signal.aborted) return;
      // Leave the status as 'unverified' rather than guessing.
      void logger.debug(
        'enrichment.skipped',
        `Verification skipped for ${record.name}: ${toErrorMessage(error)}`,
        { jobId: job.id },
      );
    },
  );
}

async function recordHistory(job: Job): Promise<void> {
  const status =
    job.status === 'completed' ? 'completed' : job.status === 'failed' ? 'failed' : 'stopped';

  const entry: SearchHistoryEntry = {
    id: createId('hist'),
    jobId: job.id,
    request: job.request,
    status,
    startedAt: job.startedAt,
    finishedAt: job.finishedAt ?? new Date().toISOString(),
    elapsedMs: job.elapsedMs,
    found: job.counters.found,
    saved: job.counters.saved,
    duplicates: job.counters.duplicates,
    enriched: job.counters.enriched,
    failed: job.counters.failed,
    error: job.error,
  };

  try {
    await historyRepository.add(entry);
  } catch (error) {
    console.error('[search-runner] failed to write history entry:', error);
  }
}
