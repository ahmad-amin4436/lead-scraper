import 'server-only';

import { AppError, NotFoundError } from '@/lib/errors';
import type { JobCommand, JobEvent, JobSnapshot } from '@/types/job';
import type { SearchRequest } from '@/types/search';
import { createId } from '@/utils/id';
import { Job } from './job';

/** How long a finished job stays queryable before it is evicted. */
const RETENTION_MS = 60 * 60 * 1000;
const SWEEP_INTERVAL_MS = 5 * 60 * 1000;

class JobManager {
  private readonly jobs = new Map<string, Job>();
  private sweepTimer: NodeJS.Timeout | null = null;

  create(request: SearchRequest): Job {
    // One active run at a time: concurrent jobs would contend on the same
    // workbook and blow through provider rate limits.
    const active = this.getActive();
    if (active) {
      throw new AppError(
        'A search is already running. Stop it before starting another.',
        'job_already_running',
        409,
      );
    }

    const job = new Job(createId('job'), request);
    this.jobs.set(job.id, job);
    this.ensureSweeper();
    return job;
  }

  get(jobId: string): Job {
    const job = this.jobs.get(jobId);
    if (!job) throw new NotFoundError(`No job with id "${jobId}"`);
    return job;
  }

  find(jobId: string): Job | null {
    return this.jobs.get(jobId) ?? null;
  }

  getActive(): Job | null {
    for (const job of this.jobs.values()) {
      if (!job.isTerminal) return job;
    }
    return null;
  }

  list(): JobSnapshot[] {
    return [...this.jobs.values()]
      .sort((a, b) => b.startedAt.localeCompare(a.startedAt))
      .map((job) => job.snapshot);
  }

  command(jobId: string, command: JobCommand): JobSnapshot {
    const job = this.get(jobId);

    const applied =
      command === 'pause' ? job.pause() : command === 'resume' ? job.resume() : job.stop();

    if (!applied) {
      throw new AppError(
        `Cannot ${command} a job that is ${job.status}`,
        'invalid_job_transition',
        409,
      );
    }

    return job.snapshot;
  }

  subscribe(jobId: string, listener: (event: JobEvent) => void): () => void {
    return this.get(jobId).subscribe(listener);
  }

  /** Evicts finished jobs past the retention window. */
  private sweep(): void {
    const cutoff = Date.now() - RETENTION_MS;

    for (const [id, job] of this.jobs) {
      if (!job.isTerminal || !job.finishedAt) continue;
      if (Date.parse(job.finishedAt) < cutoff) this.jobs.delete(id);
    }

    if (this.jobs.size === 0 && this.sweepTimer) {
      clearInterval(this.sweepTimer);
      this.sweepTimer = null;
    }
  }

  private ensureSweeper(): void {
    if (this.sweepTimer) return;
    this.sweepTimer = setInterval(() => this.sweep(), SWEEP_INTERVAL_MS);
    // Never keep the Node process alive purely for the sweeper.
    this.sweepTimer.unref?.();
  }
}

const globalForJobs = globalThis as unknown as { leadmineJobManager?: JobManager };

export const jobManager = globalForJobs.leadmineJobManager ?? new JobManager();

if (process.env.NODE_ENV !== 'production') {
  globalForJobs.leadmineJobManager = jobManager;
}
