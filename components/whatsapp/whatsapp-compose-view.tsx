'use client';

import * as React from 'react';
import { ExternalLink, Eye, Link2, MessageCircle, Search, TriangleAlert } from 'lucide-react';
import { toast } from 'sonner';

import { BackendWhatsAppStatusBadge } from '@/components/admin/status-badges';
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
import {
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';
import { useBackendBusinesses } from '@/hooks/use-leads';
import { useDebouncedValue } from '@/hooks/use-debounced-value';
import {
  useGenerateWhatsAppLinks,
  useMarkWhatsAppContacted,
  useWhatsAppPreview,
  useWhatsAppTemplates,
  type WhatsAppLink,
  type WhatsAppPreview,
} from '@/hooks/use-whatsapp';
import { formatNumber } from '@/utils/format';

const PAGE_SIZE = 50;

/**
 * Builds click-to-chat WhatsApp links for selected leads.
 *
 * There is no server-side send — WhatsApp automation is out of scope, so this
 * only prepares a wa.me link per lead. Opening one is a manual, one-at-a-time
 * action: `mark-contacted` fires the moment the user clicks "Open WhatsApp",
 * which stamps `LastWhatsAppContactedAt` on the lead the same way a real email
 * send stamps `LastContactedAt`.
 *
 * Only leads with a **confirmed** WhatsApp link are offered
 * (`whatsappStatus=Confirmed` — the business published the link on its own
 * website). "Likely" leads are excluded here even though they pass the
 * broader `WhatsAppOnly` kind elsewhere: that status is inferred from the
 * phone's line type, not verified, and this page is for actually messaging
 * someone rather than just filtering a list. Leads already WhatsApp'd are
 * hidden by default via `hasBeenWhatsAppContacted=false`.
 */
export function WhatsAppComposeView() {
  const [search, setSearch] = React.useState('');
  const [hideContacted, setHideContacted] = React.useState(true);
  const [templateId, setTemplateId] = React.useState<string>('');
  const [selected, setSelected] = React.useState<Set<string>>(new Set());
  const [preview, setPreview] = React.useState<WhatsAppPreview | null>(null);
  const [generated, setGenerated] = React.useState<WhatsAppLink[] | null>(null);
  const [openedIds, setOpenedIds] = React.useState<Set<string>>(new Set());

  const debouncedSearch = useDebouncedValue(search, 350);

  const leads = useBackendBusinesses({
    search: debouncedSearch || undefined,
    // Only businesses that published their own WhatsApp link — not merely a
    // mobile-type number, which is an inference rather than evidence.
    whatsappStatus: 'Confirmed',
    hasBeenWhatsAppContacted: hideContacted ? false : undefined,
    page: 1,
    pageSize: PAGE_SIZE,
    sortBy: 'createdAt',
    sortDir: 'desc',
  });

  const templates = useWhatsAppTemplates(true);
  const generateLinks = useGenerateWhatsAppLinks();
  const previewMessage = useWhatsAppPreview();
  const markContacted = useMarkWhatsAppContacted();

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

    previewMessage.mutate(
      { templateId, businessId: [...selected][0] },
      {
        onSuccess: setPreview,
        onError: (error) => toast.error(error.message),
      },
    );
  };

  const handleGenerate = (): void => {
    if (!templateId || selected.size === 0) return;

    generateLinks.mutate(
      { templateId, businessIds: [...selected] },
      {
        onSuccess: (result) => {
          setGenerated(result.links);
          setOpenedIds(new Set());

          const skipped = result.links.filter((l) => l.skipReason).length;
          toast.success(
            skipped > 0
              ? `${result.links.length - skipped} link(s) ready, ${skipped} skipped.`
              : `${result.links.length} link(s) ready.`,
          );
        },
        onError: (error) => toast.error(error.message),
      },
    );
  };

  const handleOpen = (link: WhatsAppLink): void => {
    if (!link.url) return;

    // Open synchronously in the click handler — popup blockers kill anything
    // opened after an awaited request resolves.
    window.open(link.url, '_blank', 'noopener,noreferrer');

    setOpenedIds((current) => new Set(current).add(link.businessId));
    markContacted.mutate({ businessId: link.businessId, templateId, message: link.message });
  };

  const noTemplates = templates.isSuccess && (templates.data?.length ?? 0) === 0;

  return (
    <>
      <PageHeader
        title="Send WhatsApp"
        description="Open a click-to-chat WhatsApp conversation with your leads using an approved preset."
      />

      {noTemplates && (
        <Alert variant="warning" className="mb-6">
          <TriangleAlert />
          <AlertDescription>
            No active WhatsApp presets yet. An administrator needs to create one under
            <strong> WhatsApp Presets</strong> before you can generate links.
          </AlertDescription>
        </Alert>
      )}

      <div className="grid gap-6 lg:grid-cols-[minmax(0,1.5fr)_minmax(0,1fr)]">
        <Card>
          <CardHeader>
            <CardTitle>Recipients</CardTitle>
            <CardDescription>
              Only your leads with a confirmed WhatsApp link are listed.
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

            <label className="flex cursor-pointer items-center gap-2 text-sm text-muted-foreground">
              <Checkbox
                checked={hideContacted}
                onCheckedChange={(value) => setHideContacted(value === true)}
              />
              Hide leads already WhatsApp&apos;d
            </label>

            {leads.isPending ? (
              <div className="space-y-2">
                {Array.from({ length: 6 }, (_, index) => (
                  <Skeleton key={index} className="h-12 w-full" />
                ))}
              </div>
            ) : rows.length === 0 ? (
              <EmptyState
                icon={<MessageCircle />}
                title={hideContacted ? 'Nothing left to contact' : 'No confirmed WhatsApp leads'}
                description={
                  hideContacted
                    ? 'Every confirmed lead has already been WhatsApp\'d. Uncheck "Hide leads already WhatsApp\'d" to see them.'
                    : 'A lead needs a WhatsApp link discovered on its own website to show up here — "Likely" (mobile-number inference) isn\'t enough.'
                }
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
                          <span className="block truncate text-xs text-success">{row.phone}</span>
                          <span className="block truncate text-xs text-muted-foreground">
                            {[row.category, row.city].filter(Boolean).join(' · ')}
                          </span>
                        </span>
                        {row.whatsAppStatus && (
                          <span className="shrink-0">
                            <BackendWhatsAppStatusBadge status={row.whatsAppStatus} />
                          </span>
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
                <p className="text-xs uppercase tracking-wide text-muted-foreground">Message</p>
                <p className="mt-0.5 whitespace-pre-wrap break-words text-sm font-medium">
                  {activeTemplate.message}
                </p>
              </div>
            )}

            <Separator />

            <div className="rounded-lg bg-muted/40 p-3 text-sm">
              <span className="font-medium tabular-nums">{selected.size}</span> recipient(s)
              selected
            </div>

            <div className="space-y-2">
              <Button
                className="w-full"
                onClick={handleGenerate}
                loading={generateLinks.isPending}
                disabled={!templateId || selected.size === 0}
              >
                <Link2 />
                Generate {selected.size || 0} link(s)
              </Button>

              <Button
                variant="outline"
                className="w-full"
                onClick={handlePreview}
                loading={previewMessage.isPending}
                disabled={!templateId}
              >
                <Eye />
                Preview
              </Button>
            </div>

            <p className="text-xs text-muted-foreground">
              Opening a link is a manual action — nothing is sent automatically. Each open is
              recorded once you click through.
            </p>
          </CardContent>
        </Card>
      </div>

      {generated && generated.length > 0 && (
        <Card className="mt-6">
          <CardHeader>
            <CardTitle>Generated links</CardTitle>
            <CardDescription>
              Click each one to open WhatsApp with the message ready to go. This does not send
              anything by itself — you still press send inside WhatsApp.
            </CardDescription>
          </CardHeader>
          <CardContent>
            <TableContainer>
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead className="pl-5">Lead</TableHead>
                    <TableHead>Phone</TableHead>
                    <TableHead>Status</TableHead>
                    <TableHead className="pr-5 text-right">Action</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {generated.map((link) => (
                    <TableRow key={link.businessId}>
                      <TableCell className="pl-5 max-w-[200px] truncate font-medium">
                        {link.businessName}
                      </TableCell>
                      <TableCell className="text-sm text-muted-foreground">
                        {link.phone || '—'}
                      </TableCell>
                      <TableCell>
                        {link.skipReason ? (
                          <Badge variant="muted">{link.skipReason}</Badge>
                        ) : openedIds.has(link.businessId) ? (
                          <Badge variant="success">Opened</Badge>
                        ) : (
                          <Badge variant="default">Ready</Badge>
                        )}
                      </TableCell>
                      <TableCell className="pr-5 text-right">
                        <Button
                          size="sm"
                          variant={openedIds.has(link.businessId) ? 'outline' : 'default'}
                          disabled={!link.url}
                          onClick={() => handleOpen(link)}
                        >
                          <ExternalLink />
                          {openedIds.has(link.businessId) ? 'Open again' : 'Open WhatsApp'}
                        </Button>
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </TableContainer>
          </CardContent>
        </Card>
      )}

      <Dialog open={preview !== null} onOpenChange={(open) => !open && setPreview(null)}>
        <DialogContent className="max-w-md">
          <DialogHeader>
            <DialogTitle>Message preview</DialogTitle>
            <DialogDescription>
              {preview?.toPhone
                ? `Rendered for ${preview.toPhone}`
                : 'Rendered without a recipient — select a lead to fill placeholders.'}
            </DialogDescription>
          </DialogHeader>

          <div className="rounded-lg border border-border bg-muted/40 p-3 text-sm whitespace-pre-wrap break-words">
            {preview?.message}
          </div>
        </DialogContent>
      </Dialog>
    </>
  );
}
