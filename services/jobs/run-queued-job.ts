import 'server-only';

import { settingsRepository } from '@/repositories/settings.repository';
import { resolveProvider } from '@/services/search/provider-registry';
import { jobManager } from './job-manager';
import { runSearchJob } from './search-runner';

/**
 * Loads a queued job and drives it to completion.
 *
 * Shared by the inline path (`next dev` / long-lived host) and the Netlify
 * Background Function. It re-resolves settings and the provider from storage so
 * the worker is self-contained given only a job id.
 *
 * Idempotency: Background Functions auto-retry on failure, so this refuses to
 * start a job that is no longer `queued`. A job already running, stopping, or
 * finished is left alone.
 */
export async function runQueuedJob(jobId: string): Promise<void> {
  const job = await jobManager.find(jobId);
  if (!job) {
    console.error(`[run-queued-job] no job with id "${jobId}"`);
    return;
  }

  if (job.status !== 'queued') {
    // A retry or duplicate trigger — the run already began or ended.
    console.warn(`[run-queued-job] job "${jobId}" is ${job.status}, not starting`);
    return;
  }

  const settings = await settingsRepository.get();
  const provider = resolveProvider(job.request.provider, settings);
  await runSearchJob(job, provider, settings);
}
