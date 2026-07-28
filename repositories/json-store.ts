import 'server-only';

import fs from 'node:fs/promises';
import path from 'node:path';

import { ensureDataDir } from '@/lib/paths';
import { Mutex } from '@/utils/async';

/**
 * A single-file JSON store with an in-memory cache and serialised writes.
 *
 * Writes go to a sibling temp file and are then renamed, so a crash mid-write
 * leaves the previous version intact rather than a truncated file.
 */
export class JsonStore<T> {
  private cache: T | null = null;
  private readonly mutex = new Mutex();

  constructor(
    private readonly filePath: string,
    private readonly fallback: () => T,
    /** Optional migration/validation applied to whatever was parsed from disk. */
    private readonly revive: (raw: unknown) => T = (raw) => raw as T,
  ) {}

  async read(): Promise<T> {
    if (this.cache !== null) return this.cache;

    return this.mutex.run(async () => {
      if (this.cache !== null) return this.cache;
      await ensureDataDir();

      try {
        const text = await fs.readFile(this.filePath, 'utf8');
        this.cache = this.revive(JSON.parse(text) as unknown);
      } catch (error) {
        const code = (error as NodeJS.ErrnoException).code;
        if (code !== 'ENOENT') {
          // A corrupt file should not take the app down; start from the default
          // and let the next write replace it.
          console.error(`[json-store] failed to read ${this.filePath}:`, error);
        }
        this.cache = this.fallback();
      }

      return this.cache;
    });
  }

  async write(next: T): Promise<T> {
    return this.mutex.run(async () => {
      await ensureDataDir();
      const temp = `${this.filePath}.${process.pid}.tmp`;
      await fs.writeFile(temp, JSON.stringify(next, null, 2), 'utf8');
      await fs.rename(temp, this.filePath);
      this.cache = next;
      return next;
    });
  }

  /** Read-modify-write under a single lock so concurrent updates don't clobber. */
  async update(mutate: (current: T) => T | Promise<T>): Promise<T> {
    const current = await this.read();
    const next = await mutate(current);
    return this.write(next);
  }

  /** Drops the in-memory copy; the next read hits disk. */
  invalidate(): void {
    this.cache = null;
  }

  get location(): string {
    return this.filePath;
  }

  get directory(): string {
    return path.dirname(this.filePath);
  }
}
