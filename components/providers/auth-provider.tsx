'use client';

import * as React from 'react';

import {
  hasAnyPermission,
  hasPermission,
  useLogin,
  useLogout,
  useRegister,
  useSession,
  type LoginInput,
  type RegisterInput,
} from '@/hooks/use-auth';
import type { BackendUser } from '@/lib/backend/types';

interface AuthContextValue {
  user: BackendUser | null;
  /** True while the session is first loading (not during background refetch). */
  isPending: boolean;
  isAuthenticated: boolean;
  signIn: (input: LoginInput) => Promise<BackendUser>;
  signUp: (input: RegisterInput) => Promise<BackendUser>;
  signOut: () => Promise<void>;
  hasPermission: (permission: string) => boolean;
  hasAnyPermission: (permissions: readonly string[]) => boolean;
}

const AuthContext = React.createContext<AuthContextValue | null>(null);

export function AuthProvider({ children }: { children: React.ReactNode }) {
  const session = useSession();
  const login = useLogin();
  const register = useRegister();
  const logout = useLogout();

  const value = React.useMemo<AuthContextValue>(
    () => ({
      user: session.data ?? null,
      isPending: session.isLoading,
      isAuthenticated: session.isSuccess && !!session.data,
      signIn: login.mutateAsync,
      signUp: register.mutateAsync,
      signOut: logout.mutateAsync,
      hasPermission: (permission) => hasPermission(session.data, permission),
      hasAnyPermission: (permissions) => hasAnyPermission(session.data, permissions),
    }),
    [session.data, session.isLoading, session.isSuccess, login.mutateAsync, register.mutateAsync, logout.mutateAsync],
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthContextValue {
  const context = React.useContext(AuthContext);
  if (!context) throw new Error('useAuth must be used within an AuthProvider');
  return context;
}
