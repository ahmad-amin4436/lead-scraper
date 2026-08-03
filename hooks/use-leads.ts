'use client';

import {
  useMutation,
  useQuery,
  useQueryClient,
  type QueryClient,
  type UseMutationResult,
} from '@tanstack/react-query';

import { apiFetch, buildQueryString } from '@/lib/api-client';
import type {
  BackendBusiness,
  BackendBusinessStats,
  BackendPagedResult,
} from '@/lib/backend/types';

export interface BackendBusinessQuery {
  search?: string;
  category?: string;
  country?: string;
  city?: string;
  source?: string;
  status?: string;
  emailStatus?: string;
  whatsappStatus?: string;
  /** Coarse lead-quality preset; mirrors `LeadKind` on the backend. */
  kind?: string;
  /** Admin-only; ignored for callers without `leads.view-all`. */
  ownerUserId?: string;
  hasEmail?: boolean;
  hasPhone?: boolean;
  hasWebsite?: boolean;
  minRating?: number;
  page?: number;
  pageSize?: number;
  sortBy?: string;
  sortDir?: string;
}

export const backendLeadsKeys = {
  list: (query: BackendBusinessQuery) => ['leads', 'list', query] as const,
  stats: ['leads', 'stats'] as const,
};

function leadsInvalidator(client: QueryClient) {
  void client.invalidateQueries({ queryKey: ['leads'] });
}

export function useBackendBusinesses(query: BackendBusinessQuery) {
  return useQuery({
    queryKey: backendLeadsKeys.list(query),
    queryFn: () =>
      apiFetch<BackendPagedResult<BackendBusiness>>(
        `/api/backend/businesses${buildQueryString(query as Record<string, unknown>)}`,
      ),
    placeholderData: (previous) => previous,
  });
}

export function useBackendBusinessStats() {
  return useQuery({
    queryKey: backendLeadsKeys.stats,
    queryFn: () => apiFetch<BackendBusinessStats>('/api/backend/businesses/stats'),
  });
}

export interface CreateBackendBusinessInput {
  name: string;
  category?: string;
  country?: string;
  state?: string;
  city?: string;
  address?: string;
  phone?: string;
  website?: string;
  email?: string;
  whatsApp?: string;
  facebook?: string;
  instagram?: string;
  linkedIn?: string;
  latitude?: number | null;
  longitude?: number | null;
  rating?: number | null;
  reviewCount?: number | null;
  mapsUrl?: string;
  notes?: string;
  source?: string;
}

export function useCreateBackendBusiness(): UseMutationResult<
  BackendBusiness,
  Error,
  CreateBackendBusinessInput
> {
  const client = useQueryClient();

  return useMutation({
    mutationFn: (input) =>
      apiFetch<BackendBusiness>('/api/backend/businesses', {
        method: 'POST',
        body: JSON.stringify(input),
      }),
    onSuccess: () => leadsInvalidator(client),
  });
}

export function useUpdateBackendBusiness(): UseMutationResult<
  BackendBusiness,
  Error,
  { id: string; patch: CreateBackendBusinessInput }
> {
  const client = useQueryClient();

  return useMutation({
    mutationFn: ({ id, patch }) =>
      apiFetch<BackendBusiness>(`/api/backend/businesses/${id}`, {
        method: 'PUT',
        body: JSON.stringify(patch),
      }),
    onSuccess: () => leadsInvalidator(client),
  });
}

export function useDeleteBackendBusiness(): UseMutationResult<void, Error, string> {
  const client = useQueryClient();

  return useMutation({
    mutationFn: (id) => apiFetch<void>(`/api/backend/businesses/${id}`, { method: 'DELETE' }),
    onSuccess: () => leadsInvalidator(client),
  });
}

export function useDeleteBackendBusinesses(): UseMutationResult<
  { removed: number },
  Error,
  string[]
> {
  const client = useQueryClient();

  return useMutation({
    mutationFn: (ids) =>
      apiFetch<{ removed: number }>('/api/backend/businesses/bulk-delete', {
        method: 'POST',
        body: JSON.stringify({ ids }),
      }),
    onSuccess: () => leadsInvalidator(client),
  });
}
