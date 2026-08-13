'use client';

import * as React from 'react';
import { AlertTriangle, ExternalLink, ShieldAlert, UploadCloud } from 'lucide-react';
import { toast } from 'sonner';

import { Button } from '@/components/ui/button';
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
import { Input } from '@/components/ui/input';
import { Alert, AlertDescription } from '@/components/ui/misc';
import {
  isLinkedInSessionRestricted,
  useLinkedInSessionStatus,
  useUploadLinkedInSession,
} from '@/hooks/use-linkedin-session';

const LOGIN_TOOL_PATH = 'backend/tools/LinkedInLogin';

/**
 * Gates LinkedIn Enrichment / People Search behind a per-user session: each
 * user logs into their own LinkedIn account locally — never through this
 * app's servers — and uploads the resulting session file once. Renders
 * nothing once a working session is on file.
 *
 * Auto-opens the connect dialog the first time a page that needs it is
 * visited without one, and stays reachable afterward through the inline
 * banner if dismissed.
 */
export function LinkedInConnectDialog() {
  const status = useLinkedInSessionStatus();
  const upload = useUploadLinkedInSession();

  const [open, setOpen] = React.useState(false);
  const [file, setFile] = React.useState<File | null>(null);
  const autoOpened = React.useRef(false);

  const restricted = isLinkedInSessionRestricted(status.data);
  const needsConnection = status.data === null || restricted;

  React.useEffect(() => {
    if (autoOpened.current || status.isPending || !needsConnection) return;
    autoOpened.current = true;
    setOpen(true);
  }, [status.isPending, needsConnection]);

  if (status.isPending || !needsConnection) return null;

  const handleUpload = (): void => {
    if (!file) return;
    upload.mutate(file, {
      onSuccess: () => {
        toast.success('LinkedIn account connected.');
        setFile(null);
        setOpen(false);
      },
      onError: (error) => toast.error(error.message),
    });
  };

  return (
    <>
      <Alert variant={restricted ? 'destructive' : 'warning'} className="mb-6">
        <ShieldAlert />
        <AlertDescription className="flex flex-wrap items-center justify-between gap-3">
          <span>
            {restricted
              ? 'LinkedIn features are paused for your account until this cools down.'
              : 'To use this feature you need to connect your own LinkedIn account.'}
          </span>
          <Button size="sm" variant="outline" onClick={() => setOpen(true)}>
            {restricted ? 'View details' : 'Connect LinkedIn'}
          </Button>
        </AlertDescription>
      </Alert>

      <Dialog open={open} onOpenChange={setOpen}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>
              {restricted ? 'LinkedIn account paused' : 'Connect your LinkedIn account'}
            </DialogTitle>
            <DialogDescription>
              {restricted
                ? "LinkedIn flagged unusual activity on this account, so it's paused to protect it from further restriction. It resumes automatically once the cooldown passes, or you can connect a different account below."
                : "To use LinkedIn Enrichment and People Search, this app needs your own LinkedIn session — never your password. Each user connects their own account, logged in entirely on your own machine."}
            </DialogDescription>
          </DialogHeader>

          {restricted && status.data?.restrictedReason && (
            <Alert variant="destructive">
              <AlertTriangle />
              <AlertDescription>{status.data.restrictedReason}</AlertDescription>
            </Alert>
          )}

          <ol className="list-decimal space-y-2 pl-4 text-sm text-muted-foreground">
            <li>
              On your own machine, run the local login tool at{' '}
              <code className="rounded bg-muted px-1 py-0.5 text-xs text-foreground">
                {LOGIN_TOOL_PATH}
              </code>
              .
            </li>
            <li>
              Log in with your own LinkedIn account in the window it opens — your credentials never
              touch this app.
            </li>
            <li>Upload the session file it saves when you&apos;re done, below.</li>
          </ol>

          <Button variant="outline" asChild className="w-fit">
            <a href="https://www.linkedin.com/login" target="_blank" rel="noopener noreferrer">
              <ExternalLink />
              Open LinkedIn to sign in
            </a>
          </Button>

          <Input
            type="file"
            accept="application/json"
            onChange={(event) => setFile(event.target.files?.[0] ?? null)}
          />

          <DialogFooter>
            <Button variant="ghost" onClick={() => setOpen(false)}>
              Not now
            </Button>
            <Button onClick={handleUpload} disabled={!file} loading={upload.isPending}>
              <UploadCloud />
              Connect
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </>
  );
}
