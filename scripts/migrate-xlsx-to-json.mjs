#!/usr/bin/env node
/**
 * One-time migration: `database/Businesses.xlsx` -> `database/businesses.json`.
 *
 * The lead store moved from an Excel workbook to a JSON blob when the app was
 * ported to Netlify Blobs, but existing rows were never carried across — the app
 * reads only `businesses.json`, leaving everything in the workbook orphaned.
 * This imports them.
 *
 * Safe to run more than once: records already present (matched on website host,
 * phone, or name-within-city) are skipped rather than duplicated, and the
 * existing JSON is backed up before anything is written.
 *
 *   node scripts/migrate-xlsx-to-json.mjs [--dry-run]
 */

import fs from 'node:fs/promises';
import path from 'node:path';
import process from 'node:process';

import ExcelJS from 'exceljs';

const DATA_DIR = process.env.LEADMINE_DATA_DIR
  ? path.resolve(process.env.LEADMINE_DATA_DIR)
  : path.join(process.cwd(), 'database');

const XLSX_PATH = path.join(DATA_DIR, 'Businesses.xlsx');
const JSON_PATH = path.join(DATA_DIR, 'businesses.json');
const DRY_RUN = process.argv.includes('--dry-run');

const COMBINING_MARKS = /[̀-ͯ]/g;

function normalizeText(value) {
  return String(value ?? '')
    .normalize('NFKD')
    .replace(COMBINING_MARKS, '')
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, ' ')
    .trim();
}

function normalizeHost(input) {
  try {
    const url = String(input).includes('://')
      ? new URL(String(input))
      : new URL(`https://${input}`);
    return url.hostname.toLowerCase().replace(/^www\./, '');
  } catch {
    return '';
  }
}

/** Same identity rules the repository uses, so this agrees with runtime dedupe. */
function dedupeKeys(record) {
  const keys = [];

  const host = record.website ? normalizeHost(record.website) : '';
  if (host) keys.push(`web:${host}`);

  const digits = String(record.phone ?? '').replace(/\D/g, '');
  if (digits.length >= 7) keys.push(`tel:${digits.slice(-10)}`);

  const name = normalizeText(record.name);
  if (name) keys.push(`name:${name}|${normalizeText(record.city || record.country)}`);

  return keys;
}

function cellToString(value) {
  if (value === null || value === undefined) return '';
  if (typeof value === 'string') return value.trim();
  if (typeof value === 'number' || typeof value === 'boolean') return String(value);
  if (value instanceof Date) return value.toISOString();

  if (typeof value === 'object') {
    if (typeof value.text === 'string') return value.text.trim();
    if (typeof value.hyperlink === 'string') return value.hyperlink.trim();
    if (Array.isArray(value.richText)) return value.richText.map((p) => p.text).join('').trim();
    if ('result' in value) return cellToString(value.result);
  }

  return String(value).trim();
}

function cellToNumber(value) {
  if (value === null || value === undefined || value === '') return null;
  if (typeof value === 'number') return Number.isFinite(value) ? value : null;
  const parsed = Number.parseFloat(cellToString(value));
  return Number.isFinite(parsed) ? parsed : null;
}

function createId() {
  return `biz_${Date.now().toString(36)}${Math.random().toString(36).slice(2, 10)}`;
}

