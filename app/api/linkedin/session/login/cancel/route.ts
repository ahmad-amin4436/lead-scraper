import { backendRequest, backendToApiResult } from '@/lib/backend/server';

export const dynamic = 'force-dynamic';

/** Abandons the caller's own pending LinkedIn login — e.g. the connect dialog was closed mid-checkpoint. */
export async function POST(): Promise<Response> {
  const { response } = await backendRequest('/api/linkedin/session/login/cancel', { method: 'POST' });
  return backendToApiResult(response);
}
