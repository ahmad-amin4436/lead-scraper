import { Badge, type BadgeProps } from '@/components/ui/badge';
import type { BusinessStatus } from '@/types/business';
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
