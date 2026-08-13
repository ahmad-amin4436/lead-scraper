import { fail, handle, ok, parseJson } from '@/lib/api/response';
import { toLinkedInLoginResult } from '@/lib/backend/linkedin-login-mapper';
import { backendRequest, problemMessage } from '@/lib/backend/server';
import type { BackendProblem } from '@/lib/backend/types';
import { linkedInLoginSchema } from '@/lib/validation/linkedin.schema';

export const dynamic = 'force-dynamic';

/**
 * Starts a fresh LinkedIn login: forwards the caller's own LinkedIn
 * email/password straight to the .NET API, which drives a server-side
 * Playwright browser through LinkedIn's own login page with them. This route
 * only forwards the request body and maps the response — the credentials are
 * never stored here.
 */
export function POST(request: Request): Promise<Response> {
  return handle(async () => {
    const input = await parseJson(request, linkedInLoginSchema);

    const { response } = await backendRequest('/api/linkedin/session/login', {
      method: 'POST',
      body: JSON.stringify(input),
    });

    if (!response.ok) {
      const problem = (await response.json().catch(() => null)) as BackendProblem | null;
      return fail(problemMessage(problem, 'Could not sign in to LinkedIn.'), 'backend_error', response.status);
    }

    const result = await response.json();
    return ok(toLinkedInLoginResult(result));
  });
}
