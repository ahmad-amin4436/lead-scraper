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
  BackendPagedResult,
  BackendPermission,
  BackendRole,
  BackendUser,
} from '@/lib/backend/types';

export interface UserListQuery {
  page?: number;
  pageSize?: number;
  search?: string;
}

export const adminKeys = {
  users: (query: UserListQuery) => ['admin', 'users', query] as const,
  roles: ['admin', 'roles'] as const,
  permissions: ['admin', 'permissions'] as const,
};

function usersInvalidator(client: QueryClient) {
  void client.invalidateQueries({ queryKey: ['admin', 'users'] });
}

// --- users -----------------------------------------------------------------

export function useUsers(query: UserListQuery) {
  return useQuery({
    queryKey: adminKeys.users(query),
    queryFn: () =>
      apiFetch<BackendPagedResult<BackendUser>>(
        `/api/backend/users${buildQueryString(query as Record<string, unknown>)}`,
      ),
    placeholderData: (previous) => previous,
  });
}

export interface CreateUserInput {
  email: string;
  password: string;
  firstName: string;
  lastName: string;
  isActive: boolean;
  roles: string[];
}

export function useCreateUser(): UseMutationResult<BackendUser, Error, CreateUserInput> {
  const client = useQueryClient();

  return useMutation({
    mutationFn: (input) =>
      apiFetch<BackendUser>('/api/backend/users', { method: 'POST', body: JSON.stringify(input) }),
    onSuccess: () => usersInvalidator(client),
  });
}

export function useUpdateUser(): UseMutationResult<
  BackendUser,
  Error,
  { id: string; patch: Record<string, unknown> }
> {
  const client = useQueryClient();

  return useMutation({
    mutationFn: ({ id, patch }) =>
      apiFetch<BackendUser>(`/api/backend/users/${id}`, {
        method: 'PUT',
        body: JSON.stringify(patch),
      }),
    onSuccess: () => usersInvalidator(client),
  });
}

export function useDeleteUser(): UseMutationResult<void, Error, string> {
  const client = useQueryClient();

  return useMutation({
    mutationFn: (id) => apiFetch<void>(`/api/backend/users/${id}`, { method: 'DELETE' }),
    onSuccess: () => usersInvalidator(client),
  });
}

export function useAssignUserRoles(): UseMutationResult<
  BackendUser,
  Error,
  { id: string; roles: string[] }
> {
  const client = useQueryClient();

  return useMutation({
    mutationFn: ({ id, roles }) =>
      apiFetch<BackendUser>(`/api/backend/users/${id}/roles`, {
        method: 'PUT',
        body: JSON.stringify({ roles }),
      }),
    onSuccess: () => usersInvalidator(client),
  });
}

export function useResetUserPassword(): UseMutationResult<
  void,
  Error,
  { id: string; newPassword: string }
> {
  return useMutation({
    mutationFn: ({ id, newPassword }) =>
      apiFetch<void>(`/api/backend/users/${id}/reset-password`, {
        method: 'POST',
        body: JSON.stringify({ userId: id, newPassword }),
      }),
  });
}

// --- roles & permissions -----------------------------------------------------

export function useRoles() {
  return useQuery({
    queryKey: adminKeys.roles,
    queryFn: () => apiFetch<BackendRole[]>('/api/backend/roles'),
  });
}

export function usePermissions() {
  return useQuery({
    queryKey: adminKeys.permissions,
    queryFn: () => apiFetch<BackendPermission[]>('/api/backend/permissions'),
  });
}

export interface CreateRoleInput {
  name: string;
  description: string;
  permissions: string[];
}

export function useCreateRole(): UseMutationResult<BackendRole, Error, CreateRoleInput> {
  const client = useQueryClient();

  return useMutation({
    mutationFn: (input) =>
      apiFetch<BackendRole>('/api/backend/roles', { method: 'POST', body: JSON.stringify(input) }),
    onSuccess: () => {
      void client.invalidateQueries({ queryKey: ['admin', 'roles'] });
      void client.invalidateQueries({ queryKey: ['admin', 'users'] });
    },
  });
}

export function useUpdateRole(): UseMutationResult<
  BackendRole,
  Error,
  { id: string; name?: string; description?: string }
> {
  const client = useQueryClient();

  return useMutation({
    mutationFn: ({ id, ...patch }) =>
      apiFetch<BackendRole>(`/api/backend/roles/${id}`, {
        method: 'PUT',
        body: JSON.stringify(patch),
      }),
    onSuccess: () => {
      void client.invalidateQueries({ queryKey: ['admin', 'roles'] });
    },
  });
}

export function useDeleteRole(): UseMutationResult<void, Error, string> {
  const client = useQueryClient();

  return useMutation({
    mutationFn: (id) => apiFetch<void>(`/api/backend/roles/${id}`, { method: 'DELETE' }),
    onSuccess: () => {
      void client.invalidateQueries({ queryKey: ['admin', 'roles'] });
      void client.invalidateQueries({ queryKey: ['admin', 'users'] });
    },
  });
}

export function useSetRolePermissions(): UseMutationResult<
  BackendRole,
  Error,
  { id: string; permissions: string[] }
> {
  const client = useQueryClient();

  return useMutation({
    mutationFn: ({ id, permissions }) =>
      apiFetch<BackendRole>(`/api/backend/roles/${id}/permissions`, {
        method: 'PUT',
        body: JSON.stringify({ permissions }),
      }),
    onSuccess: () => {
      void client.invalidateQueries({ queryKey: ['admin', 'roles'] });
    },
  });
}
