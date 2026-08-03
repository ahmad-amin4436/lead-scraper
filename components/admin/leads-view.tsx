'use client';

import * as React from 'react';
import {
  AtSign,
  Globe,
  MapPin,
  MoreHorizontal,
  Phone,
  Plus,
  Search,
  Trash2,
  Users as UsersIcon,
} from 'lucide-react';
import { useForm } from 'react-hook-form';
import { toast } from 'sonner';

import {
  BackendSourceBadge,
  BackendStatusBadge,
} from '@/components/admin/status-badges';
import { PageHeader } from '@/components/shared/page-header';
import { useAuth } from '@/components/providers/auth-provider';
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
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu';
import { Input, Textarea } from '@/components/ui/input';
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
import {
  useBackendBusinesses,
  useBackendBusinessStats,
  useCreateBackendBusiness,
  useDeleteBackendBusiness,
  useDeleteBackendBusinesses,
  useUpdateBackendBusiness,
  type BackendBusinessQuery,
  type CreateBackendBusinessInput,
} from '@/hooks/use-leads';
import { useDebouncedValue } from '@/hooks/use-debounced-value';
import { ApiClientError } from '@/lib/api-client';
import type { BackendBusiness, BackendSource } from '@/lib/backend/types';
import { formatNumber } from '@/utils/format';

const ANY = '__any__';
const SOURCES: BackendSource[] = ['Manual', 'GooglePlaces', 'OpenStreetMap'];

function errorMessage(error: unknown, fallback: string): string {
  return error instanceof ApiClientError ? error.message : fallback;
}

interface LeadFormProps {
  initial?: BackendBusiness | null;
  submitting: boolean;
  error: string | null;
  onCancel: () => void;
  onSubmit: (values: CreateBackendBusinessInput) => void;
}

