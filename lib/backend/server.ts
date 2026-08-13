// Server-side bridge to the .NET 8 API.
//
// Only Route Handlers import this module. It attaches the caller's bearer token
// (read from an httpOnly cookie), rotates the access token once when it expires,
// and normalizes every response into the `{ ok, data }` envelope the rest of the
// front end already speaks.

import { cookies } from 'next/headers';
import { Agent } from 'undici';

import { ok, fail } from '@/lib/api/response';
import { backendBaseUrl } from '@/lib/backend/config';
import type { BackendAuthResponse, BackendProblem, BackendUser } from '@/lib/backend/types';

/**
 * Skips TLS certificate validation for calls to the backend — nothing else.
 * <p>
 * Node's `fetch` has no "proceed anyway" the way a browser does: an untrusted
 * certificate (e.g. a hosting panel's self-signed default before a real one is
 * issued) fails the request outright. Set LEADMINE_API_INSECURE_SSL=true as a
 * temporary workaround while that certificate is unresolved, and unset it the
 * moment a trusted one is installed — this intentionally does not touch
 * NODE_TLS_REJECT_UNAUTHORIZED, which would silently disable verification for
 * every outbound HTTPS call this process makes, not just the backend.
 */
export const insecureDispatcher =
  process.env.LEADMINE_API_INSECURE_SSL === 'true'
    ? new Agent({ connect: { rejectUnauthorized: false } })
    : undefined;

/** `fetch`'s standard types don't know about undici's `dispatcher` option. */
export type FetchInit = RequestInit & { dispatcher?: Agent };

export const ACCESS_COOKIE = 'leadmine_access';
export const REFRESH_COOKIE = 'leadmine_refresh';

/** Mirrors the backend's Jwt:RefreshTokenDays setting. */
const REFRESH_TOKEN_TTL_SECONDS = 7 * 24 * 60 * 60;

function cookieOptions() {
  return {
    httpOnly: true,
    sameSite: 'lax' as const,
    secure: process.env.NODE_ENV === 'production',
    path: '/',
  };
}

export async function setSessionCookies(auth: BackendAuthResponse): Promise<void> {
  const store = await cookies();
  const accessTtl = Math.max(
    60,
    Math.round((new Date(auth.expiresAt).getTime() - Date.now()) / 1000),
  );

  store.set(ACCESS_COOKIE, auth.accessToken, { ...cookieOptions(), maxAge: accessTtl });
  store.set(REFRESH_COOKIE, auth.refreshToken, {
    ...cookieOptions(),
    maxAge: REFRESH_TOKEN_TTL_SECONDS,
  });
}

export async function clearSessionCookies(): Promise<void> {
  const store = await cookies();
  store.set(ACCESS_COOKIE, '', { ...cookieOptions(), maxAge: 0 });
  store.set(REFRESH_COOKIE, '', { ...cookieOptions(), maxAge: 0 });
}

export function problemMessage(problem: BackendProblem | null, fallback: string): string {
  if (!problem) return fallback;

  if (problem.errors) {
    for (const messages of Object.values(problem.errors)) {
      if (messages.length > 0) return messages[0];
    }
  }

  return problem.detail ?? problem.title ?? fallback;
}

export interface BackendRequestInit {
  method?: string;
  query?: Record<string, string | number | boolean | null | undefined>;
  headers?: HeadersInit;
  body?: BodyInit | null;
  /** Leave the bearer token off, for the anonymous endpoints. */
  skipAuth?: boolean;
}

/**
 * Calls the .NET API. Reads the access cookie and attaches it as a bearer
 * token; on a 401 (expired or missing access token) it rotates the refresh
 * token once and retries. Cookies are updated in place, so callers don't need
 * to know a rotation happened.
 */
