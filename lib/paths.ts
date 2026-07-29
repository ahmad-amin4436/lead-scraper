import '@/lib/server-guard';

/**
 * Storage keys for every persisted artifact.
 *
 * These were once filesystem paths; they are now keys into {@link blobStore},
 * which is backed by Netlify Blobs in production and the local filesystem in
 * development. Keeping them centralised means the whole app addresses storage
 * through one vocabulary regardless of the backend.
 *
 * Keys must not start with `/` and must not contain `:` (Netlify Blobs rules).
 */
export const KEYS = {
  businesses: 'businesses.json',
  settings: 'settings.json',
  history: 'search-history.json',
  exports: 'exports.json',
  logs: 'logs.jsonl',
  /** Prefix under which generated export files live. */
  exportsPrefix: 'exports/',
  /** Prefix under which per-job state blobs live. */
  jobsPrefix: 'jobs/',
} as const;

/** Key for a generated export file, namespaced under the exports prefix. */
export function exportFileKey(fileName: string): string {
  // Guard against a key escaping the prefix via path separators.
  const safe = fileName.replace(/[/\\]/g, '_');
  return `${KEYS.exportsPrefix}${safe}`;
}

/** Key for a single job's state blob. */
export function jobKey(jobId: string): string {
  const safe = jobId.replace(/[/\\:]/g, '_');
  return `${KEYS.jobsPrefix}${safe}`;
}
