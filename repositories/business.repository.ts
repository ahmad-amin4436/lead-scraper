import 'server-only';

import { KEYS } from '@/lib/paths';
import { blobStore } from '@/lib/storage/blob-store';
import {
  type BusinessPage,
  type BusinessQuery,
  type BusinessRecord,
  type BusinessStats,
} from '@/types/business';
import { Mutex } from '@/utils/async';
import { normalizeHost, normalizePhone } from '@/utils/normalize';
import { normalizeText } from '@/utils/normalize';

const IS_SERVERLESS = Boolean(
  process.env.NETLIFY || process.env.AWS_LAMBDA_FUNCTION_NAME || process.env.NETLIFY_BLOBS_CONTEXT,
);

export interface InsertResult {
  inserted: BusinessRecord[];
  duplicates: BusinessRecord[];
}

/**
 * JSON-backed store for lead records, persisted to {@link blobStore}.
 *
 * Lead data lives in a single JSON array blob (`businesses.json`). This replaced
 * an Excel workbook as the primary store because Excel offered no durable,
 * cross-instance persistence on serverless — Excel/CSV are now generated on
 * demand by the export service instead.
 *
 * On a long-lived host the array and its dedupe/id indexes are cached in memory,
 * and mutations mark the store dirty for a coalesced `flush()`. On serverless
 * every read fetches the current blob and every mutation persists immediately,
 * so instances never disagree; the "one search job at a time" rule keeps the
 * single writer (the background function) from racing itself.
 */
class BusinessRepository {
  private rows: BusinessRecord[] | null = null;
  private byId = new Map<string, BusinessRecord>();
  /** dedupe key -> record id */
  private dedupeIndex = new Map<string, string>();
  private readonly ioMutex = new Mutex();
  private dirty = false;
  private pendingFlush: Promise<void> | null = null;

  private async ensureLoaded(): Promise<BusinessRecord[]> {
    if (!IS_SERVERLESS && this.rows !== null) return this.rows;

    return this.ioMutex.run(async () => {
      if (!IS_SERVERLESS && this.rows !== null) return this.rows;

      let rows: BusinessRecord[] = [];
      try {
        const stored = await blobStore.getJSON<BusinessRecord[]>(KEYS.businesses);
        rows = Array.isArray(stored) ? stored : [];
      } catch (error) {
        console.error('[business-repository] failed to read store:', error);
        rows = [];
      }

      this.rows = rows;
      this.rebuildIndexes();
      return this.rows;
    });
  }

  private rebuildIndexes(): void {
    this.byId = new Map();
    this.dedupeIndex = new Map();
    for (const row of this.rows ?? []) {
      this.byId.set(row.id, row);
      for (const key of dedupeKeys(row)) {
        if (!this.dedupeIndex.has(key)) this.dedupeIndex.set(key, row.id);
      }
    }
  }

  private indexRecord(record: BusinessRecord): void {
    this.byId.set(record.id, record);
    for (const key of dedupeKeys(record)) {
      if (!this.dedupeIndex.has(key)) this.dedupeIndex.set(key, record.id);
    }
  }

  /**
   * Writes the current rows to the JSON blob.
   *
   * The caller MUST already hold `ioMutex` — this method does not acquire it,
   * so code paths already inside the lock can write without deadlocking. Use
   * `persist()` from unlocked contexts.
   */
  private async writeStore(): Promise<void> {
    if (!this.dirty || this.rows === null) return;
    const snapshot = [...this.rows];
    await blobStore.setJSON(KEYS.businesses, snapshot);
    this.dirty = false;
  }

  /** Acquires the IO lock and writes the store. Never call while holding it. */
  private async persist(): Promise<void> {
    await this.ioMutex.run(() => this.writeStore());
  }

  /**
   * Coalesces concurrent flush requests: callers arriving while a write is in
   * flight await that write, and a follow-up write runs only if still dirty.
   */
  async flush(): Promise<void> {
    if (!this.dirty) return;
    this.pendingFlush ??= this.persist().finally(() => {
      this.pendingFlush = null;
    });
    await this.pendingFlush;
    if (this.dirty) await this.flush();
  }

  async getAll(): Promise<BusinessRecord[]> {
    return [...(await this.ensureLoaded())];
  }

  async getById(id: string): Promise<BusinessRecord | null> {
    await this.ensureLoaded();
    return this.byId.get(id) ?? null;
  }

