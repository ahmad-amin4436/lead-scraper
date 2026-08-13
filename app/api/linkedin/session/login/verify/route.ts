import { fail, handle, ok, parseJson } from '@/lib/api/response';
import { toLinkedInLoginResult } from '@/lib/backend/linkedin-login-mapper';
import { backendRequest, problemMessage } from '@/lib/backend/server';
import type { BackendProblem } from '@/lib/backend/types';
import { linkedInLoginVerifySchema } from '@/lib/validation/linkedin.schema';

export const dynamic = 'force-dynamic';

/** Submits a verification code into a checkpoint a prior `session/login` call left pending for the caller. */
export function POST(request: Request): Promise<Response> {
  return handle(async () => {
    const input = await parseJson(request, linkedInLoginVerifySchema);

    const { response } = await backendRequest('/api/linkedin/session/login/verify', {
      method: 'POST',
      body: JSON.stringify(input),
    });

    if (!response.ok) {
      const problem = (await response.json().catch(() => null)) as BackendProblem | null;
      return fail(problemMessage(problem, 'Could not verify the LinkedIn login.'), 'backend_error', response.status);
    }

    const result = await response.json();
    return ok(toLinkedInLoginResult(result));
  });
}
