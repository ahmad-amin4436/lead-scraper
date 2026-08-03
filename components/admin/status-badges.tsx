import { Badge, type BadgeProps } from '@/components/ui/badge';
import type {
  BackendEmailStatus,
  BackendSource,
  BackendStatus,
  BackendWhatsAppStatus,
} from '@/lib/backend/types';

export const BACKEND_SOURCE_LABEL: Record<BackendSource, string> = {
  Manual: 'Manual',
  GooglePlaces: 'Google Places',
  OpenStreetMap: 'OpenStreetMap',
};

export function BackendSourceBadge({ source }: { source: BackendSource }) {
  return <Badge variant="secondary">{BACKEND_SOURCE_LABEL[source] ?? source}</Badge>;
}

const BACKEND_STATUS_META: Record<BackendStatus, { label: string; variant: BadgeProps['variant'] }> = {
  New: { label: 'New', variant: 'secondary' },
  Enriched: { label: 'Enriched', variant: 'success' },
  Partial: { label: 'Partial', variant: 'warning' },
  NoWebsite: { label: 'No website', variant: 'muted' },
  EnrichmentFailed: { label: 'Enrich failed', variant: 'destructive' },
};

export function BackendStatusBadge({ status }: { status: BackendStatus }) {
  const config = BACKEND_STATUS_META[status] ?? { label: status, variant: 'muted' as const };
  return <Badge variant={config.variant}>{config.label}</Badge>;
}

const BACKEND_EMAIL_META: Record<BackendEmailStatus, { label: string; variant: BadgeProps['variant'] }> = {
  Unverified: { label: 'Unchecked', variant: 'muted' },
  Valid: { label: 'Deliverable', variant: 'success' },
  Risky: { label: 'Risky', variant: 'warning' },
  Invalid: { label: 'Dead', variant: 'destructive' },
  Unknown: { label: 'Unknown', variant: 'muted' },
};

export function BackendEmailStatusBadge({ status }: { status: BackendEmailStatus }) {
  const config = BACKEND_EMAIL_META[status] ?? BACKEND_EMAIL_META.Unverified;
  return <Badge variant={config.variant}>{config.label}</Badge>;
}

const BACKEND_WHATSAPP_META: Record<BackendWhatsAppStatus, { label: string; variant: BadgeProps['variant'] }> = {
  Unverified: { label: 'Unchecked', variant: 'muted' },
  Confirmed: { label: 'On WhatsApp', variant: 'success' },
  Likely: { label: 'Likely', variant: 'default' },
  Unlikely: { label: 'Unlikely', variant: 'muted' },
  None: { label: 'No number', variant: 'muted' },
};

export function BackendWhatsAppStatusBadge({ status }: { status: BackendWhatsAppStatus }) {
  const config = BACKEND_WHATSAPP_META[status] ?? BACKEND_WHATSAPP_META.Unverified;
  return <Badge variant={config.variant}>{config.label}</Badge>;
}
