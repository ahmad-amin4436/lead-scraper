'use client';

import { useMutation, useQuery, useQueryClient, type UseMutationResult, type UseQueryResult } from '@tanstack/react-query';

import { apiFetch } from '@/lib/api-client';
import type { LinkedInJobSnapshot, StartLinkedInEnrichmentRequest } from '@/types/linkedin-job';

const queryKeys = {
  activeJobs: ['linkedin-enrichment-jobs'] as const,
};

/**
 * Starts a LinkedIn enrichment batch. Returns immediately with the queued
 * job's snapshot — the batch itself runs in the scraper worker and is tracked
 * by polling {@link import('./use-linkedin-job-poll').useLinkedInJobPoll}, the
 * same fire-and-forget-then-poll shape `useStartSearch` uses for search runs.
 */
export function useStartLinkedInEnrichment(): UseMutationResult<
  LinkedInJobSnapshot,
  Error,
  StartLinkedInEnrichmentRequest
> {
  const client = useQueryClient();

  return useMutation({
    mutationFn: (request) =>
      apiFetch<LinkedInJobSnapshot>('/api/linkedin/enrich-jobs', {
        method: 'POST',
        body: JSON.stringify(request),
      }),
    onSuccess: () => {
      void client.invalidateQueries({ queryKey: queryKeys.activeJobs });
    },
  });
}

export function useActiveLinkedInJobs(): UseQueryResult<LinkedInJobSnapshot[]> {
  return useQuery({
    queryKey: queryKeys.activeJobs,
    queryFn: () => apiFetch<LinkedInJobSnapshot[]>('/api/linkedin/enrich-jobs'),
  });
}

export function useLinkedInJobCommand(): UseMutationResult<
  LinkedInJobSnapshot,
  Error,
  { jobId: string; command: 'stop' }
> {
  return useMutation({
    mutationFn: ({ jobId, command }) =>
      apiFetch<LinkedInJobSnapshot>(`/api/linkedin/enrich-jobs/${jobId}`, {
        method: 'POST',
        body: JSON.stringify({ command }),
      }),
  });
}
