'use client';

import * as React from 'react';
import Link from 'next/link';
import {
  AtSign,
  ChevronLeft,
  ChevronRight,
  Database,
  Download,
  ExternalLink,
  Filter,
  Search,
  Star,
  Trash2,
  X,
} from 'lucide-react';
import { toast } from 'sonner';

import { PageHeader } from '@/components/shared/page-header';
import { BusinessStatusBadge } from '@/components/shared/status-badge';
import { Button } from '@/components/ui/button';
import { Card, CardContent } from '@/components/ui/card';
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
import { useBusinesses, useDeleteBusinesses } from '@/hooks/use-api';
import { useDebouncedValue } from '@/hooks/use-debounced-value';
import { BUSINESS_CATEGORIES } from '@/lib/constants/categories';
import { BUSINESS_STATUSES, type BusinessQuery, type BusinessStatus } from '@/types/business';
import { formatDate, formatNumber } from '@/utils/format';

const ANY = '__any__';
const PAGE_SIZES = [25, 50, 100, 200];

interface Filters {
  search: string;
  category: string;
  status: string;
  hasEmail: boolean;
  minRating: string;
}

const INITIAL_FILTERS: Filters = {
  search: '',
  category: ANY,
  status: ANY,
  hasEmail: false,
  minRating: '',
};

