'use client';

import { useMutation, useQuery, useQueryClient, type UseMutationResult, type UseQueryResult } from '@tanstack/react-query';

import { apiFetch } from '@/lib/api-client';
import type { LinkedInConnectToken, LinkedInSessionStatus } from '@/types/linkedin-job';

const queryKey = ['linkedin-session'] as const;

/**
 * The caller's own LinkedIn session status. `data` is `null` when nothing has
 * been uploaded yet — a normal state for a new user, not an error.
 *
 * Pass `poll: true` while a connect dialog is open and waiting on
 * `backend/tools/LinkedInLogin` to push a session in — the query then
 * refetches every few seconds so the dialog notices and closes itself the
 * moment it lands, with no action required from the caller.
 */
export function useLinkedInSessionStatus(poll = false): UseQueryResult<LinkedInSessionStatus | null> {
  return useQuery({
    queryKey,
    queryFn: () => apiFetch<LinkedInSessionStatus | null>('/api/linkedin/session'),
    refetchInterval: poll ? 3000 : false,
  });
}

/**
 * Mints a connect code and hands back the exact command to run
 * `backend/tools/LinkedInLogin` with — the tool pushes the captured session
 * straight to the caller's account, so there is no file to download or
 * upload.
 */
export function useCreateLinkedInConnectToken(): UseMutationResult<LinkedInConnectToken, Error, void> {
  return useMutation({
    mutationFn: () => apiFetch<LinkedInConnectToken>('/api/linkedin/connect-token', { method: 'POST' }),
  });
}

/**
 * Uploads (or replaces) the caller's own storageState.json — the file
 * produced by running `backend/tools/LinkedInLogin` on their own machine,
 * logged into their own LinkedIn account.
 */
export function useUploadLinkedInSession(): UseMutationResult<void, Error, File> {
  const client = useQueryClient();

  return useMutation({
    mutationFn: async (file) => {
      const formData = new FormData();
      formData.append('file', file);
      await apiFetch<void>('/api/linkedin/session', { method: 'POST', body: formData });
    },
    onSuccess: () => {
      void client.invalidateQueries({ queryKey });
    },
  });
}

export function useRemoveLinkedInSession(): UseMutationResult<void, Error, void> {
  const client = useQueryClient();

  return useMutation({
    mutationFn: () => apiFetch<void>('/api/linkedin/session', { method: 'DELETE' }),
    onSuccess: () => {
      void client.invalidateQueries({ queryKey });
    },
  });
}

/** True while an active restriction cooldown is in effect. */
export function isLinkedInSessionRestricted(status: LinkedInSessionStatus | null | undefined): boolean {
  return status?.restrictedUntil != null && new Date(status.restrictedUntil).getTime() > Date.now();
}
