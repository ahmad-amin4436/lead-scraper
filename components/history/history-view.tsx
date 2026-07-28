'use client';

import * as React from 'react';
import { useRouter } from 'next/navigation';
import { FileClock, RotateCw, Trash2 } from 'lucide-react';
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
import { Alert, AlertDescription, EmptyState, Skeleton } from '@/components/ui/misc';
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

export function HistoryView() {
  const router = useRouter();
  const history = useHistory();
  const deleteEntry = useDeleteHistoryEntry();
  const clearHistory = useClearHistory();
  const [confirmClear, setConfirmClear] = React.useState(false);

  const entries = history.data ?? [];

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
          entries.length > 0 ? (
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
                          onClick={() =>
                            deleteEntry.mutate(entry.id, {
                              onSuccess: () => toast.success('History entry removed.'),
                              onError: (error) => toast.error(error.message),
                            })
                          }
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
        )}
      </Card>

      <Dialog open={confirmClear} onOpenChange={setConfirmClear}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Clear search history?</DialogTitle>
            <DialogDescription>
              This removes all {formatNumber(entries.length)} history entries. Your saved leads in
              the Excel database are not affected.
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
