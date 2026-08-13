'use client';

import * as React from 'react';
import { AlertTriangle, ShieldAlert } from 'lucide-react';
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
import { Label } from '@/components/ui/label';
import { Alert, AlertDescription } from '@/components/ui/misc';
import { ApiClientError } from '@/lib/api-client';
import {
  isLinkedInSessionRestricted,
  useCancelLinkedInLogin,
  useLinkedInLogin,
  useLinkedInLoginVerify,
  useLinkedInSessionStatus,
} from '@/hooks/use-linkedin-session';

type Step = 'form' | 'verifying';

export interface LinkedInSessionDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  /** Shows the "paused" copy and reason instead of the plain connect copy. */
  restricted?: boolean;
  restrictedReason?: string | null;
}

/**
 * The credential-login form itself (with its checkpoint-code step) as a
 * standalone controlled dialog, so it can be triggered either automatically
 * — {@link LinkedInConnectDialog} below, gating a feature the first time a
 * user hits it — or manually, from a permanent control such as the LinkedIn
 * account card on Settings. That second entry point matters because a
 * session row can exist but have gone bad (LinkedIn invalidated it without
 * this app's restriction circuit-breaker ever tripping); the auto-popup
 * variant only offers a way in when there is *no* row at all, which leaves
 * that case with no visible way to reconnect.
 */
export function LinkedInSessionDialog({
  open,
  onOpenChange,
  restricted = false,
  restrictedReason,
}: LinkedInSessionDialogProps) {
  const [step, setStep] = React.useState<Step>('form');
  const [email, setEmail] = React.useState('');
  const [password, setPassword] = React.useState('');
  const [code, setCode] = React.useState('');
  const [prompt, setPrompt] = React.useState('');
  const [errorMessage, setErrorMessage] = React.useState<string | null>(null);

  const login = useLinkedInLogin();
  const verify = useLinkedInLoginVerify();
  const cancelLogin = useCancelLinkedInLogin();

  function resetForm() {
    setStep('form');
    setPassword('');
    setCode('');
    setPrompt('');
    setErrorMessage(null);
  }

  function handleOpenChange(next: boolean) {
    if (!next) {
      // Mid-checkpoint, this releases the server-side browser context and
      // this user's gate rather than leaving them held until the pending
      // login expires on its own.
      if (step === 'verifying') cancelLogin.mutate();
      resetForm();
    }
    onOpenChange(next);
  }

  function describeError(error: unknown): string {
    return error instanceof ApiClientError ? error.message : 'Something went wrong. Please try again.';
  }

  async function handleLoginSubmit(event: React.FormEvent) {
    event.preventDefault();
    setErrorMessage(null);

    try {
      const result = await login.mutateAsync({ linkedInEmail: email, linkedInPassword: password });

      if (result.status === 'success') {
        toast.success('LinkedIn account connected.');
        onOpenChange(false);
        resetForm();
        return;
      }

      if (result.status === 'verificationRequired') {
        setPrompt(result.message);
        setStep('verifying');
        return;
      }

      setErrorMessage(result.message);
    } catch (error) {
      setErrorMessage(describeError(error));
    }
  }

  async function handleVerifySubmit(event: React.FormEvent) {
    event.preventDefault();
    setErrorMessage(null);

    try {
      const result = await verify.mutateAsync({ code });

      if (result.status === 'success') {
        toast.success('LinkedIn account connected.');
        onOpenChange(false);
        resetForm();
        return;
      }

      if (result.status === 'verificationRequired') {
        setPrompt(result.message);
        setCode('');
        return;
      }

      setErrorMessage(result.message);
    } catch (error) {
      setErrorMessage(describeError(error));
    }
  }

  function handleCancelVerify() {
    cancelLogin.mutate();
    resetForm();
  }

  return (
    <Dialog open={open} onOpenChange={handleOpenChange}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{restricted ? 'LinkedIn account paused' : 'Connect your LinkedIn account'}</DialogTitle>
          <DialogDescription>
            {restricted
              ? "LinkedIn flagged unusual activity on this account, so it's paused to protect it from further restriction. It resumes automatically once the cooldown passes, or reconnecting below clears it early."
              : "Enter your LinkedIn email and password — LeadMine signs in on our server using your own account and saves the resulting session. Your password is never stored; it's used once to sign in and then discarded."}
          </DialogDescription>
        </DialogHeader>

        {restricted && restrictedReason && (
          <Alert variant="destructive">
            <AlertTriangle />
            <AlertDescription>{restrictedReason}</AlertDescription>
          </Alert>
        )}

        {errorMessage && (
          <Alert variant="destructive">
            <AlertTriangle />
            <AlertDescription>{errorMessage}</AlertDescription>
          </Alert>
        )}

        {step === 'form' ? (
          <form onSubmit={handleLoginSubmit} className="space-y-4" noValidate>
            <div className="space-y-2">
              <Label htmlFor="linkedin-email">LinkedIn email</Label>
              <Input
                id="linkedin-email"
                type="email"
                autoComplete="username"
                required
                value={email}
                onChange={(event) => setEmail(event.target.value)}
              />
            </div>
            <div className="space-y-2">
              <Label htmlFor="linkedin-password">LinkedIn password</Label>
              <Input
                id="linkedin-password"
                type="password"
                autoComplete="current-password"
                required
                value={password}
                onChange={(event) => setPassword(event.target.value)}
              />
            </div>
            <DialogFooter>
              <Button type="button" variant="ghost" onClick={() => onOpenChange(false)}>
                Not now
              </Button>
              <Button type="submit" loading={login.isPending}>
                Connect
              </Button>
            </DialogFooter>
          </form>
        ) : (
          <form onSubmit={handleVerifySubmit} className="space-y-4" noValidate>
            <p className="text-sm text-muted-foreground">{prompt}</p>
            <div className="space-y-2">
              <Label htmlFor="linkedin-code">Verification code</Label>
              <Input
                id="linkedin-code"
                type="text"
                inputMode="numeric"
                autoComplete="one-time-code"
                required
                value={code}
                onChange={(event) => setCode(event.target.value)}
              />
            </div>
            <DialogFooter>
              <Button type="button" variant="ghost" onClick={handleCancelVerify}>
                Cancel
              </Button>
              <Button type="submit" loading={verify.isPending}>
                Submit
              </Button>
            </DialogFooter>
          </form>
        )}
      </DialogContent>
    </Dialog>
  );
}

/**
 * Gates LinkedIn Enrichment / People Search behind a per-user session:
 * auto-opens {@link LinkedInSessionDialog} the first time a page using this
 * loads without one, and otherwise shows a small banner + button to open it
 * again. Renders nothing once a working session is on file — for a way to
 * reconnect even then (a session that exists but silently stopped working),
 * see the LinkedIn account card on Settings instead.
 */
export function LinkedInConnectDialog() {
  const [open, setOpen] = React.useState(false);
  const autoOpened = React.useRef(false);

  const status = useLinkedInSessionStatus();

  const restricted = isLinkedInSessionRestricted(status.data);
  const needsConnection = status.data === null || restricted;

  // Auto-open the first time this page loads without a working session.
  React.useEffect(() => {
    if (autoOpened.current || status.isPending || !needsConnection) return;
    autoOpened.current = true;
    setOpen(true);
  }, [status.isPending, needsConnection]);

  if (status.isPending || !needsConnection) return null;

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

      <LinkedInSessionDialog
        open={open}
        onOpenChange={setOpen}
        restricted={restricted}
        restrictedReason={status.data?.restrictedReason}
      />
    </>
  );
}
