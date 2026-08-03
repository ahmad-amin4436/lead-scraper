// Translation between the .NET search-job DTO and the shapes the UI already
// speaks.
//
// The backend is the source of truth for a run: it owns the queue, the lease and
// the counters. This module exists so that stays true without every component
// having to learn .NET's PascalCase enum names.

import type { BusinessStatus, EmailStatus, WhatsAppStatus } from '@/types/business';
import type { JobSnapshot, JobStatus } from '@/types/job';
import type { LiveResult, SearchHistoryEntry, SearchRequest } from '@/types/search';

/** A search job exactly as the .NET API returns it. */
export interface BackendSearchJob {
  id: string;
  status: string;
  requestJson: string;
  totalTasks: number;
  completedTasks: number;
  found: number;
  saved: number;
  duplicates: number;
  enriched: number;
  enrichmentFailed: number;
  emailsVerified: number;
  whatsAppReachable: number;
  skipped: number;
  failed: number;
  currentTask: string;
  startedAt: string;
  finishedAt: string | null;
  heartbeatAt: string | null;
  stopRequested: boolean;
  error: string | null;
  requestedByUserId: string | null;
  attempts: number;
  recentResultsJson: string;
  isLeased: boolean;
  percentComplete: number;
}

const JOB_STATUS_MAP: Record<string, JobStatus> = {
  Queued: 'queued',
  Running: 'running',
  Stopping: 'stopping',
  Completed: 'completed',
  Failed: 'failed',
  Stopped: 'stopped',
};

const BUSINESS_STATUS_MAP: Record<string, BusinessStatus> = {
  New: 'new',
  Enriched: 'enriched',
  Partial: 'partial',
  NoWebsite: 'no-website',
  EnrichmentFailed: 'enrichment-failed',
};

const EMAIL_STATUS_MAP: Record<string, EmailStatus> = {
  Unverified: 'unverified',
  Valid: 'valid',
  Risky: 'risky',
  Invalid: 'invalid',
  Unknown: 'unknown',
};

const WHATSAPP_STATUS_MAP: Record<string, WhatsAppStatus> = {
  Unverified: 'unverified',
  Confirmed: 'confirmed',
  Likely: 'likely',
  Unlikely: 'unlikely',
  None: 'none',
};

interface BackendLiveResult {
  id?: string;
  name?: string;
  category?: string;
  city?: string;
  country?: string;
  email?: string;
  phone?: string;
  website?: string;
  status?: string;
  emailStatus?: string;
  whatsAppStatus?: string;
  duplicate?: boolean;
}

function parseJson<T>(raw: string | null | undefined, fallback: T): T {
  if (!raw) return fallback;
  try {
    return JSON.parse(raw) as T;
  } catch {
    // A malformed blob costs a progress panel, not the run.
    return fallback;
  }
}

/**
 * Estimates time remaining from the average task duration so far.
 *
 * Null until at least one task has finished — a prediction from zero samples is
 * worse than showing nothing.
 */
function estimateEta(job: BackendSearchJob, elapsedMs: number): number | null {
  if (job.completedTasks <= 0 || job.totalTasks <= job.completedTasks) return null;

  const perTask = elapsedMs / job.completedTasks;
  return Math.round(perTask * (job.totalTasks - job.completedTasks));
}

export function toJobSnapshot(job: BackendSearchJob): JobSnapshot {
  const startedAt = new Date(job.startedAt).getTime();
  const finishedAt = job.finishedAt ? new Date(job.finishedAt).getTime() : null;
  const elapsedMs = Math.max(0, (finishedAt ?? Date.now()) - startedAt);

  const rawResults = parseJson<BackendLiveResult[]>(job.recentResultsJson, []);

  const results: LiveResult[] = rawResults.map((result, index) => ({
    id: result.id ?? `${job.id}-${index}`,
    name: result.name ?? '',
    category: result.category ?? '',
    country: result.country ?? '',
    state: '',
    city: result.city ?? '',
    address: '',
    phone: result.phone ?? '',
    website: result.website ?? '',
    email: result.email ?? '',
    emailStatus: EMAIL_STATUS_MAP[result.emailStatus ?? ''] ?? 'unverified',
    whatsapp: '',
    whatsappStatus: WHATSAPP_STATUS_MAP[result.whatsAppStatus ?? ''] ?? 'unverified',
    facebook: '',
    instagram: '',
    linkedin: '',
    latitude: null,
    longitude: null,
    rating: null,
    reviewCount: null,
    mapsUrl: '',
    source: 'manual',
    dateAdded: job.startedAt,
    status: BUSINESS_STATUS_MAP[result.status ?? ''] ?? 'new',
    notes: '',
    duplicate: result.duplicate ?? false,
  }));

  return {
    id: job.id,
    status: JOB_STATUS_MAP[job.status] ?? 'queued',
    request: parseJson<SearchRequest>(job.requestJson, {} as SearchRequest),
    counters: {
      totalTasks: job.totalTasks,
      completedTasks: job.completedTasks,
      found: job.found,
      saved: job.saved,
      duplicates: job.duplicates,
      enriched: job.enriched,
      enrichmentFailed: job.enrichmentFailed,
      emailsVerified: job.emailsVerified,
      whatsappReachable: job.whatsAppReachable,
      skipped: job.skipped,
      failed: job.failed,
    },
    progress: {
      percent: job.percentComplete,
      elapsedMs,
      etaMs: finishedAt ? null : estimateEta(job, elapsedMs),
      currentTask: job.currentTask,
    },
    startedAt: job.startedAt,
    finishedAt: job.finishedAt,
    error: job.error,
    results,
  };
}

/**
 * A finished run, as the history page shows it.
 *
 * A still-running job maps to `stopped` rather than being rejected: the history
 * endpoint filters those out already, and a type-level lie is worse than a
 * conservative default if one ever slips through.
 */
export function toHistoryEntry(job: BackendSearchJob): SearchHistoryEntry {
  const startedAt = new Date(job.startedAt).getTime();
  const finishedAt = job.finishedAt ? new Date(job.finishedAt).getTime() : Date.now();

  const status: SearchHistoryEntry['status'] =
    job.status === 'Completed' ? 'completed' : job.status === 'Failed' ? 'failed' : 'stopped';

  return {
    id: job.id,
    jobId: job.id,
    request: parseJson<SearchRequest>(job.requestJson, {} as SearchRequest),
    status,
    startedAt: job.startedAt,
    finishedAt: job.finishedAt ?? new Date(finishedAt).toISOString(),
    elapsedMs: Math.max(0, finishedAt - startedAt),
    found: job.found,
    saved: job.saved,
    duplicates: job.duplicates,
    enriched: job.enriched,
    failed: job.failed,
    error: job.error,
  };
}

/**
 * Builds the create-job payload.
 *
 * `totalTasks` is sent alongside the request so the progress bar means something
 * from the moment the run is queued, rather than only once a worker has parsed
 * the request and reported a total.
 */
export function toCreateJobRequest(request: SearchRequest): {
  requestJson: string;
  totalTasks: number;
} {
  return {
    requestJson: JSON.stringify(request),
    totalTasks: request.cities.length * request.categories.length,
  };
}
