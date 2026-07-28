export function formatNumber(value: number): string {
  return new Intl.NumberFormat('en-US').format(value);
}

export function formatPercent(value: number, fractionDigits = 0): string {
  return `${value.toFixed(fractionDigits)}%`;
}

export function formatBytes(bytes: number): string {
  if (bytes <= 0) return '0 B';
  const units = ['B', 'KB', 'MB', 'GB'];
  const exponent = Math.min(units.length - 1, Math.floor(Math.log(bytes) / Math.log(1024)));
  const value = bytes / 1024 ** exponent;
  return `${value.toFixed(exponent === 0 ? 0 : 1)} ${units[exponent]}`;
}

/** Compact duration: `1h 04m`, `4m 12s`, `850ms`. */
export function formatDuration(ms: number): string {
  if (!Number.isFinite(ms) || ms < 0) return '—';
  if (ms < 1000) return `${Math.round(ms)}ms`;

  const totalSeconds = Math.floor(ms / 1000);
  const hours = Math.floor(totalSeconds / 3600);
  const minutes = Math.floor((totalSeconds % 3600) / 60);
  const seconds = totalSeconds % 60;

  if (hours > 0) return `${hours}h ${String(minutes).padStart(2, '0')}m`;
  if (minutes > 0) return `${minutes}m ${String(seconds).padStart(2, '0')}s`;
  return `${seconds}s`;
}

export function formatDateTime(iso: string): string {
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return '—';
  return new Intl.DateTimeFormat('en-US', {
    dateStyle: 'medium',
    timeStyle: 'short',
  }).format(date);
}

export function formatDate(iso: string): string {
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return '—';
  return new Intl.DateTimeFormat('en-US', { dateStyle: 'medium' }).format(date);
}

export function formatRelativeTime(iso: string): string {
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return '—';

  const deltaMs = date.getTime() - Date.now();
  const absSeconds = Math.abs(deltaMs) / 1000;
  const formatter = new Intl.RelativeTimeFormat('en-US', { numeric: 'auto' });

  const divisions: [number, Intl.RelativeTimeFormatUnit][] = [
    [60, 'second'],
    [3600, 'minute'],
    [86400, 'hour'],
    [604800, 'day'],
    [2629800, 'week'],
    [31557600, 'month'],
  ];

  for (const [limit, unit] of divisions) {
    if (absSeconds < limit) {
      const divisor = limit === 60 ? 1 : limit === 3600 ? 60 : limit === 86400 ? 3600 : limit === 604800 ? 86400 : limit === 2629800 ? 604800 : 2629800;
      return formatter.format(Math.round(deltaMs / 1000 / divisor), unit);
    }
  }

  return formatter.format(Math.round(deltaMs / 1000 / 31557600), 'year');
}

/** Truncates on a word boundary where possible. */
export function truncate(value: string, maxLength: number): string {
  if (value.length <= maxLength) return value;
  const sliced = value.slice(0, maxLength - 1);
  const lastSpace = sliced.lastIndexOf(' ');
  return `${lastSpace > maxLength * 0.6 ? sliced.slice(0, lastSpace) : sliced}…`;
}
