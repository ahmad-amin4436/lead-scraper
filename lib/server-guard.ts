/**
 * Marks a module as server-only.
 *
 * This replaces the `server-only` package, which cannot be used here. That
 * package resolves through the `react-server` export condition: Next.js sets it
 * when bundling server code and gets a harmless empty module, but any other
 * bundler gets the variant that throws on import *by design*.
 *
 * The Netlify Background Function that runs searches is bundled by plain
 * esbuild, which does not set that condition — so importing the app's own
 * services made the worker crash the instant it started. It still returned the
 * background function's immediate 202, so searches looked accepted but silently
 * never ran, leaving every job stuck in `queued`.
 *
 * Importing this module instead keeps the intent (loudly reject a module that
 * ends up in browser code) without depending on bundler-specific resolution.
 * The check is at runtime rather than build time; in practice these modules also
 * import `node:` builtins, which fail a client build on their own.
 */
if (typeof window !== 'undefined') {
  throw new Error(
    'A server-only module was imported from browser code. Move this import into a Server Component, Route Handler, or Server Action.',
  );
}

export {};
