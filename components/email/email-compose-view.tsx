'use client';

import * as React from 'react';
import { Eye, Mail, Search, Send, TriangleAlert } from 'lucide-react';
import { toast } from 'sonner';

import { PageHeader } from '@/components/shared/page-header';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
import { Checkbox } from '@/components/ui/checkbox';
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Alert, AlertDescription, EmptyState, Separator, Skeleton } from '@/components/ui/misc';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { useBackendBusinesses } from '@/hooks/use-leads';
import { useDebouncedValue } from '@/hooks/use-debounced-value';
import {
  useEmailPreview,
  useEmailSignatures,
  useEmailTemplates,
  useSendEmail,
  type EmailPreview,
} from '@/hooks/use-email';
import { formatNumber } from '@/utils/format';

const PAGE_SIZE = 50;
const INHERIT = '__inherit__';

/**
 * Compose and send a preset to selected leads.
 *
 * Only leads that actually carry an email address are offered — the list is
 * filtered server-side with `kind=EmailOnly`, so the user is never selecting
 * recipients that would silently be skipped.
 */
export function EmailComposeView() {
  const [search, setSearch] = React.useState('');
  const [templateId, setTemplateId] = React.useState<string>('');
  const [signatureId, setSignatureId] = React.useState<string>(INHERIT);
  const [selected, setSelected] = React.useState<Set<string>>(new Set());
  const [preview, setPreview] = React.useState<EmailPreview | null>(null);

  const debouncedSearch = useDebouncedValue(search, 350);

  const leads = useBackendBusinesses({
    search: debouncedSearch || undefined,
    // Recipients must have somewhere to send to.
    kind: 'EmailOnly',
    page: 1,
    pageSize: PAGE_SIZE,
    sortBy: 'createdAt',
    sortDir: 'desc',
  });

  const templates = useEmailTemplates(true);
  const signatures = useEmailSignatures();
  const sendEmail = useSendEmail();
  const previewEmail = useEmailPreview();

  const rows = leads.data?.items ?? [];
  const activeTemplate = templates.data?.find((t) => t.id === templateId) ?? null;

  const toggle = (id: string): void => {
    setSelected((current) => {
      const next = new Set(current);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  };

  const allSelected = rows.length > 0 && rows.every((row) => selected.has(row.id));

  const toggleAll = (): void => {
    setSelected((current) => {
      const next = new Set(current);
      if (allSelected) rows.forEach((row) => next.delete(row.id));
      else rows.forEach((row) => next.add(row.id));
      return next;
    });
  };

  const handlePreview = (): void => {
    if (!templateId) return;

    previewEmail.mutate(
      {
        templateId,
        businessId: [...selected][0],
        signatureId: signatureId === INHERIT ? undefined : signatureId,
      },
      {
        onSuccess: setPreview,
        onError: (error) => toast.error(error.message),
      },
    );
  };

  const handleSend = (dryRun: boolean): void => {
    if (!templateId || selected.size === 0) return;

    sendEmail.mutate(
      {
        templateId,
        businessIds: [...selected],
        signatureId: signatureId === INHERIT ? undefined : signatureId,
        dryRun,
      },
      {
        onSuccess: (result) => {
          if (result.failed > 0) {
            toast.warning(
              `${result.sent} sent, ${result.failed} failed, ${result.skipped} skipped.`,
            );
          } else {
            toast.success(
              dryRun
                ? `Dry run rendered ${result.sent} message(s) — nothing delivered.`
                : `Sent ${result.sent} email(s).`,
            );
          }

          if (!dryRun && result.failed === 0) setSelected(new Set());
        },
        onError: (error) => toast.error(error.message),
      },
    );
  };

  const noTemplates = templates.isSuccess && (templates.data?.length ?? 0) === 0;

  return (
    <>
      <PageHeader
        title="Send email"
        description="Email your leads using a preset approved by an administrator."
      />

      {noTemplates && (
        <Alert variant="warning" className="mb-6">
          <TriangleAlert />
          <AlertDescription>
            No active email presets yet. An administrator needs to create one under
            <strong> Email Presets</strong> before you can send.
          </AlertDescription>
        </Alert>
      )}

      <div className="grid gap-6 lg:grid-cols-[minmax(0,1.5fr)_minmax(0,1fr)]">
        <Card>
          <CardHeader>
            <CardTitle>Recipients</CardTitle>
            <CardDescription>
              Only your leads that have an email address are listed.
            </CardDescription>
          </CardHeader>
          <CardContent className="space-y-4">
            <div className="relative">
              <Search className="pointer-events-none absolute left-2.5 top-1/2 size-4 -translate-y-1/2 text-muted-foreground" />
              <Input
                value={search}
                onChange={(event) => setSearch(event.target.value)}
                placeholder="Search your leads…"
                className="pl-8"
                aria-label="Search leads"
              />
            </div>

            {leads.isPending ? (
              <div className="space-y-2">
                {Array.from({ length: 6 }, (_, index) => (
                  <Skeleton key={index} className="h-12 w-full" />
                ))}
              </div>
            ) : rows.length === 0 ? (
              <EmptyState
                icon={<Mail />}
                title="No emailable leads"
                description="Run a search with contact enrichment on to collect email addresses."
              />
            ) : (
              <>
                <label className="flex cursor-pointer items-center gap-2 text-sm">
                  <Checkbox checked={allSelected} onCheckedChange={toggleAll} />
                  Select all {rows.length} shown
                </label>

                <ul className="scrollbar-thin max-h-[26rem] space-y-1 overflow-y-auto pr-1">
                  {rows.map((row) => (
                    <li key={row.id}>
                      <label className="flex cursor-pointer items-start gap-2 rounded-lg border border-border p-2.5 hover:bg-accent/50">
                        <Checkbox
                          checked={selected.has(row.id)}
                          onCheckedChange={() => toggle(row.id)}
                          className="mt-0.5"
                        />
                        <span className="min-w-0 flex-1">
                          <span className="block truncate text-sm font-medium">{row.name}</span>
                          <span className="block truncate text-xs text-success">{row.email}</span>
                          <span className="block truncate text-xs text-muted-foreground">
                            {[row.category, row.city].filter(Boolean).join(' · ')}
                          </span>
                        </span>
                        {row.emailStatus && (
                          <Badge
                            variant={row.emailStatus === 'Valid' ? 'success' : 'muted'}
                            className="shrink-0"
                          >
                            {row.emailStatus}
                          </Badge>
                        )}
                      </label>
                    </li>
                  ))}
                </ul>

                <p className="text-xs text-muted-foreground">
                  Showing the {formatNumber(rows.length)} most recent of{' '}
                  {formatNumber(leads.data?.total ?? 0)}. Narrow with search to reach the rest.
                </p>
              </>
            )}
          </CardContent>
        </Card>

        <Card className="h-fit">
          <CardHeader>
            <CardTitle>Message</CardTitle>
            <CardDescription>
              Presets are authored by an administrator. Placeholders fill in per recipient.
            </CardDescription>
          </CardHeader>
          <CardContent className="space-y-5">
            <div className="space-y-2">
              <Label htmlFor="template">Preset</Label>
              <Select value={templateId} onValueChange={setTemplateId}>
                <SelectTrigger id="template">
                  <SelectValue placeholder="Choose a preset" />
                </SelectTrigger>
                <SelectContent>
                  {(templates.data ?? []).map((template) => (
                    <SelectItem key={template.id} value={template.id}>
                      {template.name}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
              {activeTemplate?.description && (
                <p className="text-xs text-muted-foreground">{activeTemplate.description}</p>
              )}
            </div>

            {activeTemplate && (
              <div className="rounded-lg border border-border bg-muted/40 p-3">
                <p className="text-xs uppercase tracking-wide text-muted-foreground">Subject</p>
                <p className="mt-0.5 break-words text-sm font-medium">{activeTemplate.subject}</p>
              </div>
            )}

            <div className="space-y-2">
              <Label htmlFor="signature">Signature</Label>
              <Select value={signatureId} onValueChange={setSignatureId}>
                <SelectTrigger id="signature">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value={INHERIT}>
                    Use the preset&apos;s signature
                    {activeTemplate?.signatureName ? ` (${activeTemplate.signatureName})` : ''}
                  </SelectItem>
                  {(signatures.data ?? []).map((signature) => (
                    <SelectItem key={signature.id} value={signature.id}>
                      {signature.name}
                      {signature.isDefault ? ' — default' : ''}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>

            <Separator />

            <div className="rounded-lg bg-muted/40 p-3 text-sm">
              <span className="font-medium tabular-nums">{selected.size}</span> recipient(s)
              selected
            </div>

            <div className="space-y-2">
              <Button
                className="w-full"
                onClick={() => handleSend(false)}
                loading={sendEmail.isPending}
                disabled={!templateId || selected.size === 0}
              >
                <Send />
                Send to {selected.size || 0} lead(s)
              </Button>

              <div className="grid grid-cols-2 gap-2">
                <Button
                  variant="outline"
                  onClick={handlePreview}
                  loading={previewEmail.isPending}
                  disabled={!templateId}
                >
                  <Eye />
                  Preview
                </Button>
                <Button
                  variant="outline"
                  onClick={() => handleSend(true)}
                  loading={sendEmail.isPending}
                  disabled={!templateId || selected.size === 0}
                  title="Renders and logs every message without delivering it"
                >
                  Dry run
                </Button>
              </div>
            </div>

            <p className="text-xs text-muted-foreground">
              Sends are paced and capped per day to protect sender reputation, and every message is
              recorded in Email History.
            </p>
          </CardContent>
        </Card>
      </div>

      <Dialog open={preview !== null} onOpenChange={(open) => !open && setPreview(null)}>
        <DialogContent className="max-w-2xl">
          <DialogHeader>
            <DialogTitle className="truncate">{preview?.subject}</DialogTitle>
            <DialogDescription>
              {preview?.toEmail
                ? `Rendered for ${preview.toEmail}`
                : 'Rendered without a recipient — select a lead to fill placeholders.'}
            </DialogDescription>
          </DialogHeader>

          {/* Sandboxed: the body embeds lead data scraped from third-party sites. */}
          <iframe
            title="Email preview"
            sandbox=""
            srcDoc={preview?.bodyHtml ?? ''}
            className="h-[420px] w-full rounded-lg border border-border bg-white"
          />
        </DialogContent>
      </Dialog>
    </>
  );
}
