import 'server-only';

import fs from 'node:fs/promises';

import ExcelJS from 'exceljs';

import { ensureDataDir } from '@/lib/paths';

export const HEADER_FILL = 'FF1E293B';
export const HEADER_FONT = 'FFF8FAFC';

export interface SheetColumn {
  header: string;
  key: string;
  width: number;
}

/**
 * ExcelJS returns cell values as strings, numbers, dates, or rich objects
 * (hyperlinks, formulas, rich text). Flatten all of them to a plain string.
 */
export function cellToString(value: ExcelJS.CellValue): string {
  if (value === null || value === undefined) return '';
  if (typeof value === 'string') return value.trim();
  if (typeof value === 'number' || typeof value === 'boolean') return String(value);
  if (value instanceof Date) return value.toISOString();

  if (typeof value === 'object') {
    if ('text' in value && typeof value.text === 'string') return value.text.trim();
    if ('hyperlink' in value && typeof value.hyperlink === 'string') return value.hyperlink.trim();
    if ('richText' in value && Array.isArray(value.richText)) {
      return value.richText.map((part) => part.text).join('').trim();
    }
    if ('result' in value) return cellToString(value.result as ExcelJS.CellValue);
    if ('error' in value) return '';
  }

  return String(value).trim();
}

export function cellToNumber(value: ExcelJS.CellValue): number | null {
  if (value === null || value === undefined || value === '') return null;
  if (typeof value === 'number') return Number.isFinite(value) ? value : null;

  const parsed = Number.parseFloat(cellToString(value));
  return Number.isFinite(parsed) ? parsed : null;
}

/** Applies the shared header styling, freeze pane, and autofilter. */
export function styleSheet(sheet: ExcelJS.Worksheet, columns: SheetColumn[]): void {
  sheet.columns = columns.map((c) => ({ header: c.header, key: c.key, width: c.width }));

  const header = sheet.getRow(1);
  header.font = { bold: true, color: { argb: HEADER_FONT }, size: 11 };
  header.fill = { type: 'pattern', pattern: 'solid', fgColor: { argb: HEADER_FILL } };
  header.alignment = { vertical: 'middle', horizontal: 'left' };
  header.height = 22;
  header.commit();

  sheet.views = [{ state: 'frozen', ySplit: 1 }];
  sheet.autoFilter = {
    from: { row: 1, column: 1 },
    to: { row: 1, column: columns.length },
  };
}

export async function fileExists(filePath: string): Promise<boolean> {
  try {
    await fs.access(filePath);
    return true;
  } catch {
    return false;
  }
}

/**
 * Loads a workbook, creating an empty one with a styled sheet when the file is
 * missing or unreadable.
 */
export async function loadOrCreateWorkbook(
  filePath: string,
  sheetName: string,
  columns: SheetColumn[],
): Promise<{ workbook: ExcelJS.Workbook; sheet: ExcelJS.Worksheet; created: boolean }> {
  await ensureDataDir();
  const workbook = new ExcelJS.Workbook();
  workbook.creator = 'LeadMine AI';
  workbook.lastModifiedBy = 'LeadMine AI';

  if (await fileExists(filePath)) {
    try {
      await workbook.xlsx.readFile(filePath);
      const existing = workbook.getWorksheet(sheetName) ?? workbook.worksheets[0];
      if (existing) {
        // Re-apply column keys so `row.getCell(key)` works on a sheet that was
        // last written by Excel itself (which drops ExcelJS key metadata).
        existing.columns = columns.map((c) => ({ header: c.header, key: c.key, width: c.width }));
        return { workbook, sheet: existing, created: false };
      }
    } catch (error) {
      console.error(`[excel] could not read ${filePath}, recreating:`, error);
    }
  }

  const sheet = workbook.addWorksheet(sheetName, {
    views: [{ state: 'frozen', ySplit: 1 }],
  });
  styleSheet(sheet, columns);
  return { workbook, sheet, created: true };
}

/** Writes to a temp path then renames, so readers never see a half-written file. */
export async function saveWorkbookAtomic(
  workbook: ExcelJS.Workbook,
  filePath: string,
): Promise<void> {
  await ensureDataDir();
  const temp = `${filePath}.${process.pid}.tmp`;
  await workbook.xlsx.writeFile(temp);
  await fs.rename(temp, filePath);
}

export { ExcelJS };
