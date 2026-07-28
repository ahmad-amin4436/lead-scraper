'use client';

import {
  useMutation,
  useQuery,
  useQueryClient,
  type UseMutationResult,
  type UseQueryResult,
} from '@tanstack/react-query';

import { apiFetch, buildQueryString } from '@/lib/api-client';
import type { BusinessPage, BusinessQuery, BusinessStats } from '@/types/business';
import type { ExportRecord, ExportRequest } from '@/types/export';
import type { JobSnapshot } from '@/types/job';
import type { LogPage, LogQuery } from '@/types/log';
import type { SearchHistoryEntry, SearchRequest } from '@/types/search';
import type { PublicAppSettings } from '@/types/settings';
import type { SettingsInput } from '@/lib/validation/settings.schema';

export const queryKeys = {
  businesses: (query: BusinessQuery) => ['businesses', query] as const,
  stats: ['businesses', 'stats'] as const,
  history: ['history'] as const,
  exports: ['exports'] as const,
  logs: (query: LogQuery) => ['logs', query] as const,
  settings: ['settings'] as const,
  jobs: ['jobs'] as const,
};

export interface ProviderStatus {
  id: string;
  label: string;
  ready: boolean;
  reason: string | null;
}

export interface SettingsResponse {
  settings: PublicAppSettings;
  providers: ProviderStatus[];
}

// --- businesses ------------------------------------------------------------

export function useBusinesses(query: BusinessQuery): UseQueryResult<BusinessPage> {
  return useQuery({
    queryKey: queryKeys.businesses(query),
    queryFn: () =>
      apiFetch<BusinessPage>(`/api/businesses${buildQueryString(query as Record<string, unknown>)}`),
    // Keeps the previous page visible while the next one loads.
    placeholderData: (previous) => previous,
  });
}

export function useBusinessStats(): UseQueryResult<BusinessStats> {
  return useQuery({
    queryKey: queryKeys.stats,
    queryFn: () => apiFetch<BusinessStats>('/api/businesses/stats'),
  });
}

export function useDeleteBusinesses(): UseMutationResult<{ removed: number }, Error, string[]> {
  const client = useQueryClient();

  return useMutation({
    mutationFn: (ids: string[]) =>
      apiFetch<{ removed: number }>('/api/businesses', {
        method: 'DELETE',
        body: JSON.stringify({ ids }),
      }),
    onSuccess: () => {
      void client.invalidateQueries({ queryKey: ['businesses'] });
    },
  });
}

export function useUpdateBusiness(): UseMutationResult<
  unknown,
  Error,
  { id: string; patch: Record<string, unknown> }
> {
  const client = useQueryClient();

  return useMutation({
    mutationFn: ({ id, patch }) =>
      apiFetch(`/api/businesses/${id}`, { method: 'PATCH', body: JSON.stringify(patch) }),
    onSuccess: () => {
      void client.invalidateQueries({ queryKey: ['businesses'] });
    },
  });
}

// --- search ----------------------------------------------------------------

export function useStartSearch(): UseMutationResult<JobSnapshot, Error, SearchRequest> {
  const client = useQueryClient();

  return useMutation({
    mutationFn: (request: SearchRequest) =>
      apiFetch<JobSnapshot>('/api/search', { method: 'POST', body: JSON.stringify(request) }),
    onSuccess: () => {
      void client.invalidateQueries({ queryKey: queryKeys.jobs });
    },
  });
}

export function useJobCommand(): UseMutationResult<
  JobSnapshot,
  Error,
  { jobId: string; command: 'stop' }
> {
  return useMutation({
    mutationFn: ({ jobId, command }) =>
      apiFetch<JobSnapshot>(`/api/search/${jobId}`, {
        method: 'POST',
        body: JSON.stringify({ command }),
      }),
  });
}

export function useActiveJobs(): UseQueryResult<JobSnapshot[]> {
  return useQuery({
    queryKey: queryKeys.jobs,
    queryFn: () => apiFetch<JobSnapshot[]>('/api/search'),
  });
}

// --- history ---------------------------------------------------------------

export function useHistory(limit?: number): UseQueryResult<SearchHistoryEntry[]> {
  return useQuery({
    queryKey: [...queryKeys.history, limit ?? 'all'],
    queryFn: () =>
      apiFetch<SearchHistoryEntry[]>(`/api/history${buildQueryString({ limit })}`),
  });
}

export function useDeleteHistoryEntry(): UseMutationResult<unknown, Error, string> {
  const client = useQueryClient();

  return useMutation({
    mutationFn: (id: string) => apiFetch(`/api/history/${id}`, { method: 'DELETE' }),
    onSuccess: () => {
      void client.invalidateQueries({ queryKey: queryKeys.history });
    },
  });
}

export function useClearHistory(): UseMutationResult<unknown, Error, void> {
  const client = useQueryClient();

  return useMutation({
    mutationFn: () => apiFetch('/api/history', { method: 'DELETE' }),
    onSuccess: () => {
      void client.invalidateQueries({ queryKey: queryKeys.history });
    },
  });
}

// --- exports ---------------------------------------------------------------

export function useExports(): UseQueryResult<ExportRecord[]> {
  return useQuery({
    queryKey: queryKeys.exports,
    queryFn: () => apiFetch<ExportRecord[]>('/api/exports'),
  });
}

export function useCreateExport(): UseMutationResult<ExportRecord, Error, ExportRequest> {
  const client = useQueryClient();

  return useMutation({
    mutationFn: (request: ExportRequest) =>
      apiFetch<ExportRecord>('/api/exports', { method: 'POST', body: JSON.stringify(request) }),
    onSuccess: () => {
      void client.invalidateQueries({ queryKey: queryKeys.exports });
    },
  });
}

export function useDeleteExport(): UseMutationResult<unknown, Error, string> {
  const client = useQueryClient();

  return useMutation({
    mutationFn: (id: string) => apiFetch(`/api/exports/${id}`, { method: 'DELETE' }),
    onSuccess: () => {
      void client.invalidateQueries({ queryKey: queryKeys.exports });
    },
  });
}

// --- logs ------------------------------------------------------------------

export function useLogs(query: LogQuery, refetchMs?: number): UseQueryResult<LogPage> {
  return useQuery({
    queryKey: queryKeys.logs(query),
    queryFn: () => apiFetch<LogPage>(`/api/logs${buildQueryString(query as Record<string, unknown>)}`),
    refetchInterval: refetchMs,
    placeholderData: (previous) => previous,
  });
}

export function useClearLogs(): UseMutationResult<unknown, Error, void> {
  const client = useQueryClient();

  return useMutation({
    mutationFn: () => apiFetch('/api/logs', { method: 'DELETE' }),
    onSuccess: () => {
      void client.invalidateQueries({ queryKey: ['logs'] });
    },
  });
}

// --- settings --------------------------------------------------------------

export function useSettings(): UseQueryResult<SettingsResponse> {
  return useQuery({
    queryKey: queryKeys.settings,
    queryFn: () => apiFetch<SettingsResponse>('/api/settings'),
  });
}

export function useUpdateSettings(): UseMutationResult<SettingsResponse, Error, SettingsInput> {
  const client = useQueryClient();

  return useMutation({
    mutationFn: (input: SettingsInput) =>
      apiFetch<SettingsResponse>('/api/settings', { method: 'PUT', body: JSON.stringify(input) }),
    onSuccess: (data) => {
      client.setQueryData(queryKeys.settings, data);
    },
  });
}
