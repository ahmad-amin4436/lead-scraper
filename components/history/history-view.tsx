'use client';

import * as React from 'react';
import { useRouter } from 'next/navigation';
import { ChevronLeft, ChevronRight, FileClock, RotateCw, Trash2 } from 'lucide-react';
import { toast } from 'sonner';

import { PageHeader } from '@/components/shared/page-header';
import { JobStatusBadge } from '@/components/shared/status-badge';
import { Badge } from '@/components/ui/badge';
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
import { useClearHistory, useDeleteHistoryEntry, useHistory } from '@/hooks/use-api';
import { getCategoryLabel } from '@/lib/constants/categories';
import { getCountryName } from '@/lib/constants/locations';
import type { SearchHistoryEntry } from '@/types/search';
import { formatDateTime, formatDuration, formatNumber } from '@/utils/format';

const PAGE_SIZES = [25, 50, 100, 200];

export function HistoryView() {
  const router = useRouter();
  const [page, setPage] = React.useState(1);
  const [pageSize, setPageSize] = React.useState(25);
  const history = useHistory({ page, pageSize });
  const deleteEntry = useDeleteHistoryEntry();
  const clearHistory = useClearHistory();
  const [confirmClear, setConfirmClear] = React.useState(false);

  const entries = history.data?.items ?? [];
  const total = history.data?.total ?? 0;
  const pageCount = history.data?.pageCount ?? 1;

  const changePageSize = (next: number): void => {
    setPageSize(next);
    setPage(1);
  };

  /**
   * Deletes an entry, stepping back a page first if it's the last one on a
   * page beyond the first — otherwise the view would be stranded looking at a
   * page that just became empty even though earlier pages still have entries.
   * Handled here, at the point of the action that can cause it, rather than
   * reactively watching the data for the condition.
   */
  const deleteEntryAndAdjustPage = (entry: SearchHistoryEntry): void => {
    const wasLastOnPage = entries.length === 1 && page > 1;

    deleteEntry.mutate(entry.id, {
      onSuccess: () => {
        toast.success('History entry removed.');
        if (wasLastOnPage) setPage((current) => Math.max(1, current - 1));
      },
      onError: (error) => toast.error(error.message),
    });
  };

  /** Re-opens the search form prefilled with this run's parameters. */
  const rerun = (entry: SearchHistoryEntry): void => {
    const payload = encodeURIComponent(JSON.stringify(entry.request));
    router.push(`/search?rerun=${payload}`);
  };

  return (
    <>
      <PageHeader
        title="Search history"
        description="Every run, with its counters and a one-click rerun."
        actions={
          total > 0 ? (
            <Button variant="outline" onClick={() => setConfirmClear(true)}>
              <Trash2 />
              Clear history
            </Button>
          ) : null
        }
      />

      {history.isError && (
        <Alert variant="destructive" className="mb-6">
          <AlertDescription>{history.error.message}</AlertDescription>
        </Alert>
      )}

      <Card>
        {history.isPending ? (
          <CardContent className="space-y-2 p-5">
            {Array.from({ length: 5 }, (_, index) => (
              <Skeleton key={index} className="h-14 w-full" />
            ))}
          </CardContent>
        ) : entries.length === 0 ? (
          <CardContent className="p-5">
            <EmptyState
              icon={<FileClock />}
              title="No searches yet"
              description="Once you run a search it will be recorded here, ready to repeat."
              action={
                <Button size="sm" onClick={() => router.push('/search')}>
                  Start a search
                </Button>
              }
            />
          </CardContent>
        ) : (
          <>
            <TableContainer>
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead className="pl-5">Search</TableHead>
                    <TableHead>Results</TableHead>
                    <TableHead>Duration</TableHead>
                    <TableHead>Started</TableHead>
                    <TableHead>Status</TableHead>
                    <TableHead className="pr-5 text-right">Actions</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {entries.map((entry) => (
                    <TableRow key={entry.id}>
                      <TableCell className="pl-5">
                        <div className="max-w-[320px] space-y-1">
                          <div className="flex flex-wrap gap-1">
                            {entry.request.categories.slice(0, 3).map((id) => (
                              <Badge key={id} variant="secondary">
                                {getCategoryLabel(id)}
                              </Badge>
                            ))}
                            {entry.request.categories.length > 3 && (
                              <Badge variant="muted">
                                +{entry.request.categories.length - 3}
                              </Badge>
                            )}
                          </div>
                          <p className="truncate text-xs text-muted-foreground">
                            {entry.request.cities.join(', ')} ·{' '}
                            {getCountryName(entry.request.country)}
                          </p>
                        </div>
                      </TableCell>

                      <TableCell>
                        <div className="space-y-0.5 text-xs">
                          <p>
                            <span className="font-medium tabular-nums text-foreground">
                              {formatNumber(entry.saved)}
                            </span>{' '}
                            saved
                          </p>
                          <p className="text-muted-foreground">
                            {formatNumber(entry.found)} found · {formatNumber(entry.duplicates)} dup ·{' '}
                            {formatNumber(entry.enriched)} enriched
                          </p>
                        </div>
                      </TableCell>

                      <TableCell className="whitespace-nowrap text-sm tabular-nums">
                        {formatDuration(entry.elapsedMs)}
                      </TableCell>

                      <TableCell className="whitespace-nowrap text-xs text-muted-foreground">
                        {formatDateTime(entry.startedAt)}
                      </TableCell>

                      <TableCell>
                        <JobStatusBadge status={entry.status} />
                        {entry.error && (
                          <p className="mt-1 max-w-[200px] truncate text-xs text-destructive" title={entry.error}>
                            {entry.error}
                          </p>
                        )}
                      </TableCell>

                      <TableCell className="pr-5">
                        <div className="flex justify-end gap-1">
                          <Button
                            variant="outline"
                            size="sm"
                            onClick={() => rerun(entry)}
                            title="Rerun this search"
                          >
                            <RotateCw />
                            Rerun
                          </Button>
                          <Button
                            variant="ghost"
                            size="icon-sm"
                            onClick={() => deleteEntryAndAdjustPage(entry)}
                            aria-label="Delete history entry"
                          >
                            <Trash2 />
                          </Button>
                        </div>
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </TableContainer>

            <div className="flex flex-wrap items-center justify-between gap-3 border-t border-border px-4 py-3">
              <div className="flex items-center gap-2">
                <Label htmlFor="historyPageSize" className="text-xs text-muted-foreground">
                  Rows per page
                </Label>
                <Select value={String(pageSize)} onValueChange={(next) => changePageSize(Number(next))}>
                  <SelectTrigger id="historyPageSize" className="h-8 w-20">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    {PAGE_SIZES.map((size) => (
                      <SelectItem key={size} value={String(size)}>
                        {size}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </div>

              <div className="flex items-center gap-3">
                <span className="text-xs tabular-nums text-muted-foreground">
                  Page {page} of {pageCount}
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
            </div>
          </>
        )}
      </Card>

      <Dialog open={confirmClear} onOpenChange={setConfirmClear}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Clear search history?</DialogTitle>
            <DialogDescription>
              This removes all {formatNumber(total)} history entries. Your saved leads are not
              affected.
            </DialogDescription>
          </DialogHeader>
          <DialogFooter>
            <Button variant="outline" onClick={() => setConfirmClear(false)}>
              Cancel
            </Button>
            <Button
              variant="destructive"
              loading={clearHistory.isPending}
              onClick={() =>
                clearHistory.mutate(undefined, {
                  onSuccess: () => {
                    toast.success('History cleared.');
                    setConfirmClear(false);
                    setPage(1);
                  },
                  onError: (error) => toast.error(error.message),
                })
              }
            >
              Clear history
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </>
  );
}
