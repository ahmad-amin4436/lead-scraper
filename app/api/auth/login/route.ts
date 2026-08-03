import { fail, ok } from '@/lib/api/response';
import type { BackendAuthResponse } from '@/lib/backend/types';
import { backendRequest, backendToApiResult, setSessionCookies } from '@/lib/backend/server';

export const dynamic = 'force-dynamic';

export async function POST(request: Request): Promise<Response> {
  let payload: unknown;
  try {
    payload = await request.json();
  } catch {
    return fail('Request body must be valid JSON.', 'invalid_json', 400);
  }

  const { email, password } = (payload ?? {}) as { email?: string; password?: string };
  if (
    typeof email !== 'string' ||
    email.trim() === '' ||
    typeof password !== 'string' ||
    password === ''
  ) {
    return fail('Email and password are required.', 'validation_error', 400);
  }

  const { response } = await backendRequest('/api/auth/login', {
    method: 'POST',
    skipAuth: true,
    body: JSON.stringify({ email: email.trim(), password }),
  });

  if (!response.ok) return backendToApiResult(response);

  let auth: BackendAuthResponse;
  try {
    auth = (await response.json()) as BackendAuthResponse;
  } catch {
    return fail('Invalid response from the backend.', 'bad_response', 502);
  }

  // Tokens live in httpOnly cookies; only the profile is sent to the browser.
  await setSessionCookies(auth);
  return ok(auth.user, { status: response.status });
}