  async getByIds(ids: readonly string[]): Promise<BusinessRecord[]> {
    await this.ensureLoaded();
    return ids.map((id) => this.byId.get(id)).filter((r): r is BusinessRecord => Boolean(r));
  }

  /** Returns the existing record that `candidate` would duplicate, if any. */
  async findDuplicate(candidate: BusinessRecord): Promise<BusinessRecord | null> {
    await this.ensureLoaded();
    for (const key of dedupeKeys(candidate)) {
      const id = this.dedupeIndex.get(key);
      if (id) return this.byId.get(id) ?? null;
    }
    return null;
  }

  /**
   * Appends records, skipping any that duplicate an existing row or an earlier
   * record in the same batch. Does not write to disk — call `flush()`.
   */
  async insertMany(records: readonly BusinessRecord[], skipDuplicates = true): Promise<InsertResult> {
    const rows = await this.ensureLoaded();
    const inserted: BusinessRecord[] = [];
    const duplicates: BusinessRecord[] = [];

    for (const record of records) {
      if (skipDuplicates) {
        const existing = await this.findDuplicate(record);
        if (existing) {
          duplicates.push(record);
          continue;
        }
      }
      rows.push(record);
      this.indexRecord(record);
      inserted.push(record);
    }

    if (inserted.length > 0) this.dirty = true;
    return { inserted, duplicates };
  }

  async insert(record: BusinessRecord, skipDuplicates = true): Promise<BusinessRecord | null> {
    const { inserted } = await this.insertMany([record], skipDuplicates);
    return inserted[0] ?? null;
  }

  async update(id: string, patch: Partial<BusinessRecord>): Promise<BusinessRecord | null> {
    const rows = await this.ensureLoaded();
    const index = rows.findIndex((r) => r.id === id);
    if (index === -1) return null;

    const updated: BusinessRecord = { ...rows[index], ...patch, id };
    rows[index] = updated;
    this.rebuildIndexes();
    this.dirty = true;
    await this.flush();
    return updated;
  }

  async deleteMany(ids: readonly string[]): Promise<number> {
    const rows = await this.ensureLoaded();
    const target = new Set(ids);
    const remaining = rows.filter((r) => !target.has(r.id));
    const removed = rows.length - remaining.length;

    if (removed > 0) {
      this.rows = remaining;
      this.rebuildIndexes();
      this.dirty = true;
      await this.flush();
    }

    return removed;
  }

  async clear(): Promise<number> {
    const rows = await this.ensureLoaded();
    const removed = rows.length;
    this.rows = [];
    this.rebuildIndexes();
    this.dirty = true;
    await this.flush();
    return removed;
  }

