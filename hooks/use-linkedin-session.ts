'use client';

import { useMutation, useQuery, useQueryClient, type UseMutationResult, type UseQueryResult } from '@tanstack/react-query';

import { apiFetch } from '@/lib/api-client';
import type { LinkedInLoginResult, LinkedInSessionStatus } from '@/types/linkedin-job';

const queryKey = ['linkedin-session'] as const;

/**
 * The caller's own LinkedIn session status. `data` is `null` when nothing has
 * been connected yet — a normal state for a new user, not an error.
 */
export function useLinkedInSessionStatus(poll = false): UseQueryResult<LinkedInSessionStatus | null> {
  return useQuery({
    queryKey,
    queryFn: () => apiFetch<LinkedInSessionStatus | null>('/api/linkedin/session'),
    refetchInterval: poll ? 3000 : false,
  });
}

/**
 * Starts a fresh LinkedIn login with the caller's own email/password — the
 * API itself drives a server-side Playwright browser through LinkedIn's login
 * page. A `success` result means the session landed; invalidate the status
 * query so the rest of the app notices immediately. A `verificationRequired`
 * result means LinkedIn raised a checkpoint — follow up with
 * {@link useLinkedInLoginVerify}.
 */
export function useLinkedInLogin(): UseMutationResult<LinkedInLoginResult, Error, { linkedInEmail: string; linkedInPassword: string }> {
  const client = useQueryClient();

  return useMutation({
    mutationFn: (body) => apiFetch<LinkedInLoginResult>('/api/linkedin/session/login', {
      method: 'POST',
      body: JSON.stringify(body),
    }),
    onSuccess: (result) => {
      if (result.status === 'success') void client.invalidateQueries({ queryKey });
    },
  });
}

/** Submits a verification code into a checkpoint {@link useLinkedInLogin} left pending. */
export function useLinkedInLoginVerify(): UseMutationResult<LinkedInLoginResult, Error, { code: string }> {
  const client = useQueryClient();

  return useMutation({
    mutationFn: (body) => apiFetch<LinkedInLoginResult>('/api/linkedin/session/login/verify', {
      method: 'POST',
      body: JSON.stringify(body),
    }),
    onSuccess: (result) => {
      if (result.status === 'success') void client.invalidateQueries({ queryKey });
    },
  });
}

/** Abandons the caller's own pending login — call when the connect dialog closes mid-checkpoint. */
export function useCancelLinkedInLogin(): UseMutationResult<void, Error, void> {
  return useMutation({
    mutationFn: () => apiFetch<void>('/api/linkedin/session/login/cancel', { method: 'POST' }),
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
