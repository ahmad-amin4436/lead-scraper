import 'server-only';

import { BUSINESS_COLUMNS, type BusinessRecord } from '@/types/business';
import type { ExportTemplate } from '@/types/export';
import { normalizeHost } from '@/utils/normalize';

export interface TemplateColumn {
  header: string;
  width: number;
  value: (record: BusinessRecord) => string | number | null;
}

const leadmineColumns: TemplateColumn[] = BUSINESS_COLUMNS.map((column) => ({
  header: column.header,
  width: column.width,
  value: (record) => {
    const raw = record[column.key];
    return raw === null || raw === undefined ? '' : (raw as string | number);
  },
}));

/** Matches HubSpot's company import property names. */
const hubspotColumns: TemplateColumn[] = [
  { header: 'Company name', width: 34, value: (r) => r.name },
  { header: 'Company domain name', width: 28, value: (r) => normalizeHost(r.website) },
  { header: 'Website URL', width: 32, value: (r) => r.website },
  { header: 'Email', width: 30, value: (r) => r.email },
  { header: 'Email Status', width: 14, value: (r) => r.emailStatus || 'unverified' },
  { header: 'Phone number', width: 20, value: (r) => r.phone },
  { header: 'WhatsApp Status', width: 16, value: (r) => r.whatsappStatus || 'unverified' },
  { header: 'Street address', width: 42, value: (r) => r.address },
  { header: 'City', width: 18, value: (r) => r.city },
  { header: 'State/Region', width: 18, value: (r) => r.state },
  { header: 'Country/Region', width: 16, value: (r) => r.country },
  { header: 'Industry', width: 22, value: (r) => r.category },
  { header: 'LinkedIn Company Page', width: 30, value: (r) => r.linkedin },
  { header: 'Facebook Company Page', width: 30, value: (r) => r.facebook },
  { header: 'Description', width: 44, value: (r) => r.notes },
  { header: 'Original Source', width: 18, value: (r) => r.source },
];

/** Matches Salesforce's standard Lead import fields. */
const salesforceColumns: TemplateColumn[] = [
  { header: 'Company', width: 34, value: (r) => r.name },
  // Salesforce requires LastName on Lead; the business name stands in when no
  // individual contact is known.
  { header: 'LastName', width: 34, value: (r) => r.name },
  { header: 'Email', width: 30, value: (r) => r.email },
  { header: 'Email Status', width: 14, value: (r) => r.emailStatus || 'unverified' },
  { header: 'Phone', width: 20, value: (r) => r.phone },
  { header: 'WhatsApp Status', width: 16, value: (r) => r.whatsappStatus || 'unverified' },
  { header: 'Website', width: 32, value: (r) => r.website },
  { header: 'Street', width: 42, value: (r) => r.address },
  { header: 'City', width: 18, value: (r) => r.city },
  { header: 'State', width: 18, value: (r) => r.state },
  { header: 'Country', width: 16, value: (r) => r.country },
  { header: 'Industry', width: 22, value: (r) => r.category },
  { header: 'LeadSource', width: 18, value: (r) => r.source },
  { header: 'Rating', width: 12, value: (r) => r.rating ?? '' },
  { header: 'NumberOfEmployees', width: 18, value: () => '' },
  { header: 'Description', width: 44, value: (r) => r.notes },
];

const TEMPLATES: Record<ExportTemplate, TemplateColumn[]> = {
  leadmine: leadmineColumns,
  hubspot: hubspotColumns,
  salesforce: salesforceColumns,
};

export function getTemplateColumns(template: ExportTemplate): TemplateColumn[] {
  return TEMPLATES[template] ?? leadmineColumns;
}

export const TEMPLATE_LABELS: Record<ExportTemplate, string> = {
  leadmine: 'LeadMine (all fields)',
  hubspot: 'HubSpot companies',
  salesforce: 'Salesforce leads',
};
