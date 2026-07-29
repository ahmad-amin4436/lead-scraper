import '@/lib/server-guard';

import { ZodError, type ZodType } from 'zod';

import { AppError, ValidationError } from '@/lib/errors';
import type { ApiResult } from '@/types/api';

export function ok<T>(data: T, init?: ResponseInit): Response {
  return Response.json({ ok: true, data } satisfies ApiResult<T>, {
    status: 200,
    ...init,
  });
}

export function fail(
  message: string,
  code = 'error',
  status = 400,
  fields?: Record<string, string[]>,
): Response {
  return Response.json(
    { ok: false, error: { code, message, ...(fields ? { fields } : {}) } } satisfies ApiResult<never>,
    { status },
  );
}

/** Flattens a ZodError into `{ fieldPath: [messages] }`. */
function zodFields(error: ZodError): Record<string, string[]> {
  const fields: Record<string, string[]> = {};

  for (const issue of error.issues) {
    const key = issue.path.length > 0 ? issue.path.join('.') : '_';
    (fields[key] ??= []).push(issue.message);
  }

  return fields;
}

/**
 * Wraps a route handler so thrown errors become consistent JSON responses.
 * Unexpected errors are logged server-side and reported generically, so
 * internal details never reach the client.
 */
export function handle(fn: () => Promise<Response>): Promise<Response> {
  return fn().catch((error: unknown) => {
    if (error instanceof ValidationError) {
      return fail(error.message, error.code, error.status, error.fields);
    }
    if (error instanceof ZodError) {
      return fail('Invalid request', 'validation_error', 400, zodFields(error));
    }
    if (error instanceof AppError) {
      return fail(error.message, error.code, error.status);
    }

    console.error('[api] unhandled error:', error);
    return fail('Something went wrong on the server.', 'internal_error', 500);
  });
}

/** Parses a JSON body against a schema, raising a ValidationError on failure. */
export async function parseJson<T>(request: Request, schema: ZodType<T>): Promise<T> {
  let payload: unknown;

  try {
    payload = await request.json();
  } catch {
    throw new ValidationError('Request body must be valid JSON');
  }

  const result = schema.safeParse(payload);
  if (!result.success) {
    throw new ValidationError('Invalid request', zodFields(result.error));
  }

  return result.data;
}

/** Parses URL search params against a schema. */
export function parseQuery<T>(request: Request, schema: ZodType<T>): T {
  const url = new URL(request.url);
  const raw: Record<string, string> = {};

  for (const [key, value] of url.searchParams) {
    if (value !== '') raw[key] = value;
  }

  const result = schema.safeParse(raw);
  if (!result.success) {
    throw new ValidationError('Invalid query parameters', zodFields(result.error));
  }

  return result.data;
}
