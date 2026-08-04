'use client';

import {
  useMutation,
  useQuery,
  useQueryClient,
  type QueryClient,
  type UseMutationResult,
} from '@tanstack/react-query';

import { apiFetch, buildQueryString } from '@/lib/api-client';
import type { BackendPagedResult, BackendPerson } from '@/lib/backend/types';

export interface PersonQuery {
  search?: string;
  businessId?: string;
  isDecisionMaker?: boolean;
  ownerUserId?: string;
  page?: number;
  pageSize?: number;
}

export const peopleKeys = {
  list: (query: PersonQuery) => ['people', 'list', query] as const,
};

function peopleInvalidator(client: QueryClient) {
  void client.invalidateQueries({ queryKey: ['people'] });
}

export function usePeople(query: PersonQuery) {
  return useQuery({
    queryKey: peopleKeys.list(query),
    queryFn: () =>
      apiFetch<BackendPagedResult<BackendPerson>>(
        `/api/backend/people${buildQueryString(query as Record<string, unknown>)}`,
      ),
    placeholderData: (previous) => previous,
  });
}

export interface UpdatePersonInput {
  fullName?: string;
  jobTitle?: string;
  email?: string;
  phone?: string;
  isDecisionMaker?: boolean;
}

export function useUpdatePerson(): UseMutationResult<
  BackendPerson,
  Error,
  { id: string; patch: UpdatePersonInput }
> {
  const client = useQueryClient();

  return useMutation({
    mutationFn: ({ id, patch }) =>
      apiFetch<BackendPerson>(`/api/backend/people/${id}`, {
        method: 'PUT',
        body: JSON.stringify(patch),
      }),
    onSuccess: () => peopleInvalidator(client),
  });
}

export function useDeletePerson(): UseMutationResult<void, Error, string> {
  const client = useQueryClient();

  return useMutation({
    mutationFn: (id) => apiFetch<void>(`/api/backend/people/${id}`, { method: 'DELETE' }),
    onSuccess: () => peopleInvalidator(client),
  });
}