function LeadForm({ initial, submitting, error, onCancel, onSubmit }: LeadFormProps) {
  const {
    register,
    handleSubmit,
    setValue,
    watch,
  } = useForm<CreateBackendBusinessInput>({
    defaultValues: {
      name: initial?.name ?? '',
      category: initial?.category ?? '',
      country: initial?.country ?? '',
      state: initial?.state ?? '',
      city: initial?.city ?? '',
      address: initial?.address ?? '',
      phone: initial?.phone ?? '',
      website: initial?.website ?? '',
      email: initial?.email ?? '',
      whatsApp: initial?.whatsApp ?? '',
      facebook: initial?.facebook ?? '',
      instagram: initial?.instagram ?? '',
      linkedIn: initial?.linkedIn ?? '',
      rating: initial?.rating ?? null,
      reviewCount: initial?.reviewCount ?? null,
      notes: initial?.notes ?? '',
      source: initial?.source ?? 'Manual',
    },
  });

  const source = watch('source');

  return (
    <form onSubmit={handleSubmit(onSubmit)} noValidate className="space-y-4">
      {error && (
        <Alert variant="destructive">
          <AlertDescription>{error}</AlertDescription>
        </Alert>
      )}

      <div className="space-y-2">
        <Label htmlFor="lead-name">Business name *</Label>
        <Input id="lead-name" required {...register('name')} />
      </div>

      <div className="grid grid-cols-2 gap-3">
        <div className="space-y-2">
          <Label htmlFor="lead-category">Category</Label>
          <Input id="lead-category" {...register('category')} />
        </div>
        <div className="space-y-2">
          <Label htmlFor="lead-source">Source</Label>
          <Select value={source} onValueChange={(value) => setValue('source', value as BackendSource)}>
            <SelectTrigger id="lead-source">
              <SelectValue placeholder="Source" />
            </SelectTrigger>
            <SelectContent>
              {SOURCES.map((item) => (
                <SelectItem key={item} value={item}>
                  {item === 'GooglePlaces' ? 'Google Places' : item === 'OpenStreetMap' ? 'OpenStreetMap' : 'Manual'}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>
      </div>

      <div className="grid grid-cols-3 gap-3">
        <div className="space-y-2">
          <Label htmlFor="lead-country">Country</Label>
          <Input id="lead-country" {...register('country')} />
        </div>
        <div className="space-y-2">
          <Label htmlFor="lead-state">State</Label>
          <Input id="lead-state" {...register('state')} />
        </div>
        <div className="space-y-2">
          <Label htmlFor="lead-city">City</Label>
          <Input id="lead-city" {...register('city')} />
        </div>
      </div>

      <div className="space-y-2">
        <Label htmlFor="lead-address">Address</Label>
        <Input id="lead-address" {...register('address')} />
      </div>

      <div className="grid grid-cols-2 gap-3">
        <div className="space-y-2">
          <Label htmlFor="lead-phone">Phone</Label>
          <Input id="lead-phone" {...register('phone')} />
        </div>
        <div className="space-y-2">
          <Label htmlFor="lead-website">Website</Label>
          <Input id="lead-website" {...register('website')} />
        </div>
        <div className="space-y-2">
          <Label htmlFor="lead-email">Email</Label>
          <Input id="lead-email" type="email" {...register('email')} />
        </div>
        <div className="space-y-2">
          <Label htmlFor="lead-whatsapp">WhatsApp</Label>
          <Input id="lead-whatsapp" {...register('whatsApp')} />
        </div>
      </div>

      <div className="space-y-2">
        <Label htmlFor="lead-notes">Notes</Label>
        <Textarea id="lead-notes" {...register('notes')} />
      </div>

      <DialogFooter className="mt-2">
        <Button type="button" variant="outline" onClick={onCancel}>
          Cancel
        </Button>
        <Button type="submit" loading={submitting}>
          {initial ? 'Save changes' : 'Add lead'}
        </Button>
      </DialogFooter>
    </form>
  );
}

export function BackendLeadsView() {
  const { hasPermission } = useAuth();
  const [search, setSearch] = React.useState('');
  const [source, setSource] = React.useState<string>(ANY);
  const [status, setStatus] = React.useState<string>(ANY);
  const [page, setPage] = React.useState(1);
  const [selected, setSelected] = React.useState<Set<string>>(new Set());
  const [editing, setEditing] = React.useState<BackendBusiness | null>(null);
  const [addOpen, setAddOpen] = React.useState(false);
  const [deleting, setDeleting] = React.useState<BackendBusiness | null>(null);
  const [bulkDeleteOpen, setBulkDeleteOpen] = React.useState(false);
  const [formError, setFormError] = React.useState<string | null>(null);

  const debouncedSearch = useDebouncedValue(search, 350);

  const query = React.useMemo<BackendBusinessQuery>(
    () => ({
      search: debouncedSearch || undefined,
      source: source === ANY ? undefined : (source as BackendSource),
      status: status === ANY ? undefined : status,
      page,
      pageSize: 25,
    }),
    [debouncedSearch, source, status, page],
  );

  const businesses = useBackendBusinesses(query);
  const stats = useBackendBusinessStats();
  const createBusiness = useCreateBackendBusiness();
  const updateBusiness = useUpdateBackendBusiness();
  const deleteBusiness = useDeleteBackendBusiness();
  const deleteBusinesses = useDeleteBackendBusinesses();

  const rows = businesses.data?.items ?? [];
  const total = businesses.data?.total ?? 0;
  const pageCount = businesses.data?.pageCount ?? 1;

  function updateSearch(value: string) {
    setSearch(value);
    setPage(1);
  }

  function updateSource(value: string) {
    setSource(value);
    setPage(1);
    setSelected(new Set());
  }

  function updateStatus(value: string) {
    setStatus(value);
    setPage(1);
    setSelected(new Set());
  }

  function toggleSelected(id: string) {
    setSelected((current) => {
      const next = new Set(current);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  }

  async function handleSaveLead(values: CreateBackendBusinessInput) {
    setFormError(null);
    try {
      if (editing) {
        await updateBusiness.mutateAsync({ id: editing.id, patch: values });
        toast.success('Lead updated');
      } else {
        await createBusiness.mutateAsync(values);
        toast.success('Lead added');
      }
      setAddOpen(false);
      setEditing(null);
    } catch (error) {
      setFormError(errorMessage(error, 'Could not save the lead.'));
    }
  }

  async function handleDelete(lead: BackendBusiness) {
    try {
      await deleteBusiness.mutateAsync(lead.id);
      setDeleting(null);
      setSelected(new Set());
      toast.success('Lead deleted');
    } catch (error) {
      toast.error(errorMessage(error, 'Could not delete the lead.'));
    }
  }

  async function handleBulkDelete() {
    try {
      const result = await deleteBusinesses.mutateAsync(Array.from(selected));
      setBulkDeleteOpen(false);
      setSelected(new Set());
      toast.success(`${result.removed} lead${result.removed === 1 ? '' : 's'} deleted`);
    } catch (error) {
      toast.error(errorMessage(error, 'Could not delete the selected leads.'));
    }
  }

  const canEdit = hasPermission('leads.update') || hasPermission('leads.create');

  return (
    <>
      <PageHeader
        title="Leads"
        description="Businesses stored in the backend database."
        actions={
          hasPermission('leads.create') ? (
            <Button
              onClick={() => {
                setEditing(null);
                setFormError(null);
                setAddOpen(true);
              }}
            >
              <Plus />
              Add lead
            </Button>
          ) : undefined
        }
      />

      <div className="mb-4 grid grid-cols-2 gap-3 sm:grid-cols-4">
        <StatCard label="Total leads" value={stats.data?.total} />
        <StatCard label="With email" value={stats.data?.withEmail} />
        <StatCard label="With phone" value={stats.data?.withPhone} />
        <StatCard label="Enriched" value={stats.data?.enriched} />
      </div>

      <Card className="p-3">
        <div className="mb-3 flex flex-wrap items-center gap-3">
          <div className="relative min-w-52 flex-1">
            <Search className="absolute left-3 top-1/2 size-4 -translate-y-1/2 text-muted-foreground" />
            <Input
              value={search}
              onChange={(event) => updateSearch(event.target.value)}
              placeholder="Search by name, category or city…"
              className="pl-9"
            />
          </div>
          <Select value={source} onValueChange={updateSource}>
            <SelectTrigger className="w-44">
              <SelectValue placeholder="Source" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value={ANY}>All sources</SelectItem>
              {SOURCES.map((item) => (
                <SelectItem key={item} value={item}>
                  {item === 'GooglePlaces' ? 'Google Places' : item === 'OpenStreetMap' ? 'OpenStreetMap' : 'Manual'}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
          <Select value={status} onValueChange={updateStatus}>
            <SelectTrigger className="w-44">
              <SelectValue placeholder="Status" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value={ANY}>All statuses</SelectItem>
              <SelectItem value="New">New</SelectItem>
              <SelectItem value="Enriched">Enriched</SelectItem>
              <SelectItem value="Partial">Partial</SelectItem>
              <SelectItem value="NoWebsite">No website</SelectItem>
              <SelectItem value="EnrichmentFailed">Enrich failed</SelectItem>
            </SelectContent>
          </Select>
          {selected.size > 0 && hasPermission('leads.delete') && (
            <Button variant="destructive" size="sm" onClick={() => setBulkDeleteOpen(true)}>
              <Trash2 />
              Delete {selected.size}
            </Button>
          )}
        </div>

        <TableContainer>
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead className="w-10">
                  <span className="sr-only">Select</span>
                </TableHead>
                <TableHead>Business</TableHead>
                <TableHead>Location</TableHead>
                <TableHead>Contacts</TableHead>
                <TableHead>Source</TableHead>
                <TableHead>Status</TableHead>
                <TableHead className="w-14 text-right">Actions</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {businesses.isPending ? (
                <TableRow>
                  <TableCell colSpan={7} className="py-10">
                    <div className="space-y-2">
                      <Skeleton className="h-4 w-3/4" />
                      <Skeleton className="h-4 w-1/2" />
                      <Skeleton className="h-4 w-2/3" />
                    </div>
                  </TableCell>
                </TableRow>
              ) : rows.length === 0 ? (
                <TableRow>
                  <TableCell colSpan={7}>
                    <EmptyState
                      icon={<UsersIcon />}
                      title="No leads found"
                      description="Try different filters, or add the first lead."
                      action={
                        hasPermission('leads.create') ? (
                          <Button
                            onClick={() => {
                              setEditing(null);
                              setFormError(null);
                              setAddOpen(true);
                            }}
                          >
                            <Plus />
                            Add lead
                          </Button>
                        ) : undefined
                      }
                      className="my-2 border-0"
                    />
                  </TableCell>
                </TableRow>
              ) : (
                rows.map((lead) => (
                  <TableRow key={lead.id}>
                    <TableCell>
                      <Checkbox
                        checked={selected.has(lead.id)}
                        onCheckedChange={() => toggleSelected(lead.id)}
                        aria-label={`Select ${lead.name}`}
                      />
                    </TableCell>
                    <TableCell>
                      <p className="max-w-64 truncate font-medium">{lead.name}</p>
                      <p className="max-w-64 truncate text-xs text-muted-foreground">
                        {lead.category || 'No category'}
                      </p>
                    </TableCell>
                    <TableCell className="text-muted-foreground">
                      <p className="flex items-center gap-1.5">
                        <MapPin className="size-3.5" />
                        <span className="truncate">{formatLocation(lead)}</span>
                      </p>
                    </TableCell>
                    <TableCell>
                      <div className="flex flex-col gap-0.5 text-xs">
                        {lead.email ? (
                          <span className="flex items-center gap-1.5">
                            <AtSign className="size-3.5 text-muted-foreground" />
                            <span className="max-w-40 truncate">{lead.email}</span>
                          </span>
                        ) : null}
                        {lead.phone ? (
                          <span className="flex items-center gap-1.5">
                            <Phone className="size-3.5 text-muted-foreground" />
                            <span>{lead.phone}</span>
                          </span>
                        ) : null}
                        {lead.website ? (
                          <span className="flex items-center gap-1.5">
                            <Globe className="size-3.5 text-muted-foreground" />
                            <span className="max-w-40 truncate">{lead.website}</span>
                          </span>
                        ) : null}
                      </div>
                    </TableCell>
                    <TableCell>
                      <BackendSourceBadge source={lead.source} />
                    </TableCell>
                    <TableCell>
                      <BackendStatusBadge status={lead.status} />
                    </TableCell>
                    <TableCell className="text-right">
                      {canEdit && (
                        <DropdownMenu>
                          <DropdownMenuTrigger asChild>
                            <Button variant="ghost" size="icon-sm" aria-label="Lead actions">
                              <MoreHorizontal />
                            </Button>
                          </DropdownMenuTrigger>
                          <DropdownMenuContent align="end">
                            {hasPermission('leads.update') && (
                              <DropdownMenuItem
                                onSelect={() => {
                                  setEditing(lead);
                                  setFormError(null);
                                  setAddOpen(true);
                                }}
                              >
                                Edit
                              </DropdownMenuItem>
                            )}
                            <DropdownMenuSeparator />
                            {hasPermission('leads.delete') && (
                              <DropdownMenuItem destructive onSelect={() => setDeleting(lead)}>
                                Delete
                              </DropdownMenuItem>
                            )}
                          </DropdownMenuContent>
                        </DropdownMenu>
                      )}
                    </TableCell>
                  </TableRow>
                ))
              )}
            </TableBody>
          </Table>
        </TableContainer>

        <div className="mt-3 flex flex-wrap items-center justify-between gap-3 px-1">
          <span className="text-sm text-muted-foreground">
            {formatNumber(total)} total · {formatNumber((businesses.data?.items ?? []).length)} shown
          </span>
          <div className="flex items-center gap-2">
            <Button
              variant="outline"
              size="sm"
              disabled={page <= 1 || businesses.isPending}
              onClick={() => setPage((current) => Math.max(1, current - 1))}
            >
              Previous
            </Button>
            <span className="text-sm text-muted-foreground">
              Page {page} of {pageCount}
            </span>
            <Button
              variant="outline"
              size="sm"
              disabled={page >= pageCount || businesses.isPending}
              onClick={() => setPage((current) => current + 1)}
            >
              Next
            </Button>
          </div>
        </div>
      </Card>

      <Dialog open={addOpen} onOpenChange={(open) => !open && setAddOpen(false)}>
        <DialogContent className="max-w-xl">
          <DialogHeader>
            <DialogTitle>{editing ? 'Edit lead' : 'Add lead'}</DialogTitle>
            <DialogDescription>
              {editing
                ? `Update ${editing.name}.`
                : 'Manually add a business to the backend database.'}
            </DialogDescription>
          </DialogHeader>
          <LeadForm
            initial={editing}
            submitting={createBusiness.isPending || updateBusiness.isPending}
            error={formError}
            onCancel={() => setAddOpen(false)}
            onSubmit={(values) => void handleSaveLead(values)}
          />
        </DialogContent>
      </Dialog>

      <Dialog open={!!deleting} onOpenChange={(open) => !open && setDeleting(null)}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Delete lead</DialogTitle>
            <DialogDescription>
              This removes {deleting?.name} from the database and cannot be undone.
            </DialogDescription>
          </DialogHeader>
          <DialogFooter className="mt-6">
            <Button type="button" variant="outline" onClick={() => setDeleting(null)}>
              Cancel
            </Button>
            <Button
              type="button"
              variant="destructive"
              onClick={() => deleting && handleDelete(deleting)}
              loading={deleteBusiness.isPending}
            >
              Delete lead
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      <Dialog open={bulkDeleteOpen} onOpenChange={setBulkDeleteOpen}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Delete {selected.size} leads?</DialogTitle>
            <DialogDescription>
              The selected leads are removed from the backend database. This cannot be undone.
            </DialogDescription>
          </DialogHeader>
          <DialogFooter className="mt-6">
            <Button type="button" variant="outline" onClick={() => setBulkDeleteOpen(false)}>
              Cancel
            </Button>
            <Button
              type="button"
              variant="destructive"
              onClick={() => void handleBulkDelete()}
              loading={deleteBusinesses.isPending}
            >
              Delete leads
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </>
  );
}

function formatLocation(lead: BackendBusiness): string {
  const parts = [lead.city, lead.state, lead.country].filter(Boolean);
  return parts.length > 0 ? parts.join(', ') : '—';
}

function StatCard({ label, value }: { label: string; value?: number }) {
  return (
    <Card>
      <CardContent className="flex flex-col gap-1 p-4">
        <span className="text-xs text-muted-foreground">{label}</span>
        <span className="text-2xl font-semibold tracking-tight">
          {value === undefined ? '—' : formatNumber(value)}
        </span>
      </CardContent>
    </Card>
  );
}
