import { cookies } from 'next/headers';

import { ok } from '@/lib/api/response';
import { REFRESH_COOKIE, backendRequest, clearSessionCookies } from '@/lib/backend/server';

export const dynamic = 'force-dynamic';

export async function POST(): Promise<Response> {
  const store = await cookies();
  const refreshToken = store.get(REFRESH_COOKIE)?.value;

  // Best-effort server-side revoke; the local cookies are cleared regardless so
  // sign-out is idempotent even if the token already expired.
  if (refreshToken) {
    await backendRequest('/api/auth/logout', {
      method: 'POST',
      skipAuth: true,
      body: JSON.stringify({ refreshToken }),
    });
  }

  await clearSessionCookies();
  return ok(null);
}
