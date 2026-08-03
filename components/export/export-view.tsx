'use client';

import * as React from 'react';
import { Download, FileSpreadsheet, FileText, Loader2, TriangleAlert } from 'lucide-react';
import { toast } from 'sonner';

import { PageHeader } from '@/components/shared/page-header';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Alert, AlertDescription, Separator, Skeleton } from '@/components/ui/misc';
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
import { useBackendBusinesses, useBackendBusinessStats } from '@/hooks/use-leads';
import { useDebouncedValue } from '@/hooks/use-debounced-value';
import { LEAD_KIND_OPTIONS } from '@/lib/constants/lead-kinds';
import { cn } from '@/lib/utils';
import { formatNumber } from '@/utils/format';

const ANY = '__any__';
const PREVIEW_ROWS = 10;

type ExportFormat = 'xlsx' | 'csv';
type ExportTemplate = 'leadmine' | 'hubspot' | 'salesforce';

const TEMPLATES: { value: ExportTemplate; label: string; hint: string }[] = [
  {
    value: 'leadmine',
    label: 'LeadMine — every field',
    hint: 'The complete record, including verification statuses.',
  },
  {
    value: 'hubspot',
    label: 'HubSpot — company import',
    hint: 'Column names match HubSpot company properties.',
  },
  {
    value: 'salesforce',
    label: 'Salesforce — lead import',
    hint: 'Column names match Salesforce standard Lead fields.',
  },
];

/**
 * Export straight from this page.
 *
 * The file is generated server-side from SQL and streamed back, so nothing is
 * assembled in the browser and the export always reflects exactly the rows the
 * signed-in user is allowed to see. Filters mirror the Leads screen, and the
 * preview shows what will actually be written before anything downloads.
 */
