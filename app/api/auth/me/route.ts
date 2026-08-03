import { backendRequest, backendToApiResult } from '@/lib/backend/server';

export const dynamic = 'force-dynamic';

export async function GET(): Promise<Response> {
  const { response } = await backendRequest('/api/auth/me');
  return backendToApiResult(response);
}
