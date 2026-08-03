'use client';

import * as React from 'react';
import Link from 'next/link';
import { usePathname, useRouter } from 'next/navigation';
import { ShieldAlert } from 'lucide-react';

import { useAuth } from '@/components/providers/auth-provider';
import { Button } from '@/components/ui/button';
import { Card, CardContent } from '@/components/ui/card';
import { Skeleton } from '@/components/ui/misc';

interface RequireAuthProps {
  children: React.ReactNode;
  /** Any one of these grants access. Empty means "any signed-in user". */
  permissions?: readonly string[];
}

function AuthSkeleton() {
  return (
    <div className="space-y-4">
      <Skeleton className="h-8 w-56" />
      <Skeleton className="h-64 w-full rounded-lg" />
    </div>
  );
}

function AccessDenied() {
  return (
    <Card>
      <CardContent className="flex flex-col items-center gap-3 py-12 text-center">
        <span className="flex size-11 items-center justify-center rounded-full bg-destructive/10 text-destructive">
          <ShieldAlert className="size-5" />
        </span>
        <h2 className="font-semibold">Access denied</h2>
        <p className="max-w-sm text-sm text-muted-foreground">
          Your account does not have permission to view this page. Ask an administrator to grant you
          the required role.
        </p>
        <Button asChild variant="outline">
          <Link href="/dashboard">Back to dashboard</Link>
        </Button>
      </CardContent>
    </Card>
  );
}

export function RequireAuth({ children, permissions = [] }: RequireAuthProps) {
  const { isPending, isAuthenticated, hasAnyPermission } = useAuth();
  const router = useRouter();
  const pathname = usePathname();
  const requiredKey = permissions.join('|');

  React.useEffect(() => {
    if (!isPending && !isAuthenticated) {
      router.replace(`/login?next=${encodeURIComponent(pathname)}`);
    }
  }, [isPending, isAuthenticated, pathname, router]);

  if (isPending) return <AuthSkeleton />;
  if (!isAuthenticated) return null;

  if (requiredKey && !hasAnyPermission(requiredKey.split('|'))) {
    return <AccessDenied />;
  }

  return <>{children}</>;
}
