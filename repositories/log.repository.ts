import '@/lib/server-guard';

import { KEYS } from '@/lib/paths';
import { blobStore } from '@/lib/storage/blob-store';
import type { LogEntry, LogPage, LogQuery } from '@/types/log';
import { Mutex } from '@/utils/async';

/** Entries retained in storage and memory. */
const RETAIN = 5_000;

const IS_SERVERLESS = Boolean(
  process.env.NETLIFY || process.env.AWS_LAMBDA_FUNCTION_NAME || process.env.NETLIFY_BLOBS_CONTEXT,
);

/**
 * Activity log persisted as a single bounded JSON array in {@link blobStore}.
 *
 * The original was an append-only JSONL file, but Netlify Blobs has no append,
 * and — more importantly — the log is written by the background search worker
 * and read by the logs page in a *different* process. So each flush reads the
 * current array, merges the queued batch, trims to the most recent `RETAIN`,
 * and writes it back. The array is small (bounded), so the rewrite is cheap.
 *
 * On a long-lived host the array is also cached in memory; on serverless the
 * cache is bypassed so a reader always sees what the worker last wrote.
 */
/**
 * Parses the stored log, accepting both on-disk formats.
 *
 * Current writes produce a single JSON array. Earlier versions appended
 * newline-delimited JSON (hence the `.jsonl` key), and those files still exist —
 * feeding one to `JSON.parse` throws on the second line, which made every load
 * fail and silently discard the history. Malformed individual lines are skipped
 * rather than failing the whole read.
 */
function parseLog(raw: string | null): LogEntry[] {
  if (!raw) return [];

  const text = raw.trim();
  if (!text) return [];

  if (text.startsWith('[')) {
    const parsed: unknown = JSON.parse(text);
    return Array.isArray(parsed) ? (parsed as LogEntry[]) : [];
  }

  const entries: LogEntry[] = [];
  for (const line of text.split('\n')) {
    const trimmed = line.trim();
    if (!trimmed) continue;
    try {
      entries.push(JSON.parse(trimmed) as LogEntry);
    } catch {
      // A partial line from an interrupted append — skip it.
    }
  }
  return entries;
}

class LogRepository {
  private cache: LogEntry[] | null = null;
  private readonly mutex = new Mutex();
  private queue: LogEntry[] = [];
  private flushTimer: NodeJS.Timeout | null = null;

  private async load(): Promise<LogEntry[]> {
    if (!IS_SERVERLESS && this.cache !== null) return this.cache;

    let entries: LogEntry[] = [];
    try {
      entries = parseLog(await blobStore.getText(KEYS.logs));
    } catch (error) {
      // A corrupt log must never take down the run that is writing to it.
      console.error('[log-repository] failed to read log:', error);
      entries = [];
    }

    if (!IS_SERVERLESS) this.cache = entries;
    return entries;
  }

  /** Buffers an entry and schedules a batched write. */
  async append(entry: LogEntry): Promise<void> {
    this.queue.push(entry);

    if (!IS_SERVERLESS) {
      // Keep the in-memory tail current for cheap local reads.
      const cache = await this.load();
      cache.push(entry);
      if (cache.length > RETAIN) cache.splice(0, cache.length - RETAIN);
    }

    this.scheduleFlush();
  }

  private scheduleFlush(): void {
    if (this.flushTimer) return;
    this.flushTimer = setTimeout(() => {
      this.flushTimer = null;
      void this.flush();
    }, 250);
    this.flushTimer.unref?.();
  }

  async flush(): Promise<void> {
    if (this.queue.length === 0) return;

    await this.mutex.run(async () => {
      const batch = this.queue;
      this.queue = [];
      if (batch.length === 0) return;

      try {
        // Read-modify-write the current array so a batch from another instance
        // isn't lost. Reads storage directly (not the cache) to merge fresh.
        const stored = parseLog(await blobStore.getText(KEYS.logs));
        const merged = [...stored, ...batch];
        const trimmed = merged.length > RETAIN ? merged.slice(-RETAIN) : merged;
        await blobStore.setJSON(KEYS.logs, trimmed);
        if (!IS_SERVERLESS) this.cache = trimmed;
      } catch (error) {
        console.error('[log-repository] failed to persist log entries:', error);
      }
    });
  }

  async query(query: LogQuery): Promise<LogPage> {
    await this.flush();
    const entries = await this.load();
    const search = query.search?.toLowerCase().trim();

    const filtered = entries.filter((entry) => {
      if (query.level && entry.level !== query.level) return false;
      if (query.event && entry.event !== query.event) return false;
      if (query.jobId && entry.jobId !== query.jobId) return false;
      if (search) {
        const haystack = `${entry.message} ${entry.event} ${JSON.stringify(entry.context)}`.toLowerCase();
        if (!haystack.includes(search)) return false;
      }
      return true;
    });

    // Newest first.
    const sorted = [...filtered].reverse();
    const pageSize = Math.max(1, query.pageSize ?? 50);
    const pageCount = Math.max(1, Math.ceil(sorted.length / pageSize));
    const page = Math.min(Math.max(1, query.page ?? 1), pageCount);
    const start = (page - 1) * pageSize;

    return {
      rows: sorted.slice(start, start + pageSize),
      total: sorted.length,
      page,
      pageSize,
      pageCount,
    };
  }

  async recent(limit: number): Promise<LogEntry[]> {
    await this.flush();
    const entries = await this.load();
    return entries.slice(-limit).reverse();
  }

  async clear(): Promise<void> {
    await this.mutex.run(async () => {
      this.queue = [];
      this.cache = [];
      await blobStore.delete(KEYS.logs);
    });
  }
}

const globalForLogs = globalThis as unknown as { leadmineLogRepo?: LogRepository };

export const logRepository = globalForLogs.leadmineLogRepo ?? new LogRepository();

if (process.env.NODE_ENV !== 'production') {
  globalForLogs.leadmineLogRepo = logRepository;
}
