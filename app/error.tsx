'use client';

import { useEffect } from 'react';
import { AlertTriangle, RotateCcw } from 'lucide-react';

import { Button } from '@/components/ui/button';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';

export default function GlobalError({
  error,
  reset,
}: {
  error: Error & { digest?: string };
  reset: () => void;
}) {
  useEffect(() => {
    console.error('[app] render error:', error);
  }, [error]);

  return (
    <div className="flex min-h-dvh items-center justify-center p-6">
      <Card className="w-full max-w-md">
        <CardHeader>
          <div className="mb-2 flex size-10 items-center justify-center rounded-full bg-destructive/12 text-destructive">
            <AlertTriangle className="size-5" />
          </div>
          <CardTitle>Something went wrong</CardTitle>
          <CardDescription>
            {error.message || 'An unexpected error occurred while rendering this page.'}
          </CardDescription>
        </CardHeader>
        <CardContent className="space-y-4">
          {error.digest && (
            <p className="font-mono text-xs text-muted-foreground">Digest: {error.digest}</p>
          )}
          <Button onClick={reset}>
            <RotateCcw />
            Try again
          </Button>
        </CardContent>
      </Card>
    </div>
  );
}
