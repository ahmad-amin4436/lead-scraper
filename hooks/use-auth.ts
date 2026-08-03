'use client';

import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useRouter } from 'next/navigation';

import { apiFetch } from '@/lib/api-client';
import type { BackendUser } from '@/lib/backend/types';

export const authKeys = {
  session: ['auth', 'session'] as const,
};

export interface LoginInput {
  email: string;
  password: string;
}

export interface RegisterInput extends LoginInput {
  firstName: string;
  lastName: string;
}

export function useSession() {
  return useQuery({
    queryKey: authKeys.session,
    queryFn: () => apiFetch<BackendUser>('/api/auth/me'),
    // 401 means "signed out", not a server fault.
    retry: false,
    staleTime: 30_000,
  });
}

// Redirects are the caller's job: login/register pages honour a `next` query
// param, so routing lives in the form, not here.

export function useLogin() {
  const client = useQueryClient();

  return useMutation({
    mutationFn: (input: LoginInput) =>
      apiFetch<BackendUser>('/api/auth/login', {
        method: 'POST',
        body: JSON.stringify({ email: input.email, password: input.password }),
      }),
    onSuccess: (user) => {
      client.setQueryData(authKeys.session, user);
    },
  });
}

export function useRegister() {
  const client = useQueryClient();

  return useMutation({
    mutationFn: (input: RegisterInput) =>
      apiFetch<BackendUser>('/api/auth/register', {
        method: 'POST',
        body: JSON.stringify(input),
      }),
    onSuccess: (user) => {
      client.setQueryData(authKeys.session, user);
    },
  });
}

export function useLogout() {
  const client = useQueryClient();
  const router = useRouter();

  return useMutation({
    mutationFn: () => apiFetch<void>('/api/auth/logout', { method: 'POST' }),
    onSuccess: () => {
      // Drop backend-backed data so it can't render as cached after re-login.
      client.removeQueries({ queryKey: authKeys.session });
      client.removeQueries({ queryKey: ['admin'] });
      client.removeQueries({ queryKey: ['leads'] });
      router.replace('/login');
    },
  });
}

export function hasPermission(user: BackendUser | null | undefined, permission: string): boolean {
  return user?.permissions.includes(permission) ?? false;
}

export function hasAnyPermission(
  user: BackendUser | null | undefined,
  permissions: readonly string[],
): boolean {
  if (permissions.length === 0) return true;
  return permissions.some((permission) => hasPermission(user, permission));
}
