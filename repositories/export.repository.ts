import 'server-only';

import { KEYS, exportFileKey } from '@/lib/paths';
import { blobStore } from '@/lib/storage/blob-store';
import type { ExportRecord } from '@/types/export';
import { JsonStore } from './json-store';

const MAX_ENTRIES = 200;

const store = new JsonStore<ExportRecord[]>(
  KEYS.exports,
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

    // Keep the stored export files in step with the trimmed history.
    await Promise.all(evicted.map((entry) => this.removeFile(entry.fileName)));
    return record;
  },

  /** Storage key for a stored export file. */
  fileKey(fileName: string): string {
    return exportFileKey(fileName);
  },

  async removeFile(fileName: string): Promise<void> {
    await blobStore.delete(exportFileKey(fileName));
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
