import { backendRequest } from '@/lib/backend/server';
import { backendBaseUrl } from '@/lib/backend/config';
import { fail, ok } from '@/lib/api/response';

export const dynamic = 'force-dynamic';

/**
 * Mints a short-lived code the "Connect your LinkedIn account" dialog turns
 * into a copyable `backend/tools/LinkedInLogin` command. Adds the backend's
 * own public base URL to the response since the tool runs on the user's
 * machine and talks to the .NET API directly, never through this proxy.
 */
export async function POST(): Promise<Response> {
  const { response } = await backendRequest('/api/linkedin/session/connect-token', { method: 'POST' });

  if (!response.ok) {
    const problem = await response.json().catch(() => null);
    return fail(problem?.detail ?? problem?.title ?? 'Could not create a connect code.', 'backend_error', response.status);
  }

  const data = (await response.json()) as { token: string; expiresAt: string };
  return ok({ ...data, apiBaseUrl: backendBaseUrl() });
}