export function ExportView() {
  const [format, setFormat] = React.useState<ExportFormat>('xlsx');
  const [template, setTemplate] = React.useState<ExportTemplate>('leadmine');
  const [kind, setKind] = React.useState('Any');
  const [status, setStatus] = React.useState(ANY);
  const [search, setSearch] = React.useState('');
  const [downloading, setDownloading] = React.useState(false);

  const debouncedSearch = useDebouncedValue(search, 350);

  const filters = React.useMemo(
    () => ({
      search: debouncedSearch || undefined,
      kind: kind === 'Any' ? undefined : kind,
      status: status === ANY ? undefined : status,
    }),
    [debouncedSearch, kind, status],
  );

  const stats = useBackendBusinessStats();

  // Doubles as the row-count check and the preview.
  const preview = useBackendBusinesses({
    ...filters,
    page: 1,
    pageSize: PREVIEW_ROWS,
    sortBy: 'createdAt',
    sortDir: 'desc',
  });

  const matching = preview.data?.total ?? 0;
  const rows = preview.data?.items ?? [];

  /**
   * Streams the generated file. Uses fetch + blob rather than navigating to the
   * URL so the session cookie is sent, errors surface as a toast instead of a
   * blank tab, and the button can show real progress.
   */
  const download = async (): Promise<void> => {
    if (matching === 0) return;

    setDownloading(true);

    try {
      const params = new URLSearchParams({ format, template });
      for (const [key, value] of Object.entries(filters)) {
        if (value !== undefined) params.set(key, String(value));
      }

      const response = await fetch(`/api/exports/download?${params.toString()}`);

      if (!response.ok) {
        const problem = (await response.json().catch(() => null)) as
          | { error?: { message?: string } }
          | null;
        throw new Error(problem?.error?.message ?? `Export failed (HTTP ${response.status}).`);
      }

      const blob = await response.blob();
      const disposition = response.headers.get('Content-Disposition') ?? '';
      const suggested = /filename="?([^"]+)"?/.exec(disposition)?.[1];

      const url = URL.createObjectURL(blob);
      const anchor = document.createElement('a');
      anchor.href = url;
      anchor.download = suggested ?? `leads.${format}`;
      document.body.appendChild(anchor);
      anchor.click();
      anchor.remove();
      // Release the object URL once the download has been handed to the browser.
      URL.revokeObjectURL(url);

      toast.success(`Exported ${formatNumber(matching)} lead(s).`);
    } catch (error) {
      toast.error(error instanceof Error ? error.message : 'Export failed.');
    } finally {
      setDownloading(false);
    }
  };

  return (
    <>
      <PageHeader
        title="Export"
        description="Download your leads as Excel or CSV, ready for a CRM import."
      />

      <div className="grid gap-6 lg:grid-cols-[minmax(0,1fr)_minmax(0,1.3fr)]">
        <Card className="h-fit">
          <CardHeader>
            <CardTitle>Build the file</CardTitle>
            <CardDescription>
              {stats.isPending
                ? 'Loading your leads…'
                : `${formatNumber(stats.data?.total ?? 0)} lead(s) in your database.`}
            </CardDescription>
          </CardHeader>
          <CardContent className="space-y-5">
            <div className="space-y-2">
              <Label>Format</Label>
              <div className="grid grid-cols-2 gap-2">
                {(['xlsx', 'csv'] as const).map((option) => {
                  const Icon = option === 'xlsx' ? FileSpreadsheet : FileText;
                  const active = format === option;

                  return (
                    <button
                      key={option}
                      type="button"
                      onClick={() => setFormat(option)}
                      aria-pressed={active}
                      className={cn(
                        'flex items-center gap-2 rounded-lg border p-3 text-left text-sm transition-colors',
                        active
                          ? 'border-primary bg-primary/8 font-medium text-primary'
                          : 'border-border hover:bg-accent',
                      )}
                    >
                      <Icon className="size-4 shrink-0" />
                      <span>
                        {option === 'xlsx' ? 'Excel' : 'CSV'}
                        <span className="block text-xs font-normal text-muted-foreground">
                          .{option}
                        </span>
                      </span>
                    </button>
                  );
                })}
              </div>
            </div>

            <div className="space-y-2">
              <Label htmlFor="template">Column layout</Label>
              <Select
                value={template}
                onValueChange={(next) => setTemplate(next as ExportTemplate)}
              >
                <SelectTrigger id="template">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {TEMPLATES.map((option) => (
                    <SelectItem key={option.value} value={option.value}>
                      {option.label}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
              <p className="text-xs text-muted-foreground">
                {TEMPLATES.find((t) => t.value === template)?.hint}
              </p>
            </div>

            <Separator />

            <div className="space-y-4">
              <p className="text-sm font-medium">Which leads?</p>

              <div className="space-y-2">
                <Label htmlFor="exportKind">Lead type</Label>
                <Select value={kind} onValueChange={setKind}>
                  <SelectTrigger id="exportKind">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    {LEAD_KIND_OPTIONS.map((option) => (
                      <SelectItem key={option.value} value={option.value}>
                        {option.label}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
                <p className="text-xs text-muted-foreground">
                  {LEAD_KIND_OPTIONS.find((o) => o.value === kind)?.description}
                </p>
              </div>

              <div className="space-y-2">
                <Label htmlFor="exportStatus">Status</Label>
                <Select value={status} onValueChange={setStatus}>
                  <SelectTrigger id="exportStatus">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value={ANY}>Any status</SelectItem>
                    <SelectItem value="New">New</SelectItem>
                    <SelectItem value="Enriched">Enriched</SelectItem>
                    <SelectItem value="Partial">Partial</SelectItem>
                    <SelectItem value="NoWebsite">No website</SelectItem>
                    <SelectItem value="EnrichmentFailed">Enrich failed</SelectItem>
                  </SelectContent>
                </Select>
              </div>

              <div className="space-y-2">
                <Label htmlFor="exportSearch">Search</Label>
                <Input
                  id="exportSearch"
                  value={search}
                  onChange={(event) => setSearch(event.target.value)}
                  placeholder="Name, email, city…"
                />
              </div>
            </div>

            <Button
              className="w-full"
              onClick={() => void download()}
              disabled={downloading || matching === 0 || preview.isPending}
            >
              {downloading ? <Loader2 className="animate-spin" /> : <Download />}
              {downloading
                ? 'Preparing…'
                : `Download ${formatNumber(matching)} lead(s)`}
            </Button>

            {matching === 0 && !preview.isPending && (
              <Alert variant="warning">
                <TriangleAlert />
                <AlertDescription>
                  Nothing matches these filters, so there is nothing to export.
                </AlertDescription>
              </Alert>
            )}
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle>Preview</CardTitle>
            <CardDescription>
              The first {PREVIEW_ROWS} rows of {formatNumber(matching)} that will be exported.
            </CardDescription>
          </CardHeader>
          <CardContent className="px-0 pb-0">
            {preview.isError && (
              <div className="px-5 pb-5">
                <Alert variant="destructive">
                  <AlertDescription>{preview.error.message}</AlertDescription>
                </Alert>
              </div>
            )}

            {preview.isPending ? (
              <div className="space-y-2 px-5 pb-5">
                {Array.from({ length: 6 }, (_, index) => (
                  <Skeleton key={index} className="h-10 w-full" />
                ))}
              </div>
            ) : rows.length === 0 ? (
              <p className="px-5 pb-5 text-sm text-muted-foreground">
                No rows match the current filters.
              </p>
            ) : (
              <TableContainer>
                <Table>
                  <TableHeader>
                    <TableRow>
                      <TableHead className="pl-5">Business</TableHead>
                      <TableHead>Email</TableHead>
                      <TableHead>Phone</TableHead>
                      <TableHead className="pr-5">Location</TableHead>
                    </TableRow>
                  </TableHeader>
                  <TableBody>
                    {rows.map((row) => (
                      <TableRow key={row.id}>
                        <TableCell className="pl-5">
                          <p className="max-w-[180px] truncate text-sm font-medium">{row.name}</p>
                          <p className="max-w-[180px] truncate text-xs text-muted-foreground">
                            {row.category}
                          </p>
                        </TableCell>
                        <TableCell className="max-w-[200px] truncate text-sm">
                          {row.email || <span className="text-muted-foreground">—</span>}
                        </TableCell>
                        <TableCell className="whitespace-nowrap text-sm">
                          {row.phone || <span className="text-muted-foreground">—</span>}
                        </TableCell>
                        <TableCell className="pr-5 text-sm text-muted-foreground">
                          {[row.city, row.country].filter(Boolean).join(', ') || '—'}
                        </TableCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
              </TableContainer>
            )}
          </CardContent>
        </Card>
      </div>
    </>
  );
}
