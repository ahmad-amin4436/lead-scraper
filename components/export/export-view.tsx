'use client';

import * as React from 'react';
import { Download, FileSpreadsheet, FileText, Package, Trash2 } from 'lucide-react';
import { toast } from 'sonner';

import { PageHeader } from '@/components/shared/page-header';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
import { Checkbox } from '@/components/ui/checkbox';
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
import { useBusinessStats, useCreateExport, useDeleteExport, useExports } from '@/hooks/use-api';
import { BUSINESS_CATEGORIES } from '@/lib/constants/categories';
import { EXPORT_TEMPLATES, type ExportFormat, type ExportTemplate } from '@/types/export';
import { formatBytes, formatDateTime, formatNumber } from '@/utils/format';

const ANY = '__any__';

const TEMPLATE_LABELS: Record<ExportTemplate, string> = {
  leadmine: 'LeadMine — all 23 fields',
  hubspot: 'HubSpot — company import',
  salesforce: 'Salesforce — lead import',
};

const TEMPLATE_HINTS: Record<ExportTemplate, string> = {
  leadmine: 'The complete record, matching the Excel database layout.',
  hubspot: 'Column names match HubSpot company properties for a direct import.',
  salesforce: 'Column names match Salesforce standard Lead fields.',
};

export function ExportView() {
  const [format, setFormat] = React.useState<ExportFormat>('xlsx');
  const [template, setTemplate] = React.useState<ExportTemplate>('leadmine');
  const [category, setCategory] = React.useState(ANY);
  const [hasEmail, setHasEmail] = React.useState(false);
  const [minRating, setMinRating] = React.useState('');

  const stats = useBusinessStats();
  const exportsQuery = useExports();
  const createExport = useCreateExport();
  const deleteExport = useDeleteExport();

  const records = exportsQuery.data ?? [];

  const handleExport = (): void => {
    const parsedRating = Number.parseFloat(minRating);

    if (Number.isFinite(parsedRating) && (parsedRating < 0 || parsedRating > 5)) {
      toast.error('Minimum rating must be between 0 and 5.');
      return;
    }

    createExport.mutate(
      {
        format,
        template,
        filters: {
          category: category === ANY ? undefined : category,
          hasEmail: hasEmail ? true : undefined,
          minRating: Number.isFinite(parsedRating) ? parsedRating : undefined,
        },
      },
      {
        onSuccess: (record) => {
          toast.success(`Exported ${formatNumber(record.rowCount)} record(s).`);
          // Trigger the browser download immediately.
          window.location.href = `/api/exports/${record.id}`;
        },
        onError: (error) => toast.error(error.message),
      },
    );
  };

  return (
    <>
      <PageHeader
        title="Export"
        description="Generate Excel, CSV, or CRM-ready files from your lead database."
      />

      <div className="grid gap-6 lg:grid-cols-[minmax(0,1fr)_minmax(0,1.4fr)]">
        <Card className="h-fit">
          <CardHeader>
            <CardTitle>New export</CardTitle>
            <CardDescription>
              {stats.data
                ? `${formatNumber(stats.data.total)} record(s) available before filtering.`
                : 'Loading database…'}
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
                      className={`flex items-center gap-2 rounded-lg border p-3 text-left text-sm transition-colors ${
                        active
                          ? 'border-primary bg-primary/8 font-medium text-primary'
                          : 'border-border hover:bg-accent'
                      }`}
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
              <Label htmlFor="template">Column template</Label>
              <Select
                value={template}
                onValueChange={(next) => setTemplate(next as ExportTemplate)}
              >
                <SelectTrigger id="template">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {EXPORT_TEMPLATES.map((option) => (
                    <SelectItem key={option} value={option}>
                      {TEMPLATE_LABELS[option]}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
              <p className="text-xs text-muted-foreground">{TEMPLATE_HINTS[template]}</p>
            </div>

            <Separator />

            <div className="space-y-4">
              <p className="text-sm font-medium">Filters</p>

              <div className="space-y-2">
                <Label htmlFor="exportCategory">Category</Label>
                <Select value={category} onValueChange={setCategory}>
                  <SelectTrigger id="exportCategory">
                    <SelectValue />
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
              </div>

              <div className="space-y-2">
                <Label htmlFor="exportRating">Minimum rating</Label>
                <Input
                  id="exportRating"
                  type="number"
                  min={0}
                  max={5}
                  step={0.1}
                  placeholder="Any"
                  value={minRating}
                  onChange={(event) => setMinRating(event.target.value)}
                />
              </div>

              <label className="flex cursor-pointer items-center gap-2 text-sm">
                <Checkbox
                  checked={hasEmail}
                  onCheckedChange={(value) => setHasEmail(value === true)}
                />
                Only records with an email address
              </label>
            </div>

            <Button
              className="w-full"
              onClick={handleExport}
              loading={createExport.isPending}
              disabled={(stats.data?.total ?? 0) === 0}
            >
              <Download />
              Generate &amp; download
            </Button>

            {(stats.data?.total ?? 0) === 0 && (
              <p className="text-center text-xs text-muted-foreground">
                Nothing to export yet — run a search first.
              </p>
            )}
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle>Export history</CardTitle>
            <CardDescription>
              Previously generated files, re-downloadable until they are deleted.
            </CardDescription>
          </CardHeader>
          <CardContent className="px-0 pb-0">
            {exportsQuery.isError && (
              <div className="px-5 pb-5">
                <Alert variant="destructive">
                  <AlertDescription>{exportsQuery.error.message}</AlertDescription>
                </Alert>
              </div>
            )}

            {exportsQuery.isPending ? (
              <div className="space-y-2 px-5 pb-5">
                {Array.from({ length: 4 }, (_, index) => (
                  <Skeleton key={index} className="h-12 w-full" />
                ))}
              </div>
            ) : records.length === 0 ? (
              <div className="px-5 pb-5">
                <EmptyState
                  icon={<Package />}
                  title="No exports yet"
                  description="Generated files show up here so you can download them again."
                />
              </div>
            ) : (
              <TableContainer>
                <Table>
                  <TableHeader>
                    <TableRow>
                      <TableHead className="pl-5">File</TableHead>
                      <TableHead>Rows</TableHead>
                      <TableHead>Size</TableHead>
                      <TableHead>Created</TableHead>
                      <TableHead className="pr-5 text-right">Actions</TableHead>
                    </TableRow>
                  </TableHeader>
                  <TableBody>
                    {records.map((record) => (
                      <TableRow key={record.id}>
                        <TableCell className="pl-5">
                          <div className="flex items-center gap-2">
                            {record.format === 'xlsx' ? (
                              <FileSpreadsheet className="size-4 shrink-0 text-success" />
                            ) : (
                              <FileText className="size-4 shrink-0 text-muted-foreground" />
                            )}
                            <div className="min-w-0">
                              <p className="max-w-[200px] truncate text-sm font-medium">
                                {record.fileName}
                              </p>
                              <Badge variant="muted" className="mt-0.5">
                                {record.template}
                              </Badge>
                            </div>
                          </div>
                        </TableCell>
                        <TableCell className="tabular-nums">
                          {formatNumber(record.rowCount)}
                        </TableCell>
                        <TableCell className="whitespace-nowrap text-xs text-muted-foreground">
                          {formatBytes(record.byteSize)}
                        </TableCell>
                        <TableCell className="whitespace-nowrap text-xs text-muted-foreground">
                          {formatDateTime(record.createdAt)}
                        </TableCell>
                        <TableCell className="pr-5">
                          <div className="flex justify-end gap-1">
                            <Button asChild variant="outline" size="icon-sm">
                              <a href={`/api/exports/${record.id}`} download aria-label="Download">
                                <Download />
                              </a>
                            </Button>
                            <Button
                              variant="ghost"
                              size="icon-sm"
                              aria-label="Delete export"
                              onClick={() =>
                                deleteExport.mutate(record.id, {
                                  onSuccess: () => toast.success('Export deleted.'),
                                  onError: (error) => toast.error(error.message),
                                })
                              }
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
          </CardContent>
        </Card>
      </div>
    </>
  );
}
