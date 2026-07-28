import 'server-only';

import type { JobCounters, JobEvent, JobProgress, JobSnapshot, JobStatus } from '@/types/job';
import type { LiveResult, SearchRequest } from '@/types/search';

/** Live results retained per job; older ones are dropped to bound memory. */
const MAX_RETAINED_RESULTS = 300;

type Listener = (event: JobEvent) => void;

/**
 * A single search run: its state machine, counters, and event fan-out.
 *
 * Pause is implemented as a gate that `waitWhilePaused` awaits, so the runner
 * suspends at checkpoints rather than being interrupted mid-request. Stop aborts
 * the shared `AbortSignal`, which unwinds in-flight HTTP calls.
 */
export class Job {
  readonly id: string;
  readonly request: SearchRequest;
  readonly startedAt: string;

  status: JobStatus = 'queued';
  error: string | null = null;
  finishedAt: string | null = null;
  currentTask = 'Preparing';

  readonly counters: JobCounters = {
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
  };

  private readonly controller = new AbortController();
  private readonly listeners = new Set<Listener>();
  private results: LiveResult[] = [];
  private startedAtMs = Date.now();
  private pauseGate: { promise: Promise<void>; release: () => void } | null = null;
  /** Fractional progress inside the current task, 0-1. */
  private taskFraction = 0;

  constructor(id: string, request: SearchRequest) {
    this.id = id;
    this.request = request;
    this.startedAt = new Date().toISOString();
  }

  get signal(): AbortSignal {
    return this.controller.signal;
  }

  get isTerminal(): boolean {
    return this.status === 'completed' || this.status === 'failed' || this.status === 'stopped';
  }

  // --- lifecycle -----------------------------------------------------------

  markRunning(totalTasks: number): void {
    this.status = 'running';
    this.startedAtMs = Date.now();
    this.counters.totalTasks = totalTasks;
    this.emitStatus();
  }

  pause(): boolean {
    if (this.status !== 'running') return false;
    this.status = 'paused';

    let release!: () => void;
    const promise = new Promise<void>((resolve) => {
      release = resolve;
    });
    this.pauseGate = { promise, release };

    this.emitStatus();
    return true;
  }

  resume(): boolean {
    if (this.status !== 'paused') return false;
    this.status = 'running';
    this.pauseGate?.release();
    this.pauseGate = null;
    this.emitStatus();
    return true;
  }

  stop(): boolean {
    if (this.isTerminal) return false;
    this.status = 'stopping';
    // Release the gate so a paused runner wakes up and observes the abort.
    this.pauseGate?.release();
    this.pauseGate = null;
    this.controller.abort();
    this.emitStatus();
    return true;
  }

  finish(status: Extract<JobStatus, 'completed' | 'failed' | 'stopped'>, error?: string): void {
    this.status = status;
    this.error = error ?? null;
    this.finishedAt = new Date().toISOString();
    this.currentTask = status === 'completed' ? 'Finished' : status === 'stopped' ? 'Stopped' : 'Failed';
    this.taskFraction = 0;
    this.pauseGate?.release();
    this.pauseGate = null;

    this.emit({ type: 'status', jobId: this.id, status, error: this.error });
    this.emit({ type: 'done', jobId: this.id, status });
  }

  /** Suspends the runner while paused. Resolves immediately when running. */
  async waitWhilePaused(): Promise<void> {
    while (this.pauseGate && this.status === 'paused') {
      await this.pauseGate.promise;
    }
  }

  // --- progress ------------------------------------------------------------

  setTask(label: string, fraction = 0): void {
    this.currentTask = label;
    this.taskFraction = Math.max(0, Math.min(1, fraction));
    this.emitProgress();
  }

  completeTask(): void {
    this.counters.completedTasks += 1;
    this.taskFraction = 0;
    this.emitProgress();
  }

  addCount(key: keyof JobCounters, amount = 1): void {
    this.counters[key] += amount;
  }

  addResult(result: LiveResult): void {
    this.results.push(result);
    if (this.results.length > MAX_RETAINED_RESULTS) {
      this.results.splice(0, this.results.length - MAX_RETAINED_RESULTS);
    }
    this.emit({ type: 'result', jobId: this.id, result });
  }

  get progress(): JobProgress {
    const { totalTasks, completedTasks } = this.counters;
    const elapsedMs = (this.finishedAt ? Date.parse(this.finishedAt) : Date.now()) - this.startedAtMs;

    const done = completedTasks + this.taskFraction;
    const percent = totalTasks > 0 ? Math.min(100, (done / totalTasks) * 100) : 0;

    // Extrapolate from completed tasks only; a partial task is too noisy to trust.
    const etaMs =
      completedTasks > 0 && completedTasks < totalTasks && this.status === 'running'
        ? Math.round((elapsedMs / completedTasks) * (totalTasks - completedTasks))
        : null;

    return {
      percent: this.isTerminal && this.status === 'completed' ? 100 : Number(percent.toFixed(1)),
      elapsedMs,
      etaMs,
      currentTask: this.currentTask,
    };
  }

  get snapshot(): JobSnapshot {
    return {
      id: this.id,
      status: this.status,
      request: this.request,
      counters: { ...this.counters },
      progress: this.progress,
      startedAt: this.startedAt,
      finishedAt: this.finishedAt,
      error: this.error,
      results: [...this.results].reverse(),
    };
  }

  get elapsedMs(): number {
    return (this.finishedAt ? Date.parse(this.finishedAt) : Date.now()) - this.startedAtMs;
  }

  // --- events --------------------------------------------------------------

  subscribe(listener: Listener): () => void {
    this.listeners.add(listener);
    listener({ type: 'snapshot', job: this.snapshot });
    return () => {
      this.listeners.delete(listener);
    };
  }

  private emit(event: JobEvent): void {
    for (const listener of this.listeners) {
      try {
        listener(event);
      } catch (error) {
        // A broken SSE consumer must not derail the job.
        console.error('[job] listener error:', error);
      }
    }
  }

  emitProgress(): void {
    this.emit({
      type: 'progress',
      jobId: this.id,
      counters: { ...this.counters },
      progress: this.progress,
      status: this.status,
    });
  }

  private emitStatus(): void {
    this.emit({ type: 'status', jobId: this.id, status: this.status, error: this.error });
    this.emitProgress();
  }
}
