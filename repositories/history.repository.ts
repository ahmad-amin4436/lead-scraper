import 'server-only';

import { PATHS } from '@/lib/paths';
import type { SearchHistoryEntry } from '@/types/search';
import { JsonStore } from './json-store';

const MAX_ENTRIES = 500;

const store = new JsonStore<SearchHistoryEntry[]>(
  PATHS.history,
  () => [],
  (raw) => (Array.isArray(raw) ? (raw as SearchHistoryEntry[]) : []),
);

export const historyRepository = {
  async list(limit?: number): Promise<SearchHistoryEntry[]> {
    const entries = await store.read();
    const sorted = [...entries].sort((a, b) => b.startedAt.localeCompare(a.startedAt));
    return limit ? sorted.slice(0, limit) : sorted;
  },

  async getById(id: string): Promise<SearchHistoryEntry | null> {
    const entries = await store.read();
    return entries.find((e) => e.id === id) ?? null;
  },

  async add(entry: SearchHistoryEntry): Promise<SearchHistoryEntry> {
    await store.update((current) => [entry, ...current].slice(0, MAX_ENTRIES));
    return entry;
  },

  async remove(id: string): Promise<boolean> {
    let removed = false;
    await store.update((current) => {
      const next = current.filter((e) => e.id !== id);
      removed = next.length !== current.length;
      return next;
    });
    return removed;
  },

  async clear(): Promise<void> {
    await store.write([]);
  },
};
