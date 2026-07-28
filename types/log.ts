export const LOG_LEVELS = ['debug', 'info', 'warn', 'error'] as const;
export type LogLevel = (typeof LOG_LEVELS)[number];

export const LOG_EVENTS = [
  'search.started',
  'search.completed',
  'search.failed',
  'search.stopped',
  'search.paused',
  'search.resumed',
  'provider.query',
  'provider.failed',
  'business.saved',
  'business.duplicate',
  'enrichment.started',
  'enrichment.completed',
  'enrichment.skipped',
  'enrichment.failed',
  'export.created',
  'export.failed',
  'database.written',
  'settings.updated',
] as const;
export type LogEvent = (typeof LOG_EVENTS)[number];

export interface LogEntry {
  id: string;
  timestamp: string;
  level: LogLevel;
  event: LogEvent;
  message: string;
  jobId: string | null;
  /** Duration in ms for events that measure elapsed work. */
  elapsedMs: number | null;
  context: Record<string, string | number | boolean | null>;
}

export interface LogQuery {
  level?: LogLevel;
  event?: LogEvent;
  jobId?: string;
  search?: string;
  page?: number;
  pageSize?: number;
}

export interface LogPage {
  rows: LogEntry[];
  total: number;
  page: number;
  pageSize: number;
  pageCount: number;
}
