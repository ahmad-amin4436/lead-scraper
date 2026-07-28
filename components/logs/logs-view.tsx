'use client';

import * as React from 'react';
import { ChevronLeft, ChevronRight, RefreshCw, ScrollText, Search, Trash2 } from 'lucide-react';
import { toast } from 'sonner';

import { PageHeader } from '@/components/shared/page-header';
import { LogLevelBadge } from '@/components/shared/status-badge';
import { Button } from '@/components/ui/button';
import { Card, CardContent } from '@/components/ui/card';
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
import { Input } from '@/components/ui/input';
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
import { useClearLogs, useLogs } from '@/hooks/use-api';
import { useDebouncedValue } from '@/hooks/use-debounced-value';
import { cn } from '@/lib/utils';
import { LOG_EVENTS, LOG_LEVELS, type LogEvent, type LogLevel, type LogQuery } from '@/types/log';
import { formatDateTime, formatDuration, formatNumber } from '@/utils/format';

const ANY = '__any__';
const AUTO_REFRESH_MS = 4000;

interface Filters {
  search: string;
  level: string;
  event: string;
}

const INITIAL_FILTERS: Filters = { search: '', level: ANY, event: ANY };

export function LogsView() {
  const [filters, setFilters] = React.useState<Filters>(INITIAL_FILTERS);
  const [page, setPage] = React.useState(1);
  const [autoRefresh, setAutoRefresh] = React.useState(true);
  const [confirmClear, setConfirmClear] = React.useState(false);

  const { search, level, event } = filters;
  const debouncedSearch = useDebouncedValue(search, 350);

  // Reset to page 1 alongside the filter change rather than in a follow-up effect.
  const updateFilter = <K extends keyof Filters>(key: K, value: Filters[K]): void => {
    setFilters((current) => ({ ...current, [key]: value }));
    setPage(1);
  };

  const query = React.useMemo<LogQuery>(
    () => ({
      search: debouncedSearch || undefined,
      level: level === ANY ? undefined : (level as LogLevel),
      event: event === ANY ? undefined : (event as LogEvent),
      page,
      pageSize: 50,
    }),
    [debouncedSearch, level, event, page],
  );

  const logs = useLogs(query, autoRefresh ? AUTO_REFRESH_MS : undefined);
  const clearLogs = useClearLogs();

  const rows = logs.data?.rows ?? [];
  const total = logs.data?.total ?? 0;
  const pageCount = logs.data?.pageCount ?? 1;

  return (
    <>
      <PageHeader
        title="Logs"
        description="Started, completed, failed and skipped events across every run."
        actions={
          <>
            <Button
              variant={autoRefresh ? 'default' : 'outline'}
              onClick={() => setAutoRefresh((current) => !current)}
            >
              <RefreshCw className={cn(autoRefresh && 'animate-spin')} />
              {autoRefresh ? 'Live' : 'Paused'}
            </Button>
            <Button variant="outline" onClick={() => setConfirmClear(true)} disabled={total === 0}>
              <Trash2 />
              Clear
            </Button>
          </>
        }
      />

      <Card className="mb-6">
        <CardContent className="grid gap-3 p-5 sm:grid-cols-[minmax(0,2fr)_repeat(2,minmax(0,1fr))]">
          <div className="relative">
            <Search className="pointer-events-none absolute left-2.5 top-1/2 size-4 -translate-y-1/2 text-muted-foreground" />
            <Input
              value={search}
              onChange={(changeEvent) => updateFilter('search', changeEvent.target.value)}
              placeholder="Search log messages…"
              className="pl-8"
              aria-label="Search logs"
            />
          </div>

          <Select value={level} onValueChange={(next) => updateFilter('level', next)}>
            <SelectTrigger aria-label="Filter by level">
              <SelectValue placeholder="All levels" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value={ANY}>All levels</SelectItem>
              {LOG_LEVELS.map((item) => (
                <SelectItem key={item} value={item}>
                  {item}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>

          <Select value={event} onValueChange={(next) => updateFilter('event', next)}>
            <SelectTrigger aria-label="Filter by event">
              <SelectValue placeholder="All events" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value={ANY}>All events</SelectItem>
              {LOG_EVENTS.map((item) => (
                <SelectItem key={item} value={item}>
                  {item}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </CardContent>
      </Card>

      {logs.isError && (
        <Alert variant="destructive" className="mb-6">
          <AlertDescription>{logs.error.message}</AlertDescription>
        </Alert>
      )}

      <Card>
        {logs.isPending ? (
          <CardContent className="space-y-2 p-5">
            {Array.from({ length: 10 }, (_, index) => (
              <Skeleton key={index} className="h-10 w-full" />
            ))}
          </CardContent>
        ) : rows.length === 0 ? (
          <CardContent className="p-5">
            <EmptyState
              icon={<ScrollText />}
              title="No log entries"
              description="Activity from searches, enrichment and exports is recorded here."
            />
          </CardContent>
        ) : (
          <>
            <TableContainer>
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead className="pl-5">Time</TableHead>
                    <TableHead>Level</TableHead>
                    <TableHead>Event</TableHead>
                    <TableHead>Message</TableHead>
                    <TableHead className="pr-5">Elapsed</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {rows.map((entry) => (
                    <TableRow key={entry.id}>
                      <TableCell className="whitespace-nowrap pl-5 text-xs text-muted-foreground">
                        {formatDateTime(entry.timestamp)}
                      </TableCell>
                      <TableCell>
                        <LogLevelBadge level={entry.level} />
                      </TableCell>
                      <TableCell>
                        <code className="rounded bg-muted px-1.5 py-0.5 font-mono text-[11px]">
                          {entry.event}
                        </code>
                      </TableCell>
                      <TableCell>
                        <p className="max-w-[520px] break-words text-sm">{entry.message}</p>
                        {Object.keys(entry.context).length > 0 && (
                          <p className="mt-0.5 max-w-[520px] truncate font-mono text-[11px] text-muted-foreground">
                            {Object.entries(entry.context)
                              .map(([key, value]) => `${key}=${String(value)}`)
                              .join(' ')}
                          </p>
                        )}
                      </TableCell>
                      <TableCell className="whitespace-nowrap pr-5 text-xs tabular-nums text-muted-foreground">
                        {entry.elapsedMs !== null ? formatDuration(entry.elapsedMs) : '—'}
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </TableContainer>

            <div className="flex items-center justify-between gap-3 border-t border-border px-5 py-3">
              <span className="text-xs tabular-nums text-muted-foreground">
                {formatNumber(total)} entries · page {page} of {pageCount}
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

      <Dialog open={confirmClear} onOpenChange={setConfirmClear}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Clear all logs?</DialogTitle>
            <DialogDescription>
              This permanently deletes the log file. Leads, history and exports are unaffected.
            </DialogDescription>
          </DialogHeader>
          <DialogFooter>
            <Button variant="outline" onClick={() => setConfirmClear(false)}>
              Cancel
            </Button>
            <Button
              variant="destructive"
              loading={clearLogs.isPending}
              onClick={() =>
                clearLogs.mutate(undefined, {
                  onSuccess: () => {
                    toast.success('Logs cleared.');
                    setConfirmClear(false);
                  },
                  onError: (error) => toast.error(error.message),
                })
              }
            >
              Clear logs
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </>
  );
}
