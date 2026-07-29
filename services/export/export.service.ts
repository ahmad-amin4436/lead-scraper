import '@/lib/server-guard';

import { AppError, toErrorMessage } from '@/lib/errors';
import { exportFileKey } from '@/lib/paths';
import { blobStore } from '@/lib/storage/blob-store';
import { businessRepository } from '@/repositories/business.repository';
import { exportRepository } from '@/repositories/export.repository';
import { ExcelJS, styleSheet } from '@/repositories/excel/workbook';
import type { BusinessRecord } from '@/types/business';
import type { ExportRecord, ExportRequest } from '@/types/export';
import { UTF8_BOM, toCsv } from '@/utils/csv';
import { createId } from '@/utils/id';
import { logger } from '../logging/logger.service';
import { getTemplateColumns } from './templates';

export interface ExportResult {
  record: ExportRecord;
}

const MIME_TYPES = {
  xlsx: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
  csv: 'text/csv; charset=utf-8',
} as const;

export function mimeTypeFor(format: 'xlsx' | 'csv'): string {
  return MIME_TYPES[format];
}

class ExportService {
  /** Resolves the rows to export: explicit ids take precedence over filters. */
  private async collect(request: ExportRequest): Promise<BusinessRecord[]> {
    if (request.ids && request.ids.length > 0) {
      return businessRepository.getByIds(request.ids);
    }
    return businessRepository.queryAll(request.filters);
  }

  async create(request: ExportRequest): Promise<ExportResult> {
    const startedAt = Date.now();
    const rows = await this.collect(request);

    if (rows.length === 0) {
      throw new AppError('Nothing to export — no records match the current filters.', 'empty_export', 400);
    }

    const columns = getTemplateColumns(request.template);
    const stamp = new Date().toISOString().replace(/[:.]/g, '-').slice(0, 19);
    const fileName = `leadmine-${request.template}-${stamp}-${createId()}.${request.format}`;

    try {
      const buffer =
        request.format === 'xlsx'
          ? await this.buildXlsx(rows, columns)
          : this.buildCsv(rows, columns);

      // Store the generated file in blob storage so it survives across instances
      // and can be downloaded later.
      await blobStore.setBuffer(exportFileKey(fileName), buffer);

      const record: ExportRecord = {
        id: createId('exp'),
        fileName,
        format: request.format,
        template: request.template,
        rowCount: rows.length,
        byteSize: buffer.byteLength,
        createdAt: new Date().toISOString(),
        filters: request.filters,
      };

      await exportRepository.add(record);
      await logger.info('export.created', `Exported ${rows.length} record(s) as ${request.format.toUpperCase()}`, {
        elapsedMs: Date.now() - startedAt,
        context: { format: request.format, template: request.template, rows: rows.length },
      });

      return { record };
    } catch (error) {
      // Don't leave a partial file behind for a failed export.
      await blobStore.delete(exportFileKey(fileName)).catch(() => undefined);
      await logger.error('export.failed', `Export failed: ${toErrorMessage(error)}`, {
        context: { format: request.format, template: request.template },
      });
      throw error;
    }
  }

  private async buildXlsx(
    rows: readonly BusinessRecord[],
    columns: ReturnType<typeof getTemplateColumns>,
  ): Promise<Buffer> {
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

    for (const record of rows) {
      const row: Record<string, string | number | null> = {};
      columns.forEach((column, index) => {
        row[`c${index}`] = column.value(record);
      });
      sheet.addRow(row);
    }

    const arrayBuffer = await workbook.xlsx.writeBuffer();
    return Buffer.from(arrayBuffer as ArrayBuffer);
  }

  private buildCsv(
    rows: readonly BusinessRecord[],
    columns: ReturnType<typeof getTemplateColumns>,
  ): Buffer {
    const headers = columns.map((column) => column.header);
    const body = rows.map((record) => columns.map((column) => column.value(record)));
    const csv = UTF8_BOM + toCsv(headers, body);
    return Buffer.from(csv, 'utf8');
  }

  /** Reads a previously generated export back for download. */
  async read(id: string): Promise<{ record: ExportRecord; buffer: Buffer } | null> {
    const record = await exportRepository.getById(id);
    if (!record) return null;

    const buffer = await blobStore.getBuffer(exportFileKey(record.fileName));
    if (!buffer) return null;
    return { record, buffer };
  }
}

export const exportService = new ExportService();
