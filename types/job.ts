import type { LiveResult, SearchRequest } from './search';

export const JOB_STATUSES = [
  'queued',
  'running',
  'stopping',
  'completed',
  'failed',
  'stopped',
] as const;
export type JobStatus = (typeof JOB_STATUSES)[number];

/**
 * Only `stop` remains. Pause/resume relied on an in-memory promise gate that
 * cannot survive across serverless invocations, so they were removed when the
 * job model moved to a shared blob store.
 */
export type JobCommand = 'stop';

export interface JobCounters {
  /** Total (category x city) pairs to sweep. */
  totalTasks: number;
  completedTasks: number;
  found: number;
  saved: number;
  duplicates: number;
  enriched: number;
  enrichmentFailed: number;
  /** Emails whose domain was confirmed to accept mail. */
  emailsVerified: number;
  /** Numbers confirmed or likely to be reachable on WhatsApp. */
  whatsappReachable: number;
  skipped: number;
  failed: number;
}

export interface JobProgress {
  /** 0-100. */
  percent: number;
  /** Milliseconds since the job started, excluding nothing (wall clock). */
  elapsedMs: number;
  /** Estimated milliseconds remaining, or null when not yet predictable. */
  etaMs: number | null;
  currentTask: string;
}

export interface JobSnapshot {
  id: string;
  status: JobStatus;
  request: SearchRequest;
  counters: JobCounters;
  progress: JobProgress;
  startedAt: string;
  finishedAt: string | null;
  error: string | null;
  /** Most recent results, newest first, capped by the runner. */
  results: LiveResult[];
}

/**
 * The full persisted state of a job in blob storage. It is the snapshot plus a
 * cooperative stop flag the running worker polls at each checkpoint — replacing
 * the in-memory `AbortController` that could not span serverless invocations.
 */
export interface JobRecord extends JobSnapshot {
  stopRequested: boolean;
  /** Wall-clock ms the run started, for elapsed/ETA math across processes. */
  startedAtMs: number;
}
