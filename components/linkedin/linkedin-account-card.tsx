'use client';

import * as React from 'react';
import { Link2, ShieldAlert } from 'lucide-react';
import { toast } from 'sonner';

import { Button } from '@/components/ui/button';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
import { Alert, AlertDescription, Skeleton } from '@/components/ui/misc';
import {
  isLinkedInSessionRestricted,
  useLinkedInSessionStatus,
  useRemoveLinkedInSession,
} from '@/hooks/use-linkedin-session';
import { LinkedInSessionDialog } from './linkedin-connect-dialog';

/**
 * A permanent LinkedIn account control for Settings. Unlike
 * `LinkedInConnectDialog` (which only offers a way in when there is no
 * session row at all), this is always visible with a "Reconnect" option —
 * a session row can exist but have quietly stopped working (LinkedIn
 * invalidating it server-side without this app's restriction
 * circuit-breaker ever tripping), and without this there would be no way
 * back in short of deleting the row directly in the database.
 */
export function LinkedInAccountCard() {
  const [open, setOpen] = React.useState(false);
  const status = useLinkedInSessionStatus();
  const removeSession = useRemoveLinkedInSession();

  const restricted = isLinkedInSessionRestricted(status.data);
  const session = status.data ?? null;

  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center gap-2">
          <Link2 className="size-4" />
          LinkedIn account
        </CardTitle>
        <CardDescription>
          Connect your own LinkedIn account so LinkedIn Enrichment and People Search can sign in as you.
        </CardDescription>
      </CardHeader>
      <CardContent className="space-y-4">
        {status.isPending ? (
          <Skeleton className="h-10 w-full" />
        ) : session === null ? (
          <p className="text-sm text-muted-foreground">Not connected yet.</p>
        ) : restricted ? (
          <Alert variant="destructive">
            <ShieldAlert />
            <AlertDescription>
              Paused — {session.restrictedReason ?? 'LinkedIn flagged unusual activity on this account.'}{' '}
              Reconnecting below clears this early.
            </AlertDescription>
          </Alert>
        ) : (
          <p className="text-sm text-muted-foreground">
            Connected since {new Date(session.uploadedAt).toLocaleString()} ·{' '}
            {session.searchesToday} search{session.searchesToday === 1 ? '' : 'es'} today.
          </p>
        )}

        <div className="flex items-center gap-3">
          <Button type="button" variant="outline" onClick={() => setOpen(true)}>
            {session ? 'Reconnect' : 'Connect LinkedIn'}
          </Button>
          {session && (
            <Button
              type="button"
              variant="ghost"
              className="text-destructive"
              loading={removeSession.isPending}
              onClick={() =>
                removeSession.mutate(undefined, {
                  onSuccess: () => toast.success('LinkedIn account disconnected.'),
                  onError: (error) => toast.error(error.message),
                })
              }
            >
              Disconnect
            </Button>
          )}
        </div>
      </CardContent>

      <LinkedInSessionDialog
        open={open}
        onOpenChange={setOpen}
        restricted={restricted}
        restrictedReason={session?.restrictedReason}
      />
    </Card>
  );
}
