import 'server-only';

import fs from 'node:fs/promises';
import path from 'node:path';

import { getStore, type Store } from '@netlify/blobs';

/**
 * Persistent key/value storage that behaves the same locally and on Netlify.
 *
 * On Netlify (production and `netlify dev`) it uses Netlify Blobs, which is
 * durable and shared across every serverless instance — the property the old
 * local-filesystem store lacked, and the root cause of jobs/leads vanishing
 * between invocations. Elsewhere (`next dev`, tests, a plain Node host) it falls
 * back to files under a data directory so local development is unchanged.
 *
 * Reads always request strong consistency so a value written by the background
 * function is immediately visible to a polling request. Every method is keyed by
 * a plain string; callers namespace with prefixes (e.g. `job/<id>`).
 */
export type BlobStoreKind = 'netlify-blobs' | 'filesystem';

export interface BlobStore {
  /** Which backend is actually in use — surfaced by /api/health. */
  readonly kind: BlobStoreKind;
  getText(key: string): Promise<string | null>;
  setText(key: string, value: string): Promise<void>;
  getJSON<T>(key: string): Promise<T | null>;
  setJSON<T>(key: string, value: T): Promise<void>;
  getBuffer(key: string): Promise<Buffer | null>;
  setBuffer(key: string, value: Buffer): Promise<void>;
  list(prefix: string): Promise<string[]>;
  delete(key: string): Promise<void>;
}

const STORE_NAME = 'leadmine';

/**
 * Opens the Netlify Blobs store, or returns null when this runtime has no Blobs
 * backend.
 *
 * This deliberately *attempts the operation* rather than sniffing environment
 * variables. An earlier version keyed off `process.env.NETLIFY`, which Netlify
 * sets during builds but NOT inside the Next.js function runtime — so production
 * silently fell back to the per-instance ephemeral filesystem, and every lead
 * written by one invocation was invisible to the next.
 *
 * `getStore()` throws synchronously when the environment is unconfigured, which
 * makes eager construction an honest capability probe. Explicit credentials are
 * honoured as an escape hatch if auto-configuration ever fails.
 */
function openNetlifyStore(): Store | null {
  const siteID =
    process.env.NETLIFY_BLOBS_SITE_ID ?? process.env.NETLIFY_SITE_ID ?? process.env.SITE_ID;
  const token = process.env.NETLIFY_BLOBS_TOKEN ?? process.env.NETLIFY_API_TOKEN;

  try {
    return siteID && token
      ? getStore({ name: STORE_NAME, siteID, token, consistency: 'strong' })
      : getStore({ name: STORE_NAME, consistency: 'strong' });
  } catch (error) {
    console.warn(
      '[storage] Netlify Blobs unavailable, using filesystem:',
      error instanceof Error ? error.message : error,
    );
    return null;
  }
}

class NetlifyBlobStore implements BlobStore {
  readonly kind = 'netlify-blobs' as const;

  constructor(private readonly store: Store) {}

  private store_(): Store {
    return this.store;
  }

  async getText(key: string): Promise<string | null> {
    // The Blobs client returns null for a missing key at runtime, though the
    // typed overload doesn't express it — hence the cast.
    const value = await this.store_().get(key, { type: 'text', consistency: 'strong' });
    return (value as string | null) ?? null;
  }

  async setText(key: string, value: string): Promise<void> {
    await this.store_().set(key, value);
  }

  async getJSON<T>(key: string): Promise<T | null> {
    const value = await this.store_().get(key, { type: 'json', consistency: 'strong' });
    return (value as T | null) ?? null;
  }

  async setJSON<T>(key: string, value: T): Promise<void> {
    await this.store_().setJSON(key, value);
  }

  async getBuffer(key: string): Promise<Buffer | null> {
    const data = await this.store_().get(key, { type: 'arrayBuffer', consistency: 'strong' });
    return data ? Buffer.from(data as ArrayBuffer) : null;
  }

  async setBuffer(key: string, value: Buffer): Promise<void> {
    // BlobInput accepts ArrayBuffer only (not SharedArrayBuffer, which a Buffer's
    // .buffer may be typed as). Copy into a fresh, non-shared ArrayBuffer.
    const copy = new Uint8Array(value.byteLength);
    copy.set(value);
    await this.store_().set(key, copy.buffer);
  }

  async list(prefix: string): Promise<string[]> {
    const { blobs } = await this.store_().list({ prefix });
    return blobs.map((blob) => blob.key);
  }

  async delete(key: string): Promise<void> {
    await this.store_().delete(key);
  }
}

