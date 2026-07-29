import 'server-only';

import { jobKey } from '@/lib/paths';
import { blobStore } from '@/lib/storage/blob-store';
import type { JobCounters, JobProgress, JobRecord, JobSnapshot, JobStatus } from '@/types/job';
import type { LiveResult, SearchRequest } from '@/types/search';

/** Live results retained per job; older ones are dropped to bound blob size. */
const MAX_RETAINED_RESULTS = 300;

/** Minimum gap between throttled liveness writes. */
const HEARTBEAT_INTERVAL_MS = 20_000;

/**
 * A single search run, persisted as a JSON blob at `jobs/<id>`.
 *
 * Progress mutations update an in-memory {@link JobRecord} and are written back
 * to storage by `save()`, which the runner calls at checkpoints. A polling
 * request reads the blob (via {@link jobManager}) to render live progress —
 * there are no in-process listeners, since the reader and the worker run in
 * different serverless instances.
 *
 * Stop is cooperative: another process sets `stopRequested` in the blob; the
 * worker observes it at the next checkpoint (`refreshStop`) and aborts its own
 * local {@link AbortController}, which unwinds in-flight HTTP.
 */
export class Job {
  private record: JobRecord;
  private results: LiveResult[] = [];
  private taskFraction = 0;
  private lastHeartbeatMs = 0;
  private readonly controller = new AbortController();

  private constructor(record: JobRecord, results: LiveResult[]) {
    this.record = record;
    this.results = results;
  }

  /** Creates a fresh job and persists its initial state. */
  static async create(id: string, request: SearchRequest): Promise<Job> {
    const now = new Date();
    const record: JobRecord = {
      id,
      status: 'queued',
      request,
      counters: {
        totalTasks: 0,
        completedTasks: 0,
        found: 0,
        saved: 0,
        duplicates: 0,
        enriched: 0,
        enrichmentFailed: 0,
        emailsVerified: 0,
        whatsappReachable: 0,
        skipped: 0,
        failed: 0,
      },
      progress: { percent: 0, elapsedMs: 0, etaMs: null, currentTask: 'Preparing' },
      startedAt: now.toISOString(),
      startedAtMs: now.getTime(),
      finishedAt: null,
      error: null,
      results: [],
      stopRequested: false,
    };

    const job = new Job(record, []);
    await job.save();
    return job;
  }

  /** Loads an existing job from storage, or null when it doesn't exist. */
  static async load(id: string): Promise<Job | null> {
    const record = await blobStore.getJSON<JobRecord>(jobKey(id));
    if (!record) return null;
    return new Job(record, record.results ?? []);
  }

  get id(): string {
    return this.record.id;
  }

  get status(): JobStatus {
    return this.record.status;
  }

  get request(): SearchRequest {
    return this.record.request;
  }

  get counters(): JobCounters {
    return this.record.counters;
  }

  get startedAt(): string {
    return this.record.startedAt;
  }

  get finishedAt(): string | null {
    return this.record.finishedAt;
  }

  get error(): string | null {
    return this.record.error;
  }

  get signal(): AbortSignal {
    return this.controller.signal;
  }

  get isTerminal(): boolean {
    const s = this.record.status;
    return s === 'completed' || s === 'failed' || s === 'stopped';
  }

  get elapsedMs(): number {
    const end = this.record.finishedAt ? Date.parse(this.record.finishedAt) : Date.now();
    return end - this.record.startedAtMs;
  }

  // --- persistence ---------------------------------------------------------

  /** Recomputes derived progress and writes the full record to storage. */
  async save(): Promise<void> {
    this.record.progress = this.computeProgress();
    this.record.results = [...this.results].reverse().slice(0, MAX_RETAINED_RESULTS);
    this.record.heartbeatAt = new Date().toISOString();
    this.lastHeartbeatMs = Date.now();
    await blobStore.setJSON(jobKey(this.record.id), this.record);
  }

  /**
   * Writes a liveness beat, at most once per {@link HEARTBEAT_INTERVAL_MS}.
   *
   * Long tasks (enriching twenty sites) can otherwise run for minutes without
   * touching storage, which would make a healthy run look abandoned. Throttled
   * so it costs at most a couple of writes per minute.
   */
  async heartbeat(): Promise<void> {
    if (Date.now() - this.lastHeartbeatMs < HEARTBEAT_INTERVAL_MS) return;
    await this.save();
  }

  /**
   * Re-reads the stop flag from storage and, if another process requested a
   * stop, moves to `stopping` and aborts the local controller. Returns true when
   * a stop is in effect. Called by the runner at each checkpoint.
   */
  async refreshStop(): Promise<boolean> {
    if (this.controller.signal.aborted) return true;

    const stored = await blobStore.getJSON<JobRecord>(jobKey(this.record.id));
    if (stored?.stopRequested) {
      this.record.stopRequested = true;
      if (this.record.status === 'running') this.record.status = 'stopping';
      this.controller.abort();
      return true;
    }
    return false;
  }

  // --- lifecycle -----------------------------------------------------------

  async markRunning(totalTasks: number): Promise<void> {
    this.record.status = 'running';
    this.record.startedAtMs = Date.now();
    this.record.counters.totalTasks = totalTasks;
    await this.save();
  }

  async finish(
    status: Extract<JobStatus, 'completed' | 'failed' | 'stopped'>,
    error?: string,
  ): Promise<void> {
    this.record.status = status;
    this.record.error = error ?? null;
    this.record.finishedAt = new Date().toISOString();
    this.record.progress = this.computeProgress();
    this.record.progress.currentTask =
      status === 'completed' ? 'Finished' : status === 'stopped' ? 'Stopped' : 'Failed';
    this.taskFraction = 0;
    await this.save();
  }

  // --- progress ------------------------------------------------------------

  setTask(label: string, fraction = 0): void {
    this.record.progress.currentTask = label;
    this.taskFraction = Math.max(0, Math.min(1, fraction));
  }

  completeTask(): void {
    this.record.counters.completedTasks += 1;
    this.taskFraction = 0;
  }

  addCount(key: keyof JobCounters, amount = 1): void {
    this.record.counters[key] += amount;
  }

  addResult(result: LiveResult): void {
    this.results.push(result);
    if (this.results.length > MAX_RETAINED_RESULTS) {
      this.results.splice(0, this.results.length - MAX_RETAINED_RESULTS);
    }
  }

  private computeProgress(): JobProgress {
    const { totalTasks, completedTasks } = this.record.counters;
    const end = this.record.finishedAt ? Date.parse(this.record.finishedAt) : Date.now();
    const elapsedMs = end - this.record.startedAtMs;

    const done = completedTasks + this.taskFraction;
    const percent = totalTasks > 0 ? Math.min(100, (done / totalTasks) * 100) : 0;

    const etaMs =
      completedTasks > 0 && completedTasks < totalTasks && this.record.status === 'running'
        ? Math.round((elapsedMs / completedTasks) * (totalTasks - completedTasks))
        : null;

    return {
      percent:
        this.isTerminal && this.record.status === 'completed'
          ? 100
          : Number(percent.toFixed(1)),
      elapsedMs,
      etaMs,
      currentTask: this.record.progress.currentTask,
    };
  }

  get snapshot(): JobSnapshot {
    const progress = this.computeProgress();
    return {
      id: this.record.id,
      status: this.record.status,
      request: this.record.request,
      counters: { ...this.record.counters },
      progress,
      startedAt: this.record.startedAt,
      finishedAt: this.record.finishedAt,
      error: this.record.error,
      results: [...this.results].reverse().slice(0, MAX_RETAINED_RESULTS),
    };
  }
}
