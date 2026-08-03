// Base URL of the .NET 8 API the server-side proxy talks to.
//
// Server-only: it reads `process.env` at request time, so importing it from a
// client component would leak the value to the browser at build time.

const DEFAULT_DEV_URL = 'http://localhost:5016';
const DEFAULT_LIVE_URL = 'https://devnet.khaneazam.com';

export function backendBaseUrl(): string {
  const fromEnv = process.env.LEADMINE_API_URL?.trim();
  if (fromEnv) return fromEnv.replace(/\/+$/, '');
  return process.env.NODE_ENV === 'production' ? DEFAULT_LIVE_URL : DEFAULT_DEV_URL;
}
