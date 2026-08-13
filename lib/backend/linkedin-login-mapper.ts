// Translation between the .NET LinkedInLoginResult DTO (enum values travel as
// their PascalCase C# names) and the camelCase status literals the front end speaks.

import type { LinkedInLoginResult, LinkedInLoginStatus } from '@/types/linkedin-job';

interface BackendLinkedInLoginResult {
  status: string;
  message: string;
}

const LOGIN_STATUS_MAP: Record<string, LinkedInLoginStatus> = {
  Success: 'success',
  VerificationRequired: 'verificationRequired',
  InvalidCredentials: 'invalidCredentials',
  ChallengeUnsupported: 'challengeUnsupported',
  Restricted: 'restricted',
  Failed: 'failed',
};

export function toLinkedInLoginResult(result: BackendLinkedInLoginResult): LinkedInLoginResult {
  return {
    status: LOGIN_STATUS_MAP[result.status] ?? 'failed',
    message: result.message,
  };
}
