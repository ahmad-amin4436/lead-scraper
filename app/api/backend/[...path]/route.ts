import type { NextRequest } from 'next/server';

import { backendRequest, backendToApiResult } from '@/lib/backend/server';

export const dynamic = 'force-dynamic';

type BackendParams = { path: string[] };

/**
 * Generic gateway to the .NET API: `/api/backend/businesses?page=1` proxies to
 * the backend's `/api/businesses`. Bearer-token attach, refresh rotation and
 * error mapping all happen inside `backendRequest`/`backendToApiResult`, so the
 * client code below stays one line.
 */
async function proxy(request: NextRequest, context: { params: Promise<BackendParams> }) {
  const { path } = await context.params;
  const target = `/api/${path.join('/')}`;

  const hasBody = request.method !== 'GET' && request.method !== 'HEAD';

  const { response } = await backendRequest(target, {
    method: request.method,
    query: Object.fromEntries(request.nextUrl.searchParams.entries()),
    body: hasBody ? await request.text() : null,
  });

  return backendToApiResult(response);
}

export { proxy as GET, proxy as POST, proxy as PUT, proxy as PATCH, proxy as DELETE };
