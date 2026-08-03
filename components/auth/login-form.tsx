'use client';

import * as React from 'react';
import Link from 'next/link';
import { useRouter, useSearchParams } from 'next/navigation';
import { zodResolver } from '@hookform/resolvers/zod';
import { useForm } from 'react-hook-form';

import { useAuth } from '@/components/providers/auth-provider';
import { Alert, AlertDescription, Skeleton } from '@/components/ui/misc';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardFooter, CardHeader, CardTitle } from '@/components/ui/card';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { ApiClientError } from '@/lib/api-client';
import { loginSchema, type LoginInput } from '@/lib/validation/auth.schema';

export function LoginForm() {
  return (
    <React.Suspense fallback={<CardContent><Skeleton className="h-72 w-full" /></CardContent>}>
      <LoginFormInner />
    </React.Suspense>
  );
}

function LoginFormInner() {
  const { signIn } = useAuth();
  const router = useRouter();
  const params = useSearchParams();

  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<LoginInput>({
    resolver: zodResolver(loginSchema),
    defaultValues: { email: '', password: '' },
  });

  const [formError, setFormError] = React.useState<string | null>(null);

  async function onSubmit(values: LoginInput) {
    setFormError(null);
    try {
      await signIn(values);
      const next = params.get('next');
      router.replace(next && next.startsWith('/') && !next.startsWith('//') ? next : '/admin/leads');
    } catch (error) {
      const message =
        error instanceof ApiClientError ? error.message : 'Could not sign in. Please try again.';
      setFormError(message);
    }
  }

  return (
    <Card>
      <form onSubmit={handleSubmit(onSubmit)} noValidate>
        <CardHeader>
          <CardTitle>Sign in</CardTitle>
        </CardHeader>
        <CardContent className="space-y-4">
          {formError && (
            <Alert variant="destructive">
              <AlertDescription>{formError}</AlertDescription>
            </Alert>
          )}

          <div className="space-y-2">
            <Label htmlFor="email">Email</Label>
            <Input
              id="email"
              type="email"
              autoComplete="email"
              placeholder="you@example.com"
              aria-invalid={errors.email ? true : undefined}
              {...register('email')}
            />
            {errors.email && <p className="text-xs text-destructive">{errors.email.message}</p>}
          </div>

          <div className="space-y-2">
            <Label htmlFor="password">Password</Label>
            <Input
              id="password"
              type="password"
              autoComplete="current-password"
              aria-invalid={errors.password ? true : undefined}
              {...register('password')}
            />
            {errors.password && (
              <p className="text-xs text-destructive">{errors.password.message}</p>
            )}
          </div>
        </CardContent>
        <CardFooter className="flex-col items-stretch gap-3">
          <Button type="submit" loading={isSubmitting}>
            Sign in
          </Button>
          {/* <p className="text-center text-sm text-muted-foreground">
            No account?{' '}
            <Link href="/register" className="text-primary underline-offset-4 hover:underline">
              Create one
            </Link>
          </p> */}
        </CardFooter>
      </form>
    </Card>
  );
}
