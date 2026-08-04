'use client';

import * as React from 'react';
import { useRouter, useSearchParams } from 'next/navigation';
import { ChevronLeft, ChevronRight, ExternalLink, Trash2, Users, X } from 'lucide-react';
import { toast } from 'sonner';

import { PageHeader } from '@/components/shared/page-header';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { Checkbox } from '@/components/ui/checkbox';
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
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';
import { useDebouncedValue } from '@/hooks/use-debounced-value';
import { useDeletePerson, usePeople, type PersonQuery } from '@/hooks/use-people';
import type { BackendPerson } from '@/lib/backend/types';

const PAGE_SIZE = 25;

/**
 * Decision-makers found via LinkedIn people search, run as part of a search
 * job when "Enrich with LinkedIn" is turned on. Read-only besides delete —
 * this data comes from LinkedIn, not something users author here.
 */
export function PeopleView() {
  const router = useRouter();
  const searchParams = useSearchParams();
  const businessId = searchParams.get('businessId') ?? undefined;

  const [search, setSearch] = React.useState('');
  const [decisionMakersOnly, setDecisionMakersOnly] = React.useState(false);
  const [page, setPage] = React.useState(1);
  const [deleting, setDeleting] = React.useState<BackendPerson | null>(null);

  const debouncedSearch = useDebouncedValue(search, 350);

  const query = React.useMemo<PersonQuery>(
    () => ({
      search: debouncedSearch || undefined,
      isDecisionMaker: decisionMakersOnly ? true : undefined,
      businessId,
      page,
      pageSize: PAGE_SIZE,
    }),
    [debouncedSearch, decisionMakersOnly, businessId, page],
  );

  const people = usePeople(query);
  const deletePerson = useDeletePerson();

  const rows = people.data?.items ?? [];
  const total = people.data?.total ?? 0;
  const pageCount = people.data?.pageCount ?? 1;

  const updateSearch = (value: string): void => {
    setSearch(value);
    setPage(1);
  };

  return (
    <>
      <PageHeader
        title="People"
        description="Decision-makers found via LinkedIn people search, linked to your leads."
      />

      {people.isError && (
        <Alert variant="destructive" className="mb-6">
          <AlertDescription>{people.error.message}</AlertDescription>
        </Alert>
      )}

      {businessId && (
        <button
          type="button"
          onClick={() => router.push('/people')}
          className="mb-4 inline-flex items-center gap-1.5 rounded-full border border-dashed border-border px-3 py-1 text-xs text-muted-foreground transition-colors hover:border-primary hover:text-primary"
        >
          Filtered to one company
          <X className="size-3" />
        </button>
      )}

      <Card className="p-3">
        <div className="mb-3 flex flex-wrap items-center gap-3">
          <Input
            value={search}
            onChange={(event) => updateSearch(event.target.value)}
            placeholder="Search by name, company or title…"
            className="min-w-52 flex-1"
          />
          <label className="flex cursor-pointer items-center gap-2 text-sm text-muted-foreground">
            <Checkbox
              checked={decisionMakersOnly}
              onCheckedChange={(value) => {
                setDecisionMakersOnly(value === true);
                setPage(1);
              }}
            />
            Decision-makers only
          </label>
        </div>

        <TableContainer>
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Name</TableHead>
                <TableHead>Title</TableHead>
                <TableHead>Company</TableHead>
                <TableHead>Location</TableHead>
                <TableHead>Role</TableHead>
                <TableHead className="w-14 text-right">Actions</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {people.isPending ? (
                <TableRow>
                  <TableCell colSpan={6} className="py-10">
                    <div className="space-y-2">
                      <Skeleton className="h-4 w-3/4" />
                      <Skeleton className="h-4 w-1/2" />
                      <Skeleton className="h-4 w-2/3" />
                    </div>
                  </TableCell>
                </TableRow>
              ) : rows.length === 0 ? (
                <TableRow>
                  <TableCell colSpan={6}>
                    <EmptyState
                      icon={<Users />}
                      title="No people found"
                      description="Run a search with “Enrich with LinkedIn” turned on to find decision-makers at your leads' companies."
                      className="my-2 border-0"
                    />
                  </TableCell>
                </TableRow>
              ) : (
                rows.map((person) => (
                  <TableRow key={person.id}>
                    <TableCell>
                      <p className="font-medium">{person.fullName}</p>
                      {person.headline && (
                        <p className="max-w-[240px] truncate text-xs text-muted-foreground">
                          {person.headline}
                        </p>
                      )}
                    </TableCell>
                    <TableCell className="text-sm">{person.jobTitle || '—'}</TableCell>
                    <TableCell className="text-sm">{person.companyName || '—'}</TableCell>
                    <TableCell className="text-sm text-muted-foreground">
                      {person.location || '—'}
                    </TableCell>
                    <TableCell>
                      {person.isDecisionMaker ? (
                        <Badge variant="success">{person.decisionMakerRole || 'Decision-maker'}</Badge>
                      ) : (
                        <Badge variant="muted">—</Badge>
                      )}
                    </TableCell>
                    <TableCell className="text-right">
                      <div className="flex justify-end gap-1">
                        {person.linkedInUrl && (
                          <Button variant="ghost" size="icon-sm" aria-label="Open LinkedIn profile" asChild>
                            <a href={person.linkedInUrl} target="_blank" rel="noopener noreferrer">
                              <ExternalLink />
                            </a>
                          </Button>
                        )}
                        <Button
                          variant="ghost"
                          size="icon-sm"
                          aria-label="Delete person"
                          onClick={() => setDeleting(person)}
                        >
                          <Trash2 />
                        </Button>
                      </div>
                    </TableCell>
                  </TableRow>
                ))
              )}
            </TableBody>
          </Table>
        </TableContainer>

        <div className="mt-3 flex flex-wrap items-center justify-between gap-3 px-1">
          <span className="text-sm text-muted-foreground">{total} total</span>
          <div className="flex items-center gap-2">
            <Button
              variant="outline"
              size="sm"
              disabled={page <= 1 || people.isPending}
              onClick={() => setPage((current) => Math.max(1, current - 1))}
            >
              <ChevronLeft />
              Previous
            </Button>
            <span className="text-sm text-muted-foreground">
              Page {page} of {pageCount}
            </span>
            <Button
              variant="outline"
              size="sm"
              disabled={page >= pageCount || people.isPending}
              onClick={() => setPage((current) => current + 1)}
            >
              Next
              <ChevronRight />
            </Button>
          </div>
        </div>
      </Card>

      <Dialog open={!!deleting} onOpenChange={(open) => !open && setDeleting(null)}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Delete {deleting?.fullName}?</DialogTitle>
            <DialogDescription>This removes them from your People list.</DialogDescription>
          </DialogHeader>
          <DialogFooter className="mt-6">
            <Button variant="outline" onClick={() => setDeleting(null)}>
              Cancel
            </Button>
            <Button
              variant="destructive"
              loading={deletePerson.isPending}
              onClick={() => {
                if (!deleting) return;

                deletePerson.mutate(deleting.id, {
                  onSuccess: () => {
                    toast.success('Person deleted');
                    setDeleting(null);
                  },
                  onError: (error) => toast.error(error.message),
                });
              }}
            >
              Delete
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </>
  );
}
