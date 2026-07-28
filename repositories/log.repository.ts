import 'server-only';

import fs from 'node:fs/promises';

import { PATHS, ensureDataDir } from '@/lib/paths';
import type { LogEntry, LogPage, LogQuery } from '@/types/log';
import { Mutex } from '@/utils/async';

/** Entries retained in memory and after compaction. */
const RETAIN = 5_000;
/** Line count that triggers a rewrite of the log file. */
const COMPACT_AT = 20_000;

/**
 * Append-only JSONL log with an in-memory tail.
 *
 * Appends are cheap (one `fs.appendFile` per batch) and reads are served from
 * memory. The file is compacted to the most recent `RETAIN` entries once it
 * grows past `COMPACT_AT` lines.
 */
class LogRepository {
  private entries: LogEntry[] | null = null;
  private linesOnDisk = 0;
  private readonly mutex = new Mutex();
  private queue: LogEntry[] = [];
  private flushTimer: NodeJS.Timeout | null = null;

  private async ensureLoaded(): Promise<LogEntry[]> {
    if (this.entries !== null) return this.entries;

    return this.mutex.run(async () => {
      if (this.entries !== null) return this.entries;
      await ensureDataDir();

      let parsed: LogEntry[] = [];
      let lineCount = 0;

      try {
        const text = await fs.readFile(PATHS.logs, 'utf8');
        const lines = text.split('\n').filter((line) => line.trim().length > 0);
        lineCount = lines.length;

        for (const line of lines.slice(-RETAIN)) {
          try {
            parsed.push(JSON.parse(line) as LogEntry);
          } catch {
            // Skip malformed lines (e.g. a partial write before a crash).
          }
        }
      } catch (error) {
        if ((error as NodeJS.ErrnoException).code !== 'ENOENT') {
          console.error('[log-repository] failed to read log file:', error);
        }
        parsed = [];
      }

      this.entries = parsed;
      this.linesOnDisk = lineCount;
      return this.entries;
    });
  }

  /** Buffers an entry and schedules a batched disk write. */
  async append(entry: LogEntry): Promise<void> {
    const entries = await this.ensureLoaded();
    entries.push(entry);
    if (entries.length > RETAIN) entries.splice(0, entries.length - RETAIN);

    this.queue.push(entry);
    this.scheduleFlush();
  }

  private scheduleFlush(): void {
    if (this.flushTimer) return;
    this.flushTimer = setTimeout(() => {
      this.flushTimer = null;
      void this.flush();
    }, 250);
    // Don't hold the process open just for a log flush.
    this.flushTimer.unref?.();
  }

  async flush(): Promise<void> {
    if (this.queue.length === 0) return;

    await this.mutex.run(async () => {
      const batch = this.queue;
      this.queue = [];
      if (batch.length === 0) return;

      await ensureDataDir();
      const payload = batch.map((entry) => JSON.stringify(entry)).join('\n') + '\n';

      try {
        await fs.appendFile(PATHS.logs, payload, 'utf8');
        this.linesOnDisk += batch.length;
      } catch (error) {
        console.error('[log-repository] failed to append log entries:', error);
        return;
      }

      if (this.linesOnDisk > COMPACT_AT) await this.compact();
    });
  }

  /** Rewrites the file with the retained in-memory tail. Caller holds the lock. */
  private async compact(): Promise<void> {
    const retained = (this.entries ?? []).slice(-RETAIN);
    const temp = `${PATHS.logs}.${process.pid}.tmp`;
    const payload = retained.map((entry) => JSON.stringify(entry)).join('\n') + '\n';

    try {
      await fs.writeFile(temp, payload, 'utf8');
      await fs.rename(temp, PATHS.logs);
      this.linesOnDisk = retained.length;
    } catch (error) {
      console.error('[log-repository] compaction failed:', error);
    }
  }

  async query(query: LogQuery): Promise<LogPage> {
    await this.flush();
    const entries = await this.ensureLoaded();
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
    const entries = await this.ensureLoaded();
    return entries.slice(-limit).reverse();
  }

  async clear(): Promise<void> {
    await this.mutex.run(async () => {
      this.queue = [];
      this.entries = [];
      this.linesOnDisk = 0;
      await ensureDataDir();
      await fs.rm(PATHS.logs, { force: true });
    });
  }
}

const globalForLogs = globalThis as unknown as { leadmineLogRepo?: LogRepository };

export const logRepository = globalForLogs.leadmineLogRepo ?? new LogRepository();

if (process.env.NODE_ENV !== 'production') {
  globalForLogs.leadmineLogRepo = logRepository;
}
