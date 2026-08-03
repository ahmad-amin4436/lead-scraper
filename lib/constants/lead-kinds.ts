/**
 * "What kind of leads do you want?" — asked before a scrape, and reusable as a
 * filter on the saved-leads and export screens.
 *
 * Mirrors `LeadKind` in the .NET backend. The values are sent verbatim, so the
 * two must stay in step.
 */
export const LEAD_KINDS = [
  'Any',
  'New',
  'Enriched',
  'NoWebsite',
  'Partial',
  'WhatsAppOnly',
  'EmailOnly',
  'DeliverableEmail',
] as const;

export type LeadKind = (typeof LEAD_KINDS)[number];

export interface LeadKindMeta {
  value: LeadKind;
  label: string;
  description: string;
}

export const LEAD_KIND_META: Record<LeadKind, LeadKindMeta> = {
  Any: {
    value: 'Any',
    label: 'Everything',
    description: 'Keep every business found, contactable or not.',
  },
  New: {
    value: 'New',
    label: 'New (not yet enriched)',
    description: 'Discovered businesses before any contact discovery has run.',
  },
  Enriched: {
    value: 'Enriched',
    label: 'Enriched — has an email',
    description: 'Contact discovery found a usable email address.',
  },
  NoWebsite: {
    value: 'NoWebsite',
    label: 'No website',
    description: 'Nothing to crawl, so usually phone-only leads.',
  },
  Partial: {
    value: 'Partial',
    label: 'Partial — some contact detail',
    description: 'Found a phone or social profile, but no email address.',
  },
  WhatsAppOnly: {
    value: 'WhatsAppOnly',
    label: 'Reachable on WhatsApp',
    description: 'Confirmed or likely on WhatsApp. Likely is inferred from the line type, not verified.',
  },
  EmailOnly: {
    value: 'EmailOnly',
    label: 'Has an email address',
    description: 'Any lead carrying an email, verified or not.',
  },
  DeliverableEmail: {
    value: 'DeliverableEmail',
    label: 'Deliverable email only',
    description: 'Email whose domain accepts mail. The safest list to send to.',
  },
};

export const LEAD_KIND_OPTIONS: LeadKindMeta[] = LEAD_KINDS.map((kind) => LEAD_KIND_META[kind]);
