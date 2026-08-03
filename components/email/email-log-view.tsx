'use client';

import * as React from 'react';
import { ChevronLeft, ChevronRight, Eye, MailCheck, Search, X } from 'lucide-react';

import { PageHeader } from '@/components/shared/page-header';
import { StatCard } from '@/components/shared/stat-card';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card, CardContent } from '@/components/ui/card';
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Alert, AlertDescription, EmptyState, Skeleton } from '@/components/ui/misc';
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
import { useDebouncedValue } from '@/hooks/use-debounced-value';
import {
  useEmailLog,
  useEmailStats,
  useEmailTemplates,
  type EmailLogEntry,
  type EmailLogQuery,
  type EmailSendStatus,
} from '@/hooks/use-email';
import { formatDateTime, formatNumber } from '@/utils/format';
import { MailX, Send, TrendingUp } from 'lucide-react';

const ANY = '__any__';

interface EmailLogViewProps {
  /** Admin variant shows the sender column and every user's rows. */
  scope: 'mine' | 'all';
}

interface Filters {
  search: string;
  templateId: string;
  status: string;
  from: string;
  to: string;
}

const INITIAL: Filters = { search: '', templateId: ANY, status: ANY, from: '', to: '' };

/**
 * Sent-email history.
 *
 * The same component serves the user's own log and the admin-wide view; the
 * backend decides what is visible, so `scope` only changes presentation. A user
 * asking for another sender's rows is silently forced back to their own.
 */
