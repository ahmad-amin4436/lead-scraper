import { randomUUID } from 'node:crypto';

/** Short, sortable, collision-resistant id: base36 timestamp + random suffix. */
export function createId(prefix?: string): string {
  const stamp = Date.now().toString(36);
  const random = randomUUID().replace(/-/g, '').slice(0, 8);
  const id = `${stamp}${random}`;
  return prefix ? `${prefix}_${id}` : id;
}

/** Combining diacritical marks, stripped after NFKD normalisation. */
const COMBINING_MARKS = /[̀-ͯ]/g;

export function slugify(value: string): string {
  return value
    .normalize('NFKD')
    .replace(COMBINING_MARKS, '')
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '')
    .slice(0, 80);
}
