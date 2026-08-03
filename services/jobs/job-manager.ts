import '@/lib/server-guard';

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
 * No heartbeat for this long ⇒ the worker is gone. Generous enough to survive a
 * slow enrichment pass between writes, short enough that a dead run doesn't
 * block the next search for long.
 */
const HEARTBEAT_TIMEOUT_MS = 5 * 60 * 1000;

/**
 * Hard ceiling on a run. Netlify kills a Background Function at 15 minutes, so
 * anything older than that is dead no matter what the last heartbeat claimed.
 */
const MAX_RUN_MS = 16 * 60 * 1000;

function isTerminalStatus(status: JobRecord['status']): boolean {
  return status === 'completed' || status === 'failed' || status === 'stopped';
}

/**
 * True when a non-terminal job's worker has evidently died.
 *
 * Without this, a function that times out mid-run leaves the job `running` or
 * `stopping` forever: the UI shows a progress bar that never moves, and
 * `getActive()` refuses to start any new search.
 */
function isStale(record: JobRecord): boolean {
  if (isTerminalStatus(record.status)) return false;

  const startedMs = Date.parse(record.startedAt);
  if (Number.isFinite(startedMs) && Date.now() - startedMs > MAX_RUN_MS) return true;

  // Jobs written before heartbeats existed fall back to their start time.
  const lastBeat = Date.parse(record.heartbeatAt ?? record.startedAt);
  if (!Number.isFinite(lastBeat)) return false;

  return Date.now() - lastBeat > HEARTBEAT_TIMEOUT_MS;
}

/**
 * Blob-backed registry of search jobs.
 *
 * Every method reads or writes `jobs/<id>` in {@link blobStore}, so a job
 * created by one serverless instance is visible to every other — the fix for
 * the stream/poll 404, where an in-memory Map lived in a single process.
 */
class JobManager {
  async create(request: SearchRequest, ownerUserId?: string | null): Promise<Job> {
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
    return Job.create(createId('job'), request, ownerUserId);
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

  /**
   * Finalises jobs whose worker has died, returning the reconciled set.
   *
   * A stale job is closed as `stopped` when a stop was already requested (the
   * user's intent is honoured even though the worker never saw the flag),
   * otherwise as `failed` with an explanatory error.
   */
  private async reconcile(records: JobRecord[]): Promise<JobRecord[]> {
    const reconciled = await Promise.all(
      records.map(async (record) => {
        if (!isStale(record)) return record;

        const finished: JobRecord = {
          ...record,
          status: record.stopRequested ? 'stopped' : 'failed',
          finishedAt: new Date().toISOString(),
          error: record.stopRequested
            ? null
            : 'The worker stopped responding, so the run was ended. Any leads saved before that point were kept.',
          progress: {
            ...record.progress,
            currentTask: record.stopRequested ? 'Stopped' : 'Ended — worker stopped responding',
            etaMs: null,
          },
        };

        try {
          await blobStore.setJSON(jobKey(record.id), finished);
        } catch (error) {
          console.error(`[job-manager] failed to finalise stale job ${record.id}:`, error);
          return record;
        }

        return finished;
      }),
    );

    return reconciled;
  }

  async getActive(): Promise<Job | null> {
    const records = await this.reconcile(await this.records());
    const active = records.find(
      (r) => r.status === 'queued' || r.status === 'running' || r.status === 'stopping',
    );
    return active ? this.get(active.id) : null;
  }

  async list(): Promise<JobSnapshot[]> {
    const records = await this.reconcile(await this.records());
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

    if (isTerminalStatus(record.status)) {
      throw new AppError(`Cannot stop a job that is ${record.status}`, 'invalid_job_transition', 409);
    }

    record.stopRequested = true;

    if (isStale(record)) {
      // No live worker will ever observe the flag, so close it out here rather
      // than leaving the job wedged in `stopping` forever.
      record.status = 'stopped';
      record.finishedAt = new Date().toISOString();
      record.progress = { ...record.progress, currentTask: 'Stopped', etaMs: null };
    } else if (record.status === 'running' || record.status === 'queued') {
      record.status = 'stopping';
    }

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
