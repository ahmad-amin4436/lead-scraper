'use client';

import { useMutation, useQuery, useQueryClient, type UseMutationResult, type UseQueryResult } from '@tanstack/react-query';

import { apiFetch } from '@/lib/api-client';
import type { PeopleSearchJobSnapshot, StartPeopleSearchRequest } from '@/types/people-search-job';

const queryKeys = {
  activeJobs: ['people-search-jobs'] as const,
};

/**
 * Starts a company-scoped LinkedIn people search. Returns immediately with
 * the queued job's snapshot — the search itself runs in the scraper worker
 * and is tracked by polling
 * {@link import('./use-people-search-job-poll').usePeopleSearchJobPoll}, the
 * same fire-and-forget-then-poll shape `useStartSearch` uses for search runs.
 */
export function useStartPeopleSearch(): UseMutationResult<
  PeopleSearchJobSnapshot,
  Error,
  StartPeopleSearchRequest
> {
  const client = useQueryClient();

  return useMutation({
    mutationFn: (request) =>
      apiFetch<PeopleSearchJobSnapshot>('/api/linkedin/people-search-jobs', {
        method: 'POST',
        body: JSON.stringify(request),
      }),
    onSuccess: () => {
      void client.invalidateQueries({ queryKey: queryKeys.activeJobs });
    },
  });
}

export function useActivePeopleSearchJobs(): UseQueryResult<PeopleSearchJobSnapshot[]> {
  return useQuery({
    queryKey: queryKeys.activeJobs,
    queryFn: () => apiFetch<PeopleSearchJobSnapshot[]>('/api/linkedin/people-search-jobs'),
  });
}

export function usePeopleSearchJobCommand(): UseMutationResult<
  PeopleSearchJobSnapshot,
  Error,
  { jobId: string; command: 'stop' }
> {
  return useMutation({
    mutationFn: ({ jobId, command }) =>
      apiFetch<PeopleSearchJobSnapshot>(`/api/linkedin/people-search-jobs/${jobId}`, {
        method: 'POST',
        body: JSON.stringify({ command }),
      }),
  });
}