export function EmailLogView({ scope }: EmailLogViewProps) {
  const [filters, setFilters] = React.useState<Filters>(INITIAL);
  const [page, setPage] = React.useState(1);
  const [viewing, setViewing] = React.useState<EmailLogEntry | null>(null);

  const debouncedSearch = useDebouncedValue(filters.search, 350);

  const updateFilter = <K extends keyof Filters>(key: K, value: Filters[K]): void => {
    setFilters((current) => ({ ...current, [key]: value }));
    setPage(1);
  };

  const query = React.useMemo<EmailLogQuery>(
    () => ({
      search: debouncedSearch || undefined,
      templateId: filters.templateId === ANY ? undefined : filters.templateId,
      status: filters.status === ANY ? undefined : (filters.status as EmailSendStatus),
      // datetime-local yields "2026-08-03T09:00"; the API expects an instant.
      from: filters.from ? new Date(filters.from).toISOString() : undefined,
      to: filters.to ? new Date(filters.to).toISOString() : undefined,
      page,
      pageSize: 50,
    }),
    [debouncedSearch, filters.templateId, filters.status, filters.from, filters.to, page],
  );

  const log = useEmailLog(query);
  const stats = useEmailStats(query.from, query.to);
  const templates = useEmailTemplates();

  const rows = log.data?.items ?? [];
  const total = log.data?.total ?? 0;
  const pageCount = log.data?.pageCount ?? 1;

  const filtersActive =
    Boolean(filters.search) ||
    filters.templateId !== ANY ||
    filters.status !== ANY ||
    Boolean(filters.from) ||
    Boolean(filters.to);

  /** Quick range helper — the common question is "what went out today?". */
  const setRange = (days: number): void => {
    const to = new Date();
    const from = new Date();
    from.setDate(from.getDate() - days);
    from.setHours(0, 0, 0, 0);

    setFilters((current) => ({
      ...current,
      from: toLocalInput(from),
      to: toLocalInput(to),
    }));
    setPage(1);
  };

  return (
    <>
      <PageHeader
        title={scope === 'all' ? 'All email activity' : 'Email history'}
        description={
          scope === 'all'
            ? 'Every email sent by every user — what was sent, to whom, and when.'
            : 'Emails you have sent to leads.'
        }
      />

      <div className="mb-6 grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
        <StatCard
          label="Sent"
          value={formatNumber(stats.data?.totalSent ?? 0)}
          hint="In the selected range"
          icon={Send}
          tone="success"
          loading={stats.isPending}
        />
        <StatCard
          label="Failed"
          value={formatNumber(stats.data?.totalFailed ?? 0)}
          hint="Delivery rejected"
          icon={MailX}
          tone={stats.data?.totalFailed ? 'warning' : 'muted'}
          loading={stats.isPending}
        />
        <StatCard
          label="Today"
          value={formatNumber(stats.data?.sentToday ?? 0)}
          hint="Since midnight UTC"
          icon={MailCheck}
          tone="primary"
          loading={stats.isPending}
        />
        <StatCard
          label="Last 7 days"
          value={formatNumber(stats.data?.sentLast7Days ?? 0)}
          hint="Rolling week"
          icon={TrendingUp}
          tone="muted"
          loading={stats.isPending}
        />
      </div>

      <Card className="mb-6">
        <CardContent className="space-y-3 p-5">
          <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
            <div className="relative sm:col-span-2">
              <Search className="pointer-events-none absolute left-2.5 top-1/2 size-4 -translate-y-1/2 text-muted-foreground" />
              <Input
                value={filters.search}
                onChange={(event) => updateFilter('search', event.target.value)}
                placeholder="Search recipient, subject or sender…"
                className="pl-8"
                aria-label="Search sent email"
              />
            </div>

            <Select
              value={filters.templateId}
              onValueChange={(next) => updateFilter('templateId', next)}
            >
              <SelectTrigger aria-label="Filter by preset">
                <SelectValue placeholder="All presets" />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value={ANY}>All presets</SelectItem>
                {(templates.data ?? []).map((template) => (
                  <SelectItem key={template.id} value={template.id}>
                    {template.name}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>

            <Select value={filters.status} onValueChange={(next) => updateFilter('status', next)}>
              <SelectTrigger aria-label="Filter by status">
                <SelectValue placeholder="Any status" />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value={ANY}>Any status</SelectItem>
                <SelectItem value="Sent">Sent</SelectItem>
                <SelectItem value="Failed">Failed</SelectItem>
                <SelectItem value="Queued">Queued</SelectItem>
              </SelectContent>
            </Select>
          </div>

          <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
            <div className="space-y-1.5">
              <Label htmlFor="from" className="text-xs">
                From
              </Label>
              <Input
                id="from"
                type="datetime-local"
                value={filters.from}
                onChange={(event) => updateFilter('from', event.target.value)}
              />
            </div>

            <div className="space-y-1.5">
              <Label htmlFor="to" className="text-xs">
                To
              </Label>
              <Input
                id="to"
                type="datetime-local"
                value={filters.to}
                onChange={(event) => updateFilter('to', event.target.value)}
              />
            </div>

            <div className="flex items-end gap-1.5 sm:col-span-2">
              {[
                { label: 'Today', days: 0 },
                { label: '7 days', days: 7 },
                { label: '30 days', days: 30 },
              ].map((preset) => (
                <Button
                  key={preset.label}
                  type="button"
                  variant="outline"
                  size="sm"
                  onClick={() => setRange(preset.days)}
                >
                  {preset.label}
                </Button>
              ))}

              {filtersActive && (
                <Button
                  type="button"
                  variant="ghost"
                  size="sm"
                  onClick={() => {
                    setFilters(INITIAL);
                    setPage(1);
                  }}
                >
                  <X />
                  Clear
                </Button>
              )}
            </div>
          </div>
        </CardContent>
      </Card>

      {log.isError && (
        <Alert variant="destructive" className="mb-6">
          <AlertDescription>{log.error.message}</AlertDescription>
        </Alert>
      )}

      <Card>
        {log.isPending ? (
          <CardContent className="space-y-2 p-5">
            {Array.from({ length: 8 }, (_, index) => (
              <Skeleton key={index} className="h-12 w-full" />
            ))}
          </CardContent>
        ) : rows.length === 0 ? (
          <CardContent className="p-5">
            <EmptyState
              icon={<MailCheck />}
              title={filtersActive ? 'No matching emails' : 'No emails sent yet'}
              description={
                filtersActive
                  ? 'Try widening the date range or clearing the filters.'
                  : 'Emails sent to leads will be recorded here.'
              }
            />
          </CardContent>
        ) : (
          <>
            <TableContainer>
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead className="pl-5">Sent</TableHead>
                    {scope === 'all' && <TableHead>Sender</TableHead>}
                    <TableHead>Recipient</TableHead>
                    <TableHead>Subject</TableHead>
                    <TableHead>Preset</TableHead>
                    <TableHead>Status</TableHead>
                    <TableHead className="pr-5 text-right">View</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {rows.map((row) => (
                    <TableRow key={row.id}>
                      <TableCell className="whitespace-nowrap pl-5 text-xs text-muted-foreground">
                        {formatDateTime(row.sentAt ?? row.createdAt)}
                      </TableCell>

                      {scope === 'all' && (
                        <TableCell className="max-w-[180px] truncate text-sm">
                          {row.senderEmail}
                        </TableCell>
                      )}

                      <TableCell>
                        <p className="max-w-[220px] truncate text-sm font-medium">{row.toEmail}</p>
                        {row.toName && (
                          <p className="max-w-[220px] truncate text-xs text-muted-foreground">
                            {row.toName}
                          </p>
                        )}
                      </TableCell>

                      <TableCell className="max-w-[260px] truncate text-sm">{row.subject}</TableCell>

                      <TableCell>
                        <Badge variant="secondary">{row.templateName || '—'}</Badge>
                      </TableCell>

                      <TableCell>
                        <Badge
                          variant={
                            row.status === 'Sent'
                              ? 'success'
                              : row.status === 'Failed'
                                ? 'destructive'
                                : 'muted'
                          }
                          title={row.error ?? undefined}
                        >
                          {row.status}
                        </Badge>
                      </TableCell>

                      <TableCell className="pr-5 text-right">
                        <Button
                          variant="ghost"
                          size="icon-sm"
                          onClick={() => setViewing(row)}
                          aria-label="View the email that was sent"
                        >
                          <Eye />
                        </Button>
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </TableContainer>

            <div className="flex items-center justify-between gap-3 border-t border-border px-5 py-3">
              <span className="text-xs tabular-nums text-muted-foreground">
                {formatNumber(total)} email(s) · page {page} of {pageCount}
              </span>
              <div className="flex gap-1">
                <Button
                  variant="outline"
                  size="icon-sm"
                  onClick={() => setPage((current) => Math.max(1, current - 1))}
                  disabled={page <= 1}
                  aria-label="Previous page"
                >
                  <ChevronLeft />
                </Button>
                <Button
                  variant="outline"
                  size="icon-sm"
                  onClick={() => setPage((current) => Math.min(pageCount, current + 1))}
                  disabled={page >= pageCount}
                  aria-label="Next page"
                >
                  <ChevronRight />
                </Button>
              </div>
            </div>
          </>
        )}
      </Card>

      <Dialog open={viewing !== null} onOpenChange={(open) => !open && setViewing(null)}>
        <DialogContent className="max-w-2xl">
          <DialogHeader>
            <DialogTitle className="truncate">{viewing?.subject}</DialogTitle>
            <DialogDescription>
              {viewing?.senderEmail} → {viewing?.toEmail} ·{' '}
              {viewing && formatDateTime(viewing.sentAt ?? viewing.createdAt)}
            </DialogDescription>
          </DialogHeader>

          {viewing?.error && (
            <Alert variant="destructive">
              <AlertDescription>{viewing.error}</AlertDescription>
            </Alert>
          )}

          {/*
            The exact HTML that was delivered. Rendered in a sandboxed iframe
            rather than with dangerouslySetInnerHTML: the body embeds lead data
            from third-party websites, so it is untrusted content and must not
            execute or read anything from this origin.
          */}
          <iframe
            title="Email content"
            sandbox=""
            srcDoc={viewing?.bodyHtml ?? ''}
            className="h-[420px] w-full rounded-lg border border-border bg-white"
          />
        </DialogContent>
      </Dialog>
    </>
  );
}

/** Formats a Date for a `datetime-local` input, which wants local wall time. */
function toLocalInput(date: Date): string {
  const pad = (value: number) => String(value).padStart(2, '0');
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`;
}
