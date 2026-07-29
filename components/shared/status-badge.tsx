import { Badge, type BadgeProps } from '@/components/ui/badge';
import type { BusinessStatus, EmailStatus, WhatsAppStatus } from '@/types/business';
import type { JobStatus } from '@/types/job';
import type { LogLevel } from '@/types/log';

const BUSINESS_STATUS: Record<BusinessStatus, { label: string; variant: BadgeProps['variant'] }> = {
  new: { label: 'New', variant: 'secondary' },
  enriched: { label: 'Enriched', variant: 'success' },
  partial: { label: 'Partial', variant: 'warning' },
  'no-website': { label: 'No website', variant: 'muted' },
  'enrichment-failed': { label: 'Enrich failed', variant: 'destructive' },
};

export function BusinessStatusBadge({ status }: { status: BusinessStatus }) {
  const config = BUSINESS_STATUS[status] ?? { label: status, variant: 'muted' as const };
  return <Badge variant={config.variant}>{config.label}</Badge>;
}

/**
 * Labels are deliberately hedged. The check proves the *domain* accepts mail,
 * not that the mailbox exists, so nothing here claims a verified inbox.
 */
export const EMAIL_STATUS_META: Record<
  EmailStatus,
  { label: string; variant: BadgeProps['variant']; hint: string }
> = {
  valid: {
    label: 'Deliverable',
    variant: 'success',
    hint: 'Domain accepts mail (MX record found). The individual mailbox is not guaranteed.',
  },
  risky: {
    label: 'Risky',
    variant: 'warning',
    hint: 'Disposable provider, or no MX record so mail may not route.',
  },
  invalid: {
    label: 'Dead',
    variant: 'destructive',
    hint: 'Malformed address, or the domain does not exist / accepts no mail.',
  },
  unknown: {
    label: 'Unknown',
    variant: 'muted',
    hint: 'The domain lookup was inconclusive — worth re-checking later.',
  },
  unverified: {
    label: 'Unchecked',
    variant: 'muted',
    hint: 'Not verified yet.',
  },
};

export function EmailStatusBadge({ status }: { status: EmailStatus }) {
  const config = EMAIL_STATUS_META[status] ?? EMAIL_STATUS_META.unverified;
  return (
    <Badge variant={config.variant} title={config.hint}>
      {config.label}
    </Badge>
  );
}

/**
 * WhatsApp registration cannot be checked for a number you don't own, so only
 * `confirmed` (a link the business published) is evidence — the rest is inferred
 * from the line type and labelled as such.
 */
export const WHATSAPP_STATUS_META: Record<
  WhatsAppStatus,
  { label: string; variant: BadgeProps['variant']; hint: string }
> = {
  confirmed: {
    label: 'On WhatsApp',
    variant: 'success',
    hint: 'The business published a WhatsApp link on its own website.',
  },
  likely: {
    label: 'Likely',
    variant: 'default',
    hint: 'Mobile number, so WhatsApp is likely — but this is inferred, not verified.',
  },
  unlikely: {
    label: 'Unlikely',
    variant: 'muted',
    hint: 'Landline or unvalidated number, so WhatsApp is improbable.',
  },
  none: {
    label: 'No number',
    variant: 'muted',
    hint: 'No phone number on this record.',
  },
  unverified: {
    label: 'Unchecked',
    variant: 'muted',
    hint: 'Not assessed yet.',
  },
};

export function WhatsAppStatusBadge({ status }: { status: WhatsAppStatus }) {
  const config = WHATSAPP_STATUS_META[status] ?? WHATSAPP_STATUS_META.unverified;
  return (
    <Badge variant={config.variant} title={config.hint}>
      {config.label}
    </Badge>
  );
}

const JOB_STATUS: Record<JobStatus, { label: string; variant: BadgeProps['variant'] }> = {
  queued: { label: 'Queued', variant: 'secondary' },
  running: { label: 'Running', variant: 'default' },
  stopping: { label: 'Stopping', variant: 'warning' },
  completed: { label: 'Completed', variant: 'success' },
  failed: { label: 'Failed', variant: 'destructive' },
  stopped: { label: 'Stopped', variant: 'muted' },
};

export function JobStatusBadge({ status }: { status: JobStatus }) {
  const config = JOB_STATUS[status] ?? { label: status, variant: 'muted' as const };
  return <Badge variant={config.variant}>{config.label}</Badge>;
}

const LOG_LEVEL: Record<LogLevel, BadgeProps['variant']> = {
  debug: 'muted',
  info: 'secondary',
  warn: 'warning',
  error: 'destructive',
};

export function LogLevelBadge({ level }: { level: LogLevel }) {
  return (
    <Badge variant={LOG_LEVEL[level] ?? 'muted'} className="uppercase">
      {level}
    </Badge>
  );
}
