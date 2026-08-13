import { backendRequest, backendToApiResult } from '@/lib/backend/server';
import { ok } from '@/lib/api/response';

export const dynamic = 'force-dynamic';

/**
 * The caller's own LinkedIn session — one per app user, created by signing
 * in with their own LinkedIn credentials (see `session/login`). See
 * `LinkedInSessionManager`'s remarks for why each user brings their own
 * LinkedIn account rather than the app sharing one dedicated one.
 */

/** Status only — upload date, searches used today, restriction state. Never the session content itself. */
export async function GET(): Promise<Response> {
  const { response } = await backendRequest('/api/linkedin/session');

  // No session yet is an expected, common state here — not a fetch failure —
  // so the caller gets `null` back rather than an error to show the user.
  if (response.status === 404) return ok(null);

  return backendToApiResult(response);
}

/** Removes the caller's own session. */
export async function DELETE(): Promise<Response> {
  const { response } = await backendRequest('/api/linkedin/session', { method: 'DELETE' });
  return backendToApiResult(response);
}