  async query(query: BusinessQuery): Promise<BusinessPage> {
    const rows = await this.ensureLoaded();
    const filtered = applyFilters(rows, query);

    const sortBy = query.sortBy ?? 'dateAdded';
    const direction = query.sortDir === 'asc' ? 1 : -1;
    const sorted = [...filtered].sort((a, b) => compareRecords(a, b, sortBy) * direction);

    const pageSize = Math.max(1, query.pageSize ?? 25);
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

  /** Applies filters without pagination — used by the export service. */
  async queryAll(query: BusinessQuery): Promise<BusinessRecord[]> {
    const rows = await this.ensureLoaded();
    const filtered = applyFilters(rows, query);
    const sortBy = query.sortBy ?? 'dateAdded';
    const direction = query.sortDir === 'asc' ? 1 : -1;
    return [...filtered].sort((a, b) => compareRecords(a, b, sortBy) * direction);
  }

  async stats(): Promise<BusinessStats> {
    const rows = await this.ensureLoaded();

    const ratings = rows.map((r) => r.rating).filter((r): r is number => typeof r === 'number');
    const averageRating =
      ratings.length > 0 ? ratings.reduce((sum, r) => sum + r, 0) / ratings.length : null;

    const days: { date: string; count: number }[] = [];
    for (let offset = 6; offset >= 0; offset -= 1) {
      const day = new Date();
      day.setUTCHours(0, 0, 0, 0);
      day.setUTCDate(day.getUTCDate() - offset);
      const key = day.toISOString().slice(0, 10);
      days.push({
        date: key,
        count: rows.filter((r) => r.dateAdded.slice(0, 10) === key).length,
      });
    }

    return {
      total: rows.length,
      withEmail: rows.filter((r) => r.email).length,
      withPhone: rows.filter((r) => r.phone).length,
      withWebsite: rows.filter((r) => r.website).length,
      withSocial: rows.filter((r) => r.facebook || r.instagram || r.linkedin || r.whatsapp).length,
      enriched: rows.filter((r) => r.status === 'enriched' || r.status === 'partial').length,
      verifiedEmails: rows.filter((r) => r.emailStatus === 'valid').length,
      whatsappReachable: rows.filter(
        (r) => r.whatsappStatus === 'confirmed' || r.whatsappStatus === 'likely',
      ).length,
      averageRating,
      byCategory: topCounts(rows.map((r) => r.category), 8),
      byCountry: topCounts(rows.map((r) => r.country), 8),
      bySource: topCounts(rows.map((r) => r.source), 5),
      addedLast7Days: days,
    };
  }

  /** Forces the next read to reload from disk (used after external edits). */
  async reload(): Promise<void> {
    await this.flush();
    this.rows = null;
    this.byId.clear();
    this.dedupeIndex.clear();
  }
}

/**
 * Identity keys for duplicate detection. A candidate collides when *any* key
 * matches an existing row: same website host, same phone, or same
 * name-within-city.
 */
function dedupeKeys(record: BusinessRecord): string[] {
  const keys: string[] = [];

  const host = record.website ? normalizeHost(record.website) : '';
  if (host) keys.push(`web:${host}`);

  const phone = record.phone ? normalizePhone(record.phone) : '';
  // Short strings are too collision-prone to trust as an identity.
  if (phone.replace(/\D/g, '').length >= 7) keys.push(`tel:${phone.replace(/\D/g, '').slice(-10)}`);

  const name = normalizeText(record.name);
  const city = normalizeText(record.city || record.country);
  if (name) keys.push(`name:${name}|${city}`);

  return keys;
}

function applyFilters(rows: readonly BusinessRecord[], query: BusinessQuery): BusinessRecord[] {
  const search = query.search ? normalizeText(query.search) : '';

  return rows.filter((row) => {
    if (query.category && row.category !== query.category) return false;
    if (query.country && row.country !== query.country) return false;
    if (query.city && row.city !== query.city) return false;
    if (query.source && row.source !== query.source) return false;
    if (query.status && row.status !== query.status) return false;
    if (query.minRating !== undefined && (row.rating ?? 0) < query.minRating) return false;
    if (query.minReviews !== undefined && (row.reviewCount ?? 0) < query.minReviews) return false;
    if (query.hasEmail !== undefined && Boolean(row.email) !== query.hasEmail) return false;
    if (query.hasPhone !== undefined && Boolean(row.phone) !== query.hasPhone) return false;
    if (query.hasWebsite !== undefined && Boolean(row.website) !== query.hasWebsite) return false;

    if (search) {
      const haystack = normalizeText(
        `${row.name} ${row.category} ${row.city} ${row.country} ${row.email} ${row.phone} ${row.website} ${row.address}`,
      );
      if (!haystack.includes(search)) return false;
    }

    return true;
  });
}

function compareRecords(
  a: BusinessRecord,
  b: BusinessRecord,
  key: keyof BusinessRecord,
): number {
  const left = a[key];
  const right = b[key];

  if (left === right) return 0;
  if (left === null || left === undefined || left === '') return -1;
  if (right === null || right === undefined || right === '') return 1;

  if (typeof left === 'number' && typeof right === 'number') return left - right;
  return String(left).localeCompare(String(right), 'en', { sensitivity: 'base' });
}

function topCounts(values: readonly string[], limit: number): { label: string; count: number }[] {
  const counts = new Map<string, number>();
  for (const value of values) {
    if (!value) continue;
    counts.set(value, (counts.get(value) ?? 0) + 1);
  }
  return [...counts.entries()]
    .map(([label, count]) => ({ label, count }))
    .sort((a, b) => b.count - a.count)
    .slice(0, limit);
}

/**
 * Module-level singleton, pinned to `globalThis` so the in-memory cache and
 * indexes survive HMR module reloads in development.
 */
const globalForRepo = globalThis as unknown as { leadmineBusinessRepo?: BusinessRepository };

export const businessRepository =
  globalForRepo.leadmineBusinessRepo ?? new BusinessRepository();

if (process.env.NODE_ENV !== 'production') {
  globalForRepo.leadmineBusinessRepo = businessRepository;
}
