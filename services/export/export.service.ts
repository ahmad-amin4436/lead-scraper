import 'server-only';

import fs from 'node:fs/promises';
import path from 'node:path';

import { AppError, toErrorMessage } from '@/lib/errors';
import { EXPORTS_DIR, ensureDataDir } from '@/lib/paths';
import { businessRepository } from '@/repositories/business.repository';
import { exportRepository } from '@/repositories/export.repository';
import type { BusinessRecord } from '@/types/business';
import type { ExportRecord, ExportRequest } from '@/types/export';
import { UTF8_BOM, toCsv } from '@/utils/csv';
import { createId } from '@/utils/id';
import { ExcelJS, saveWorkbookAtomic, styleSheet } from '@/repositories/excel/workbook';
import { logger } from '../logging/logger.service';
import { getTemplateColumns } from './templates';

export interface ExportResult {
  record: ExportRecord;
  /** Absolute path of the written file. */
  filePath: string;
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

    await ensureDataDir();

    const columns = getTemplateColumns(request.template);
    const stamp = new Date().toISOString().replace(/[:.]/g, '-').slice(0, 19);
    const fileName = `leadmine-${request.template}-${stamp}-${createId()}.${request.format}`;
    const filePath = path.join(EXPORTS_DIR, fileName);

    try {
      if (request.format === 'xlsx') {
        await this.writeXlsx(filePath, rows, columns);
      } else {
        await this.writeCsv(filePath, rows, columns);
      }

      const stats = await fs.stat(filePath);

      const record: ExportRecord = {
        id: createId('exp'),
        fileName,
        format: request.format,
        template: request.template,
        rowCount: rows.length,
        byteSize: stats.size,
        createdAt: new Date().toISOString(),
        filters: request.filters,
      };

      await exportRepository.add(record);
      await logger.info('export.created', `Exported ${rows.length} record(s) as ${request.format.toUpperCase()}`, {
        elapsedMs: Date.now() - startedAt,
        context: { format: request.format, template: request.template, rows: rows.length },
      });

      return { record, filePath };
    } catch (error) {
      // Don't leave a partial file behind for a failed export.
      await fs.rm(filePath, { force: true }).catch(() => undefined);
      await logger.error('export.failed', `Export failed: ${toErrorMessage(error)}`, {
        context: { format: request.format, template: request.template },
      });
      throw error;
    }
  }

  private async writeXlsx(
    filePath: string,
    rows: readonly BusinessRecord[],
    columns: ReturnType<typeof getTemplateColumns>,
  ): Promise<void> {
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

    await saveWorkbookAtomic(workbook, filePath);
  }

  private async writeCsv(
    filePath: string,
    rows: readonly BusinessRecord[],
    columns: ReturnType<typeof getTemplateColumns>,
  ): Promise<void> {
    const headers = columns.map((column) => column.header);
    const body = rows.map((record) => columns.map((column) => column.value(record)));
    const csv = UTF8_BOM + toCsv(headers, body);

    const temp = `${filePath}.tmp`;
    await fs.writeFile(temp, csv, 'utf8');
    await fs.rename(temp, filePath);
  }

  /** Reads a previously generated export back for download. */
  async read(id: string): Promise<{ record: ExportRecord; buffer: Buffer } | null> {
    const record = await exportRepository.getById(id);
    if (!record) return null;

    const filePath = exportRepository.resolvePath(record.fileName);
    if (!filePath) return null;

    try {
      const buffer = await fs.readFile(filePath);
      return { record, buffer };
    } catch {
      return null;
    }
  }
}

export const exportService = new ExportService();
