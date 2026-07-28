import type { BusinessQuery } from './business';

export const EXPORT_FORMATS = ['xlsx', 'csv'] as const;
export type ExportFormat = (typeof EXPORT_FORMATS)[number];

/** Column mapping presets so exports drop straight into a CRM importer. */
export const EXPORT_TEMPLATES = ['leadmine', 'hubspot', 'salesforce'] as const;
export type ExportTemplate = (typeof EXPORT_TEMPLATES)[number];

export interface ExportRequest {
  format: ExportFormat;
  template: ExportTemplate;
  filters: BusinessQuery;
  /** When set, only these record ids are exported. */
  ids?: string[];
}

export interface ExportRecord {
  id: string;
  fileName: string;
  format: ExportFormat;
  template: ExportTemplate;
  rowCount: number;
  byteSize: number;
  createdAt: string;
  filters: BusinessQuery;
}