/**
 * Filesystem-backed store for local development. Keys map to files under a root
 * directory; `/` in a key becomes a nested path. Writes are atomic (temp file +
 * rename) so a crash never leaves a half-written file.
 */
class FileBlobStore implements BlobStore {
  readonly kind = 'filesystem' as const;

  private ensured: Promise<void> | null = null;

  constructor(private readonly root: string) {}

  private resolve(key: string): string {
    // Guard against a key escaping the root via `..`.
    const target = path.resolve(this.root, key);
    if (target !== this.root && !target.startsWith(this.root + path.sep)) {
      throw new Error(`Invalid storage key: ${key}`);
    }
    return target;
  }

  private async ensureRoot(): Promise<void> {
    this.ensured ??= fs.mkdir(this.root, { recursive: true }).then(() => undefined);
    await this.ensured;
  }

  async getText(key: string): Promise<string | null> {
    try {
      return await fs.readFile(this.resolve(key), 'utf8');
    } catch (error) {
      if ((error as NodeJS.ErrnoException).code === 'ENOENT') return null;
      throw error;
    }
  }

  private async writeAtomic(key: string, data: string | Uint8Array): Promise<void> {
    await this.ensureRoot();
    const target = this.resolve(key);
    await fs.mkdir(path.dirname(target), { recursive: true });
    const temp = `${target}.${process.pid}.${Date.now()}.tmp`;
    await fs.writeFile(temp, data);
    await fs.rename(temp, target);
  }

  async setText(key: string, value: string): Promise<void> {
    await this.writeAtomic(key, value);
  }

  async getJSON<T>(key: string): Promise<T | null> {
    const text = await this.getText(key);
    if (text === null) return null;
    return JSON.parse(text) as T;
  }

  async setJSON<T>(key: string, value: T): Promise<void> {
    await this.writeAtomic(key, JSON.stringify(value, null, 2));
  }

  async getBuffer(key: string): Promise<Buffer | null> {
    try {
      return await fs.readFile(this.resolve(key));
    } catch (error) {
      if ((error as NodeJS.ErrnoException).code === 'ENOENT') return null;
      throw error;
    }
  }

  async setBuffer(key: string, value: Buffer): Promise<void> {
    await this.writeAtomic(key, value);
  }

  async list(prefix: string): Promise<string[]> {
    await this.ensureRoot();
    const entries: string[] = [];

    const walk = async (dir: string, rel: string): Promise<void> => {
      let dirEntries: import('node:fs').Dirent[];
      try {
        dirEntries = await fs.readdir(dir, { withFileTypes: true, encoding: 'utf8' });
      } catch {
        return;
      }
      for (const entry of dirEntries) {
        const childRel = rel ? `${rel}/${entry.name}` : entry.name;
        if (entry.isDirectory()) {
          await walk(path.join(dir, entry.name), childRel);
        } else if (entry.name.endsWith('.tmp')) {
          continue;
        } else {
          entries.push(childRel);
        }
      }
    };

    await walk(this.root, '');
    return entries.filter((key) => key.startsWith(prefix));
  }

  async delete(key: string): Promise<void> {
    await fs.rm(this.resolve(key), { force: true });
  }
}

function resolveLocalRoot(): string {
  const configured = process.env.LEADMINE_DATA_DIR?.trim();
  // The turbopackIgnore hints stop the bundler treating these runtime data
  // paths as module resolution, which otherwise traces the whole project tree
  // into the deployment bundle.
  if (configured) {
    return path.isAbsolute(configured)
      ? configured
      : path.join(/* turbopackIgnore: true */ process.cwd(), configured);
  }
  return path.join(/* turbopackIgnore: true */ process.cwd(), 'database');
}

function createStore(): BlobStore {
  const netlify = openNetlifyStore();
  if (netlify) return new NetlifyBlobStore(netlify);

  // Only correct on a host with a real, persistent disk. On a serverless
  // platform this directory is per-instance and ephemeral, so data written by
  // one invocation will not be visible to the next — /api/health reports the
  // active backend so this is diagnosable rather than silent.
  return new FileBlobStore(resolveLocalRoot());
}

// Pin to globalThis so the (stateless) store instance and the FileBlobStore's
// mkdir memoization survive dev HMR reloads.
const globalForStore = globalThis as unknown as { leadmineBlobStore?: BlobStore };

export const blobStore: BlobStore = globalForStore.leadmineBlobStore ?? createStore();
globalForStore.leadmineBlobStore = blobStore;
