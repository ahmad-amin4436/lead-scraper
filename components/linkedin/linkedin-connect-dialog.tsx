'use client';

import * as React from 'react';
import { AlertTriangle, Check, Copy, Loader2, ShieldAlert } from 'lucide-react';
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
import { Alert, AlertDescription } from '@/components/ui/misc';
import {
  isLinkedInSessionRestricted,
  useCreateLinkedInConnectToken,
  useLinkedInSessionStatus,
} from '@/hooks/use-linkedin-session';

function buildCommand(token: string, apiBaseUrl: string): string {
  return `dotnet run --project backend/tools/LinkedInLogin -- --connect ${token} --api ${apiBaseUrl}`;
}

/**
 * Gates LinkedIn Enrichment / People Search behind a per-user session: each
 * user logs into their own LinkedIn account locally — never through this
 * app's servers — and `backend/tools/LinkedInLogin` pushes the captured
 * session straight to their account using a short-lived connect code. There
 * is no file to find or upload; this dialog only displays the one command to
 * run, then polls and closes itself once it lands.
 *
 * Renders nothing once a working session is on file.
 */
export function LinkedInConnectDialog() {
  const [open, setOpen] = React.useState(false);
  const [copied, setCopied] = React.useState(false);
  const autoOpened = React.useRef(false);
  const hadPendingConnection = React.useRef(false);

  const status = useLinkedInSessionStatus(open);
  const connectToken = useCreateLinkedInConnectToken();

  const restricted = isLinkedInSessionRestricted(status.data);
  const needsConnection = status.data === null || restricted;

  // Auto-open the first time this page loads without a working session.
  React.useEffect(() => {
    if (autoOpened.current || status.isPending || !needsConnection) return;
    autoOpened.current = true;
    setOpen(true);
  }, [status.isPending, needsConnection]);

  // A fresh code every time the dialog opens — the last one may have expired.
  React.useEffect(() => {
    if (open) connectToken.mutate();
    // connectToken is a fresh object each render; only re-run when the dialog opens.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open]);

  // Notice the session landing (via the poll above) and close automatically.
  React.useEffect(() => {
    if (!open) return;

    if (needsConnection) {
      hadPendingConnection.current = true;
      return;
    }

    if (hadPendingConnection.current) {
      hadPendingConnection.current = false;
      toast.success('LinkedIn account connected.');
      setOpen(false);
    }
  }, [open, needsConnection]);

  if (status.isPending || !needsConnection) return null;

  const command = connectToken.data ? buildCommand(connectToken.data.token, connectToken.data.apiBaseUrl) : null;

  const handleCopy = (): void => {
    if (!command) return;
    void navigator.clipboard.writeText(command).then(() => {
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
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
            <DialogTitle>{restricted ? 'LinkedIn account paused' : 'Connect your LinkedIn account'}</DialogTitle>
            <DialogDescription>
              {restricted
                ? "LinkedIn flagged unusual activity on this account, so it's paused to protect it from further restriction. It resumes automatically once the cooldown passes, or logging in again below clears it early."
                : 'This runs entirely on your own machine — your LinkedIn password never touches this app, only the resulting session.'}
            </DialogDescription>
          </DialogHeader>

          {restricted && status.data?.restrictedReason && (
            <Alert variant="destructive">
              <AlertTriangle />
              <AlertDescription>{status.data.restrictedReason}</AlertDescription>
            </Alert>
          )}

          <ol className="list-decimal space-y-2 pl-4 text-sm text-muted-foreground">
            <li>Copy the command below and run it in a terminal on your own machine.</li>
            <li>
              Log in with your own LinkedIn account in the window it opens, then press Enter in
              that terminal.
            </li>
            <li>That&apos;s it — this closes on its own once it&apos;s connected.</li>
          </ol>

          <div className="space-y-2">
            {command ? (
              <div className="flex items-start gap-2">
                <code className="flex-1 overflow-x-auto rounded-md border border-border bg-muted px-3 py-2 text-xs">
                  {command}
                </code>
                <Button variant="outline" size="icon" onClick={handleCopy} aria-label="Copy command">
                  {copied ? <Check className="text-success" /> : <Copy />}
                </Button>
              </div>
            ) : (
              <div className="flex items-center gap-2 text-sm text-muted-foreground">
                <Loader2 className="size-4 animate-spin" />
                Generating your connect code…
              </div>
            )}

            {connectToken.data && (
              <p className="text-xs text-muted-foreground">
                Valid for 15 minutes. Waiting for it to run — this page checks automatically.
              </p>
            )}

            {connectToken.isError && (
              <p className="text-xs text-destructive">
                Could not generate a connect code: {connectToken.error.message}
              </p>
            )}
          </div>

          <DialogFooter>
            <Button variant="ghost" onClick={() => setOpen(false)}>
              Not now
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </>
  );
}
