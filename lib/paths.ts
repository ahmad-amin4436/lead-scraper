import 'server-only';

import fs from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';

/**
 * Resolves the writable data directory.
 *
 * Serverless platforms (Netlify, Vercel) ship a read-only bundle, so the
 * project-local `./database` folder cannot be written to there. `LEADMINE_DATA_DIR`
 * overrides the location; otherwise we fall back to the OS temp directory when a
 * serverless environment is detected. Temp storage is ephemeral — the README
 * explains why a persistent volume is required for real deployments.
 */
function resolveDataDir(): string {
  const configured = process.env.LEADMINE_DATA_DIR?.trim();
  if (configured) {
    return path.isAbsolute(configured)
      ? configured
      : path.join(/* turbopackIgnore: true */ process.cwd(), configured);
  }

  const isServerless = Boolean(
    process.env.NETLIFY || process.env.VERCEL || process.env.AWS_LAMBDA_FUNCTION_NAME,
  );

  // The turbopackIgnore hints stop the bundler treating these runtime data
  // paths as module resolution, which otherwise traces the whole project
  // into the deployment bundle.
  return isServerless
    ? path.join(/* turbopackIgnore: true */ os.tmpdir(), 'leadmine-data')
    : path.join(/* turbopackIgnore: true */ process.cwd(), 'database');
}

export const DATA_DIR = resolveDataDir();
export const EXPORTS_DIR = path.join(DATA_DIR, 'exports');

export const PATHS = {
  dataDir: DATA_DIR,
  exportsDir: EXPORTS_DIR,
  businesses: path.join(DATA_DIR, 'Businesses.xlsx'),
  settings: path.join(DATA_DIR, 'settings.json'),
  history: path.join(DATA_DIR, 'search-history.json'),
  exports: path.join(DATA_DIR, 'exports.json'),
  logs: path.join(DATA_DIR, 'logs.jsonl'),
} as const;

let ensured: Promise<void> | null = null;

/** Creates the data directories once per process. Safe to call on every request. */
export function ensureDataDir(): Promise<void> {
  ensured ??= (async () => {
    await fs.mkdir(DATA_DIR, { recursive: true });
    await fs.mkdir(EXPORTS_DIR, { recursive: true });
  })().catch((error: unknown) => {
    // Reset so a transient failure (e.g. a race on first boot) can be retried.
    ensured = null;
    throw error;
  });

  return ensured;
}

/** True when the path stays inside the exports directory (path traversal guard). */
export function isInsideExports(candidate: string): boolean {
  const resolved = path.resolve(candidate);
  const root = path.resolve(EXPORTS_DIR);
  return resolved === root || resolved.startsWith(root + path.sep);
}
