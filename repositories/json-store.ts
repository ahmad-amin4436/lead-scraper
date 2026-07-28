import 'server-only';

import { blobStore } from '@/lib/storage/blob-store';
import { Mutex } from '@/utils/async';

/**
 * True on Netlify, where each request may run in a fresh instance. An in-memory
 * cache there would serve stale data written by a different invocation, so we
 * disable it and always read through to the blob.
 */
const IS_SERVERLESS = Boolean(
  process.env.NETLIFY || process.env.AWS_LAMBDA_FUNCTION_NAME || process.env.NETLIFY_BLOBS_CONTEXT,
);

/**
 * A single JSON document in {@link blobStore}, addressed by key.
 *
 * On a long-lived host the parsed value is cached in memory and writes update
 * the cache. On serverless the cache is bypassed so concurrent instances never
 * disagree — every read fetches the current blob. Writes are serialised through
 * a mutex within a process; cross-process ordering relies on one-writer-at-a-time
 * (search jobs) plus the blob backend's own atomicity.
 */
export class JsonStore<T> {
  private cache: T | null = null;
  private readonly mutex = new Mutex();

  constructor(
    private readonly key: string,
    private readonly fallback: () => T,
    /** Optional migration/validation applied to whatever was parsed from storage. */
    private readonly revive: (raw: unknown) => T = (raw) => raw as T,
  ) {}

  async read(): Promise<T> {
    if (!IS_SERVERLESS && this.cache !== null) return this.cache;

    return this.mutex.run(async () => {
      if (!IS_SERVERLESS && this.cache !== null) return this.cache;

      let value: T;
      try {
        const raw = await blobStore.getJSON<unknown>(this.key);
        value = raw === null ? this.fallback() : this.revive(raw);
      } catch (error) {
        // Corrupt or unreadable data should not take the app down; start from the
        // default and let the next write replace it.
        console.error(`[json-store] failed to read ${this.key}:`, error);
        value = this.fallback();
      }

      if (!IS_SERVERLESS) this.cache = value;
      return value;
    });
  }

  async write(next: T): Promise<T> {
    return this.mutex.run(async () => {
      await blobStore.setJSON(this.key, next);
      if (!IS_SERVERLESS) this.cache = next;
      return next;
    });
  }

  /** Read-modify-write under a single in-process lock so updates don't clobber. */
  async update(mutate: (current: T) => T | Promise<T>): Promise<T> {
    const current = await this.read();
    const next = await mutate(current);
    return this.write(next);
  }

  /** Drops any in-memory copy; the next read hits storage. */
  invalidate(): void {
    this.cache = null;
  }

  get location(): string {
    return this.key;
  }
}
