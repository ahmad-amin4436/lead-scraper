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

  const { email, password, firstName, lastName } = (payload ?? {}) as {
    email?: string;
    password?: string;
    firstName?: string;
    lastName?: string;
  };

  if (
    typeof email !== 'string' ||
    email.trim() === '' ||
    typeof password !== 'string' ||
    password.length < 8 ||
    typeof firstName !== 'string' ||
    firstName.trim() === '' ||
    typeof lastName !== 'string' ||
    lastName.trim() === ''
  ) {
    return fail(
      'Email, a password of at least 8 characters, and first and last name are required.',
      'validation_error',
      400,
    );
  }

  const { response } = await backendRequest('/api/auth/register', {
    method: 'POST',
    skipAuth: true,
    body: JSON.stringify({
      email: email.trim(),
      password,
      firstName: firstName.trim(),
      lastName: lastName.trim(),
    }),
  });

  if (!response.ok) return backendToApiResult(response);

  let auth: BackendAuthResponse;
  try {
    auth = (await response.json()) as BackendAuthResponse;
  } catch {
    return fail('Invalid response from the backend.', 'bad_response', 502);
  }

  await setSessionCookies(auth);
  return ok(auth.user, { status: response.status });
}