export function DatabaseView() {
  const [filters, setFilters] = React.useState<Filters>(INITIAL_FILTERS);
  const [page, setPage] = React.useState(1);
  const [pageSize, setPageSize] = React.useState(25);
  const [selected, setSelected] = React.useState<Set<string>>(new Set());
  const [confirmOpen, setConfirmOpen] = React.useState(false);

  const { search, category, status, hasEmail, minRating } = filters;

  const debouncedSearch = useDebouncedValue(search, 350);
  const debouncedRating = useDebouncedValue(minRating, 350);

  /**
   * Filter changes reset pagination and selection together. Doing this in the
   * change handler rather than an effect avoids a second render pass where the
   * old page is briefly requested against the new filters.
   */
  const updateFilter = React.useCallback(
    <K extends keyof Filters>(key: K, value: Filters[K]): void => {
      setFilters((current) => ({ ...current, [key]: value }));
      setPage(1);
      setSelected(new Set());
    },
    [],
  );

  const changePageSize = (next: number): void => {
    setPageSize(next);
    setPage(1);
    setSelected(new Set());
  };

  const query = React.useMemo<BusinessQuery>(() => {
    const parsedRating = Number.parseFloat(debouncedRating);

    return {
      search: debouncedSearch || undefined,
      category: category === ANY ? undefined : category,
      status: status === ANY ? undefined : (status as BusinessStatus),
      hasEmail: hasEmail ? true : undefined,
      minRating: Number.isFinite(parsedRating) ? parsedRating : undefined,
      page,
      pageSize,
      sortBy: 'dateAdded',
      sortDir: 'desc',
    };
  }, [debouncedSearch, category, status, hasEmail, debouncedRating, page, pageSize]);

  const businesses = useBusinesses(query);
  const deleteBusinesses = useDeleteBusinesses();

  const rows = businesses.data?.rows ?? [];
  const total = businesses.data?.total ?? 0;
  const pageCount = businesses.data?.pageCount ?? 1;

  const allOnPageSelected = rows.length > 0 && rows.every((row) => selected.has(row.id));
  const someOnPageSelected = rows.some((row) => selected.has(row.id));

  const toggleAll = (): void => {
    setSelected((current) => {
      const next = new Set(current);
      if (allOnPageSelected) {
        for (const row of rows) next.delete(row.id);
      } else {
        for (const row of rows) next.add(row.id);
      }
      return next;
    });
  };

  const toggleOne = (id: string): void => {
    setSelected((current) => {
      const next = new Set(current);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  };

  const handleDelete = (): void => {
    const ids = [...selected];

    deleteBusinesses.mutate(ids, {
      onSuccess: ({ removed }) => {
        toast.success(`Deleted ${removed} record(s).`);
        setSelected(new Set());
        setConfirmOpen(false);
      },
      onError: (error) => toast.error(error.message),
    });
  };

  const resetFilters = (): void => {
    setFilters(INITIAL_FILTERS);
    setPage(1);
    setSelected(new Set());
  };

  const filtersActive =
    Boolean(search) || category !== ANY || status !== ANY || hasEmail || Boolean(minRating);

  return (
    <>
      <PageHeader
        title="Excel database"
        description="Every lead saved to /database/Businesses.xlsx."
        actions={
          <Button asChild variant="outline">
            <Link href="/export">
              <Download />
              Export
            </Link>
          </Button>
        }
      />

      <Card className="mb-6">
        <CardContent className="space-y-4 p-5">
          <div className="grid gap-3 lg:grid-cols-[minmax(0,2fr)_repeat(3,minmax(0,1fr))]">
            <div className="relative">
              <Search className="pointer-events-none absolute left-2.5 top-1/2 size-4 -translate-y-1/2 text-muted-foreground" />
              <Input
                value={search}
                onChange={(event) => updateFilter('search', event.target.value)}
                placeholder="Search name, email, city, website…"
                className="pl-8"
                aria-label="Search leads"
              />
            </div>

            <Select value={category} onValueChange={(next) => updateFilter('category', next)}>
              <SelectTrigger aria-label="Filter by category">
                <SelectValue placeholder="All categories" />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value={ANY}>All categories</SelectItem>
                {BUSINESS_CATEGORIES.map((item) => (
                  <SelectItem key={item.id} value={item.label}>
                    {item.label}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>

            <Select value={status} onValueChange={(next) => updateFilter('status', next)}>
              <SelectTrigger aria-label="Filter by status">
                <SelectValue placeholder="All statuses" />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value={ANY}>All statuses</SelectItem>
                {BUSINESS_STATUSES.map((item) => (
                  <SelectItem key={item} value={item}>
                    {item}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>

            <Input
              type="number"
              min={0}
              max={5}
              step={0.1}
              value={minRating}
              onChange={(event) => updateFilter('minRating', event.target.value)}
              placeholder="Min rating"
              aria-label="Minimum rating"
            />
          </div>

          <div className="flex flex-wrap items-center justify-between gap-3">
            <label className="flex cursor-pointer items-center gap-2 text-sm">
              <Checkbox
                checked={hasEmail}
                onCheckedChange={(value) => updateFilter('hasEmail', value === true)}
              />
              <AtSign className="size-3.5 text-muted-foreground" />
              Has email only
            </label>

            <div className="flex items-center gap-2">
              {filtersActive && (
                <Button variant="ghost" size="sm" onClick={resetFilters}>
                  <X />
                  Clear filters
                </Button>
              )}
              <span className="text-xs tabular-nums text-muted-foreground">
                {formatNumber(total)} record{total === 1 ? '' : 's'}
              </span>
            </div>
          </div>
        </CardContent>
      </Card>

      {selected.size > 0 && (
        <div className="mb-4 flex flex-wrap items-center justify-between gap-3 rounded-lg border border-primary/25 bg-primary/8 px-4 py-2.5">
          <p className="text-sm font-medium">
            {formatNumber(selected.size)} selected
          </p>
          <div className="flex gap-2">
            <Button variant="ghost" size="sm" onClick={() => setSelected(new Set())}>
              Clear selection
            </Button>
            <Button variant="destructive" size="sm" onClick={() => setConfirmOpen(true)}>
              <Trash2 />
              Delete
            </Button>
          </div>
        </div>
      )}

      {businesses.isError && (
        <Alert variant="destructive" className="mb-6">
          <AlertDescription>{businesses.error.message}</AlertDescription>
        </Alert>
      )}

      <Card>
        {businesses.isPending ? (
          <CardContent className="space-y-2 p-5">
            {Array.from({ length: 8 }, (_, index) => (
              <Skeleton key={index} className="h-11 w-full" />
            ))}
          </CardContent>
        ) : rows.length === 0 ? (
          <CardContent className="p-5">
            <EmptyState
              icon={filtersActive ? <Filter /> : <Database />}
              title={filtersActive ? 'No matches' : 'Database is empty'}
              description={
                filtersActive
                  ? 'Try loosening the filters.'
                  : 'Run a search to start collecting leads.'
              }
              action={
                filtersActive ? (
                  <Button size="sm" variant="outline" onClick={resetFilters}>
                    Clear filters
                  </Button>
                ) : (
                  <Button size="sm" asChild>
                    <Link href="/search">Start a search</Link>
                  </Button>
                )
              }
            />
          </CardContent>
        ) : (
          <>
            <TableContainer>
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead className="w-10 pl-4">
                      <Checkbox
                        checked={
                          allOnPageSelected ? true : someOnPageSelected ? 'indeterminate' : false
                        }
                        onCheckedChange={toggleAll}
                        aria-label="Select all on this page"
                      />
                    </TableHead>
                    <TableHead>Business</TableHead>
                    <TableHead>Contact</TableHead>
                    <TableHead>Location</TableHead>
                    <TableHead>Rating</TableHead>
                    <TableHead>Status</TableHead>
                    <TableHead className="pr-4">Added</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {rows.map((row) => (
                    <TableRow key={row.id} data-state={selected.has(row.id) ? 'selected' : undefined}>
                      <TableCell className="pl-4">
                        <Checkbox
                          checked={selected.has(row.id)}
                          onCheckedChange={() => toggleOne(row.id)}
                          aria-label={`Select ${row.name}`}
                        />
                      </TableCell>

                      <TableCell>
                        <p className="max-w-[240px] truncate font-medium">{row.name}</p>
                        <p className="max-w-[240px] truncate text-xs text-muted-foreground">
                          {row.category}
                        </p>
                      </TableCell>

                      <TableCell>
                        <div className="space-y-0.5 text-xs">
                          {row.email ? (
                            <a
                              href={`mailto:${row.email}`}
                              className="block max-w-[220px] truncate text-success hover:underline"
                            >
                              {row.email}
                            </a>
                          ) : (
                            <span className="text-muted-foreground">No email</span>
                          )}
                          {row.phone && <p className="text-muted-foreground">{row.phone}</p>}
                          {row.website && (
                            <a
                              href={row.website}
                              target="_blank"
                              rel="noopener noreferrer nofollow"
                              className="inline-flex max-w-[220px] items-center gap-1 truncate text-muted-foreground hover:text-foreground hover:underline"
                            >
                              <span className="truncate">{row.website.replace(/^https?:\/\//, '')}</span>
                              <ExternalLink className="size-3 shrink-0" />
                            </a>
                          )}
                        </div>
                      </TableCell>

                      <TableCell>
                        <p className="max-w-[180px] truncate text-sm">{row.city || '—'}</p>
                        <p className="max-w-[180px] truncate text-xs text-muted-foreground">
                          {row.country}
                        </p>
                      </TableCell>

                      <TableCell>
                        {row.rating !== null ? (
                          <span className="flex items-center gap-1 text-sm tabular-nums">
                            <Star className="size-3.5 fill-warning text-warning" />
                            {row.rating.toFixed(1)}
                            <span className="text-xs text-muted-foreground">
                              ({formatNumber(row.reviewCount ?? 0)})
                            </span>
                          </span>
                        ) : (
                          <span className="text-xs text-muted-foreground">—</span>
                        )}
                      </TableCell>

                      <TableCell>
                        <BusinessStatusBadge status={row.status} />
                      </TableCell>

                      <TableCell className="whitespace-nowrap pr-4 text-xs text-muted-foreground">
                        {formatDate(row.dateAdded)}
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </TableContainer>

            <div className="flex flex-wrap items-center justify-between gap-3 border-t border-border px-4 py-3">
              <div className="flex items-center gap-2">
                <Label htmlFor="pageSize" className="text-xs text-muted-foreground">
                  Rows per page
                </Label>
                <Select value={String(pageSize)} onValueChange={(next) => changePageSize(Number(next))}>
                  <SelectTrigger id="pageSize" className="h-8 w-20">
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

      <Dialog open={confirmOpen} onOpenChange={setConfirmOpen}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Delete {formatNumber(selected.size)} record(s)?</DialogTitle>
            <DialogDescription>
              This removes the rows from Businesses.xlsx permanently. Previously generated export
              files are not affected.
            </DialogDescription>
          </DialogHeader>
          <DialogFooter>
            <Button variant="outline" onClick={() => setConfirmOpen(false)}>
              Cancel
            </Button>
            <Button
              variant="destructive"
              onClick={handleDelete}
              loading={deleteBusinesses.isPending}
            >
              <Trash2 />
              Delete permanently
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </>
  );
}
