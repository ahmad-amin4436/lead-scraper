/**
 * Characters that make a spreadsheet treat a cell as a formula. A value
 * beginning with one is prefixed with an apostrophe so exported data can never
 * execute when the file is opened (CSV injection).
 */
const FORMULA_PREFIXES = ['=', '+', '-', '@', '\t', '\r'];

function escapeCell(value: string | number | null | undefined): string {
  if (value === null || value === undefined) return '';

  let text = String(value);

  if (text.length > 0 && FORMULA_PREFIXES.includes(text[0])) {
    text = `'${text}`;
  }

  if (/[",\n\r]/.test(text)) {
    return `"${text.replace(/"/g, '""')}"`;
  }

  return text;
}

/** Builds an RFC 4180 CSV document. */
export function toCsv(
  headers: readonly string[],
  rows: readonly (string | number | null | undefined)[][],
): string {
  const lines = [headers.map(escapeCell).join(',')];

  for (const row of rows) {
    lines.push(row.map(escapeCell).join(','));
  }

  return lines.join('\r\n');
}

/** UTF-8 BOM so Excel detects the encoding of non-ASCII exports. */
export const UTF8_BOM = '﻿';
