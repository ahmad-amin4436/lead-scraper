import 'server-only';

import fs from 'node:fs/promises';
import path from 'node:path';

import { EXPORTS_DIR, PATHS, isInsideExports } from '@/lib/paths';
import type { ExportRecord } from '@/types/export';
import { JsonStore } from './json-store';

const MAX_ENTRIES = 200;

const store = new JsonStore<ExportRecord[]>(
  PATHS.exports,
  () => [],
  (raw) => (Array.isArray(raw) ? (raw as ExportRecord[]) : []),
);

export const exportRepository = {
  async list(): Promise<ExportRecord[]> {
    const entries = await store.read();
    return [...entries].sort((a, b) => b.createdAt.localeCompare(a.createdAt));
  },

  async getById(id: string): Promise<ExportRecord | null> {
    const entries = await store.read();
    return entries.find((e) => e.id === id) ?? null;
  },

  async add(record: ExportRecord): Promise<ExportRecord> {
    const evicted: ExportRecord[] = [];

    await store.update((current) => {
      const next = [record, ...current];
      evicted.push(...next.slice(MAX_ENTRIES));
      return next.slice(0, MAX_ENTRIES);
    });

    // Keep the exports folder in step with the trimmed history.
    await Promise.all(evicted.map((entry) => this.removeFile(entry.fileName)));
    return record;
  },

  /** Absolute path for a stored export, or null if it escapes the exports dir. */
  resolvePath(fileName: string): string | null {
    const candidate = path.join(EXPORTS_DIR, path.basename(fileName));
    return isInsideExports(candidate) ? candidate : null;
  },

  async removeFile(fileName: string): Promise<void> {
    const target = this.resolvePath(fileName);
    if (!target) return;
    await fs.rm(target, { force: true });
  },

  async remove(id: string): Promise<boolean> {
    const record = await this.getById(id);
    if (!record) return false;

    await store.update((current) => current.filter((e) => e.id !== id));
    await this.removeFile(record.fileName);
    return true;
  },

  async clear(): Promise<void> {
    const entries = await store.read();
    await Promise.all(entries.map((entry) => this.removeFile(entry.fileName)));
    await store.write([]);
  },
};