async function readWorkbook() {
  const workbook = new ExcelJS.Workbook();
  await workbook.xlsx.readFile(XLSX_PATH);

  const sheet = workbook.getWorksheet('Businesses') ?? workbook.worksheets[0];
  if (!sheet) throw new Error('No worksheet found in Businesses.xlsx');

  // Map by header text so a reordered sheet still imports correctly.
  const headerToIndex = new Map();
  sheet.getRow(1).eachCell((cell, col) => {
    headerToIndex.set(cellToString(cell.value).toLowerCase(), col);
  });

  const rows = [];

  sheet.eachRow((row, rowNumber) => {
    if (rowNumber === 1) return;

    const read = (header) => {
      const col = headerToIndex.get(header.toLowerCase());
      return col === undefined ? '' : row.getCell(col).value;
    };

    const name = cellToString(read('Business Name'));
    if (!name) return;

    rows.push({
      id: cellToString(read('ID')) || createId(),
      name,
      category: cellToString(read('Category')),
      country: cellToString(read('Country')),
      state: cellToString(read('State')),
      city: cellToString(read('City')),
      address: cellToString(read('Address')),
      phone: cellToString(read('Phone')),
      website: cellToString(read('Website')),
      email: cellToString(read('Email')),
      emailStatus: cellToString(read('Email Status')) || 'unverified',
      whatsapp: cellToString(read('WhatsApp')),
      whatsappStatus: cellToString(read('WhatsApp Status')) || 'unverified',
      facebook: cellToString(read('Facebook')),
      instagram: cellToString(read('Instagram')),
      linkedin: cellToString(read('LinkedIn')),
      latitude: cellToNumber(read('Latitude')),
      longitude: cellToNumber(read('Longitude')),
      rating: cellToNumber(read('Google Rating')),
      reviewCount: cellToNumber(read('Review Count')),
      mapsUrl: cellToString(read('Maps URL')),
      source: cellToString(read('Source')) || 'manual',
      dateAdded: cellToString(read('Date Added')) || new Date().toISOString(),
      status: cellToString(read('Status')) || 'new',
      notes: cellToString(read('Notes')),
    });
  });

  return rows;
}

async function readExistingJson() {
  try {
    const parsed = JSON.parse(await fs.readFile(JSON_PATH, 'utf8'));
    if (Array.isArray(parsed)) return parsed;
    if (Array.isArray(parsed?.rows)) return parsed.rows;
    if (Array.isArray(parsed?.records)) return parsed.records;
    return [];
  } catch (error) {
    if (error.code === 'ENOENT') return [];
    throw error;
  }
}

async function main() {
  console.log(`Data directory : ${DATA_DIR}`);

  try {
    await fs.access(XLSX_PATH);
  } catch {
    console.log('No Businesses.xlsx found — nothing to migrate.');
    return;
  }

  const [workbookRows, existing] = await Promise.all([readWorkbook(), readExistingJson()]);

  console.log(`Businesses.xlsx: ${workbookRows.length} row(s)`);
  console.log(`businesses.json: ${existing.length} row(s)`);

  /*
   * Content-key dedupe runs ONLY against records already in businesses.json.
   *
   * Workbook rows are not compared with each other: they were each accepted as a
   * distinct lead when first saved, and the name-within-city rule would now
   * collapse legitimately separate branches of a chain in the same city. A
   * migration must preserve what the user already has, not re-filter it.
   */
  const existingKeys = new Set();
  const seenIds = new Set();
  for (const record of existing) {
    seenIds.add(record.id);
    for (const key of dedupeKeys(record)) existingKeys.add(key);
  }

  const merged = [...existing];
  let imported = 0;
  let skipped = 0;

  for (const record of workbookRows) {
    // A row already carried over by an earlier run of this script.
    if (seenIds.has(record.id)) {
      skipped += 1;
      continue;
    }

    if (dedupeKeys(record).some((key) => existingKeys.has(key))) {
      skipped += 1;
      continue;
    }

    seenIds.add(record.id);
    merged.push(record);
    imported += 1;
  }

  console.log(`\nWould import : ${imported}`);
  console.log(`Duplicates   : ${skipped}`);
  console.log(`New total    : ${merged.length}`);

  if (DRY_RUN) {
    console.log('\n--dry-run: nothing written.');
    return;
  }

  if (imported === 0) {
    console.log('\nNothing new to import; leaving businesses.json untouched.');
    return;
  }

  if (existing.length > 0) {
    const backup = `${JSON_PATH}.bak-${Date.now()}`;
    await fs.copyFile(JSON_PATH, backup);
    console.log(`\nBacked up existing store to ${path.basename(backup)}`);
  }

  const temp = `${JSON_PATH}.tmp`;
  await fs.writeFile(temp, JSON.stringify(merged, null, 2), 'utf8');
  await fs.rename(temp, JSON_PATH);

  console.log(`Wrote ${merged.length} record(s) to businesses.json`);
}

main().catch((error) => {
  console.error('Migration failed:', error);
  process.exit(1);
});