export async function backendRequest(
  path: string,
  init: BackendRequestInit = {},
): Promise<{ response: Response }> {
  const url = new URL(`${backendBaseUrl()}${path}`);
  for (const [key, value] of Object.entries(init.query ?? {})) {
    if (value === undefined || value === null || value === '') continue;
    url.searchParams.set(key, String(value));
  }

  const store = await cookies();
  const accessToken = store.get(ACCESS_COOKIE)?.value;
  const refreshToken = store.get(REFRESH_COOKIE)?.value;

  const perform = (token?: string) =>
    fetch(url, {
      method: init.method ?? 'GET',
      headers: {
        // FormData (proxying a file upload through to the .NET API) needs
        // undici to set its own Content-Type with a multipart boundary —
        // forcing JSON here would send the file as an unparseable body.
        ...(init.body && !(init.body instanceof FormData) ? { 'Content-Type': 'application/json' } : {}),
        ...(token ? { Authorization: `Bearer ${token}` } : {}),
        ...(init.headers as HeadersInit),
      },
      body: init.body ?? null,
      cache: 'no-store',
      ...(insecureDispatcher ? { dispatcher: insecureDispatcher } : {}),
    } satisfies FetchInit);

  const first = await perform(init.skipAuth ? undefined : accessToken);

  // Recoverable if the refresh token is still valid, even when the access token
  // was missing entirely (e.g. the server restarted between requests).
  if (!init.skipAuth && first.status === 401 && refreshToken) {
    const rotated = await rotateSession(refreshToken);
    if (rotated) {
      await setSessionCookies(rotated);
      return { response: await perform(rotated.accessToken) };
    }
    await clearSessionCookies();
  }

  return { response: first };
}

async function rotateSession(refreshToken: string): Promise<BackendAuthResponse | null> {
  try {
    const response = await fetch(`${backendBaseUrl()}/api/auth/refresh`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ refreshToken }),
      cache: 'no-store',
      ...(insecureDispatcher ? { dispatcher: insecureDispatcher } : {}),
    } satisfies FetchInit);
    if (!response.ok) return null;

    const body = (await response.json()) as BackendAuthResponse;
    if (!body.accessToken || !body.refreshToken) return null;
    return body;
  } catch {
    return null;
  }
}

function statusCode(code: number): string {
  const reasons: Record<number, string> = {
    400: 'bad_request',
    401: 'unauthorized',
    403: 'forbidden',
    404: 'not_found',
    409: 'conflict',
    422: 'validation_error',
    500: 'internal_error',
    502: 'bad_gateway',
    503: 'unavailable',
    504: 'gateway_timeout',
  };
  return reasons[code] ?? 'backend_error';
}

/**
 * Turns a raw .NET API response into the `{ ok, data }` envelope every route
 * handler returns, preserving the status code and mapping problem details onto
 * a readable error message.
 */
export async function backendToApiResult(response: Response): Promise<Response> {
  const contentType = response.headers.get('content-type') ?? '';
  const isJson = contentType.toLowerCase().includes('application/json');

  if (response.ok) {
    // A 204 has no body by spec — Response.json() would throw if we tried to
    // keep the 204 status here. The client always parses a JSON envelope
    // regardless of status, so report success as 200 with a null payload.
    if (response.status === 204) return ok(null);
    if (!isJson) return ok(await response.text(), { status: response.status });

    try {
      return ok(await response.json(), { status: response.status });
    } catch {
      return fail('Invalid response from the backend.', 'bad_response', 502);
    }
  }

  let problem: BackendProblem | null = null;
  if (isJson) {
    try {
      problem = (await response.json()) as BackendProblem;
    } catch {
      problem = null;
    }
  }

  const status = problem?.status ?? response.status;

  if (status === 401) {
    return fail(
      problem?.detail ?? 'Your session has expired. Please sign in again.',
      'unauthorized',
      401,
    );
  }

  return fail(
    problemMessage(problem, `Backend request failed (HTTP ${response.status}).`),
    statusCode(status),
    status,
    problem?.errors,
  );
}

/**
 * Resolves the signed-in user from the session cookie.
 *
 * Used where a route needs the caller's identity rather than just proxying a
 * request — starting a search records the owner so the background worker, which
 * has no session of its own, can still attribute scraped leads correctly.
 * Returns null when the session is missing or expired.
 */
export async function currentBackendUser(): Promise<BackendUser | null> {
  try {
    const { response } = await backendRequest('/api/auth/me');
    if (!response.ok) return null;

    return (await response.json()) as BackendUser;
  } catch {
    return null;
  }
}
