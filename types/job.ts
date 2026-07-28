import type { LiveResult, SearchRequest } from './search';

export const JOB_STATUSES = [
  'queued',
  'running',
  'paused',
  'stopping',
  'completed',
  'failed',
  'stopped',
] as const;
export type JobStatus = (typeof JOB_STATUSES)[number];

export type JobCommand = 'pause' | 'resume' | 'stop';

export interface JobCounters {
  /** Total (category x city) pairs to sweep. */
  totalTasks: number;
  completedTasks: number;
  found: number;
  saved: number;
  duplicates: number;
  enriched: number;
  enrichmentFailed: number;
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
  /** Most recent results, newest first, capped by the job manager. */
  results: LiveResult[];
}

export type JobEvent =
  | { type: 'snapshot'; job: JobSnapshot }
  | { type: 'result'; jobId: string; result: LiveResult }
  | { type: 'progress'; jobId: string; counters: JobCounters; progress: JobProgress; status: JobStatus }
  | { type: 'status'; jobId: string; status: JobStatus; error: string | null }
  | { type: 'done'; jobId: string; status: JobStatus };
