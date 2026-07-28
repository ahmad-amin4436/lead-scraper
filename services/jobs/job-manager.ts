import 'server-only';

import { AppError, NotFoundError } from '@/lib/errors';
import { KEYS, jobKey } from '@/lib/paths';
import { blobStore } from '@/lib/storage/blob-store';
import type { JobCommand, JobRecord, JobSnapshot } from '@/types/job';
import type { SearchRequest } from '@/types/search';
import { createId } from '@/utils/id';
import { Job } from './job';

/** How long a finished job stays queryable before it is swept. */
const RETENTION_MS = 60 * 60 * 1000;

/**
 * Blob-backed registry of search jobs.
 *
 * Every method reads or writes `jobs/<id>` in {@link blobStore}, so a job
 * created by one serverless instance is visible to every other — the fix for
 * the stream/poll 404, where an in-memory Map lived in a single process.
 */
class JobManager {
  async create(request: SearchRequest): Promise<Job> {
    // One active run at a time: concurrent jobs would contend on the same lead
    // store and blow through provider rate limits. Also serialises the single
    // writer so blob read-modify-write stays safe.
    const active = await this.getActive();
    if (active) {
      throw new AppError(
        'A search is already running. Stop it before starting another.',
        'job_already_running',
        409,
      );
    }

    await this.sweep();
    return Job.create(createId('job'), request);
  }

  async find(jobId: string): Promise<Job | null> {
    return Job.load(jobId);
  }

  async get(jobId: string): Promise<Job> {
    const job = await Job.load(jobId);
    if (!job) throw new NotFoundError(`No job with id "${jobId}"`);
    return job;
  }

  private async records(): Promise<JobRecord[]> {
    const keys = await blobStore.list(KEYS.jobsPrefix);
    const loaded = await Promise.all(keys.map((key) => blobStore.getJSON<JobRecord>(key)));
    return loaded.filter((r): r is JobRecord => r !== null);
  }

  async getActive(): Promise<Job | null> {
    const records = await this.records();
    const active = records.find(
      (r) => r.status === 'queued' || r.status === 'running' || r.status === 'stopping',
    );
    return active ? this.get(active.id) : null;
  }

  async list(): Promise<JobSnapshot[]> {
    const records = await this.records();
    return records
      .sort((a, b) => b.startedAt.localeCompare(a.startedAt))
      .map(toSnapshot);
  }

  /**
   * Applies a command. Only `stop` exists now: it sets the cooperative flag in
   * the job blob, which the running worker observes at its next checkpoint.
   */
  async command(jobId: string, command: JobCommand): Promise<JobSnapshot> {
    if (command !== 'stop') {
      throw new AppError(`Unsupported command "${command}"`, 'invalid_job_transition', 400);
    }

    const record = await blobStore.getJSON<JobRecord>(jobKey(jobId));
    if (!record) throw new NotFoundError(`No job with id "${jobId}"`);

    const terminal =
      record.status === 'completed' || record.status === 'failed' || record.status === 'stopped';
    if (terminal) {
      throw new AppError(`Cannot stop a job that is ${record.status}`, 'invalid_job_transition', 409);
    }

    record.stopRequested = true;
    if (record.status === 'running' || record.status === 'queued') record.status = 'stopping';
    await blobStore.setJSON(jobKey(jobId), record);
    return toSnapshot(record);
  }

  /** Deletes finished jobs past the retention window. Best-effort. */
  private async sweep(): Promise<void> {
    try {
      const records = await this.records();
      const cutoff = Date.now() - RETENTION_MS;
      await Promise.all(
        records
          .filter((r) => r.finishedAt && Date.parse(r.finishedAt) < cutoff)
          .map((r) => blobStore.delete(jobKey(r.id))),
      );
    } catch (error) {
      console.error('[job-manager] sweep failed:', error);
    }
  }
}

function toSnapshot(record: JobRecord): JobSnapshot {
  return {
    id: record.id,
    status: record.status,
    request: record.request,
    counters: record.counters,
    progress: record.progress,
    startedAt: record.startedAt,
    finishedAt: record.finishedAt,
    error: record.error,
    results: record.results ?? [],
  };
}

export const jobManager = new JobManager();
