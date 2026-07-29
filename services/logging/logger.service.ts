import '@/lib/server-guard';

import { logRepository } from '@/repositories/log.repository';
import type { LogEntry, LogEvent, LogLevel } from '@/types/log';
import { createId } from '@/utils/id';

type LogContext = Record<string, string | number | boolean | null | undefined>;

/** Drops undefined values so the persisted context stays JSON-clean. */
function cleanContext(context: LogContext = {}): LogEntry['context'] {
  const result: LogEntry['context'] = {};
  for (const [key, value] of Object.entries(context)) {
    if (value !== undefined) result[key] = value;
  }
  return result;
}

export interface LogOptions {
  jobId?: string | null;
  elapsedMs?: number | null;
  context?: LogContext;
}

async function write(
  level: LogLevel,
  event: LogEvent,
  message: string,
  options: LogOptions = {},
): Promise<void> {
  const entry: LogEntry = {
    id: createId('log'),
    timestamp: new Date().toISOString(),
    level,
    event,
    message,
    jobId: options.jobId ?? null,
    elapsedMs: options.elapsedMs ?? null,
    context: cleanContext(options.context),
  };

  try {
    await logRepository.append(entry);
  } catch (error) {
    // Logging must never break the caller.
    console.error('[logger] failed to persist entry:', error);
  }

  if (level === 'error') console.error(`[${event}] ${message}`);
  else if (level === 'warn') console.warn(`[${event}] ${message}`);
}

export const logger = {
  debug: (event: LogEvent, message: string, options?: LogOptions) =>
    write('debug', event, message, options),
  info: (event: LogEvent, message: string, options?: LogOptions) =>
    write('info', event, message, options),
  warn: (event: LogEvent, message: string, options?: LogOptions) =>
    write('warn', event, message, options),
  error: (event: LogEvent, message: string, options?: LogOptions) =>
    write('error', event, message, options),
  flush: () => logRepository.flush(),
};
