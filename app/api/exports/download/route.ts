import type { NextRequest } from 'next/server';

import { fail } from '@/lib/api/response';
import { backendRequest, problemMessage } from '@/lib/backend/server';
import type { BackendBusiness, BackendPagedResult, BackendProblem } from '@/lib/backend/types';
import { getTemplateColumns } from '@/services/export/templates';
import { UTF8_BOM, toCsv } from '@/utils/csv';

export const dynamic = 'force-dynamic';

/** Rows fetched per page while paging the whole result set. */
const PAGE_SIZE = 500;

/** Hard ceiling, so one request cannot try to stream an unbounded table. */
const MAX_ROWS = 50_000;

/**
 * Builds and streams an export of the caller's leads.
 *
 * Runs entirely server-side: it pages the .NET API with the caller's bearer
 * token, so the file can only ever contain rows that user is allowed to see, and
 * the browser never assembles a spreadsheet or holds the full dataset. Excel is
 * an output format here — the data itself lives in SQL Server.
 */
export async function GET(request: NextRequest): Promise<Response> {
  const params = request.nextUrl.searchParams;

  const format = params.get('format') === 'csv' ? 'csv' : 'xlsx';
  const template = params.get('template') ?? 'leadmine';

  // Forward only the filters the API understands; anything else is ignored.
  const filters: Record<string, string> = {};
  for (const key of ['search', 'kind', 'status', 'source', 'category', 'country', 'city']) {
    const value = params.get(key);
    if (value) filters[key] = value;
  }

  const rows: BackendBusiness[] = [];
  let page = 1;

  try {
    for (;;) {
      const { response } = await backendRequest('/api/businesses', {
        query: { ...filters, page, pageSize: PAGE_SIZE, sortBy: 'createdAt', sortDir: 'desc' },
      });

      if (!response.ok) {
        const problem = (await response.json().catch(() => null)) as BackendProblem | null;
        return fail(
          problemMessage(problem, 'Could not read leads for export.'),
          'export_failed',
          response.status,
        );
      }

      const body = (await response.json()) as BackendPagedResult<BackendBusiness>;
      rows.push(...body.items);

      if (rows.length >= Math.min(body.total, MAX_ROWS)) break;
      if (page >= body.pageCount) break;
      page += 1;
    }
  } catch (error) {
    console.error('[export] failed to read leads:', error);
    return fail('Could not reach the API to build the export.', 'backend_unreachable', 502);
  }

  if (rows.length === 0) {
    return fail('Nothing matches these filters, so there is nothing to export.', 'empty_export', 400);
  }

  const columns = getTemplateColumns(template as 'leadmine' | 'hubspot' | 'salesforce');
  const stamp = new Date().toISOString().replace(/[:.]/g, '-').slice(0, 19);
  const fileName = `leadmine-${template}-${stamp}.${format}`;

  if (format === 'csv') {
    const csv =
      UTF8_BOM +
      toCsv(
        columns.map((column) => column.header),
        rows.map((row) => columns.map((column) => column.value(toRecord(row)))),
      );

    return new Response(csv, {
      headers: {
        'Content-Type': 'text/csv; charset=utf-8',
        'Content-Disposition': `attachment; filename="${fileName}"`,
        'Cache-Control': 'no-store',
      },
    });
  }

  // ExcelJS is heavy and Node-only, so it is imported lazily — a CSV export
  // should not pay for loading it.
  const { ExcelJS, styleSheet } = await import('@/repositories/excel/workbook');

  const workbook = new ExcelJS.Workbook();
  workbook.creator = 'LeadMine AI';
  workbook.created = new Date();

  const sheet = workbook.addWorksheet('Leads');
  styleSheet(
    sheet,
    columns.map((column, index) => ({
      header: column.header,
      key: `c${index}`,
      width: column.width,
    })),
  );

  for (const row of rows) {
    const record = toRecord(row);
    const cells: Record<string, string | number | null> = {};
    columns.forEach((column, index) => {
      cells[`c${index}`] = column.value(record);
    });
    sheet.addRow(cells);
  }

  const buffer = await workbook.xlsx.writeBuffer();

  return new Response(new Uint8Array(buffer as ArrayBuffer), {
    headers: {
      'Content-Type': 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
      'Content-Disposition': `attachment; filename="${fileName}"`,
      'Cache-Control': 'no-store',
    },
  });
}

/**
 * Adapts an API lead onto the shape the export column templates expect.
 *
 * The templates were written against the local record type, whose enums are
 * lowercase strings; the API sends PascalCase names. Mapping here keeps one set
 * of column definitions rather than a second, near-identical copy.
 */
function toRecord(row: BackendBusiness): Parameters<
  ReturnType<typeof getTemplateColumns>[number]['value']
>[0] {
  return {
    id: row.id,
    name: row.name ?? '',
    category: row.category ?? '',
    country: row.country ?? '',
    state: row.state ?? '',
    city: row.city ?? '',
    address: row.address ?? '',
    phone: row.phone ?? '',
    website: row.website ?? '',
    email: row.email ?? '',
    emailStatus: (row.emailStatus ?? 'Unverified').toLowerCase() as never,
    whatsapp: row.whatsApp ?? '',
    whatsappStatus: (row.whatsAppStatus ?? 'Unverified').toLowerCase() as never,
    facebook: row.facebook ?? '',
    instagram: row.instagram ?? '',
    linkedin: row.linkedIn ?? '',
    latitude: row.latitude ?? null,
    longitude: row.longitude ?? null,
    rating: row.rating ?? null,
    reviewCount: row.reviewCount ?? null,
    mapsUrl: row.mapsUrl ?? '',
    source: (row.source ?? 'manual') as never,
    dateAdded: row.createdAt ?? '',
    status: (row.status ?? 'new') as never,
    notes: row.notes ?? '',
  };
}
