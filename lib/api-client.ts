import type { ApiResult } from '@/types/api';

export class ApiClientError extends Error {
  readonly code: string;
  readonly status: number;
  readonly fields: Record<string, string[]>;

  constructor(message: string, code: string, status: number, fields: Record<string, string[]> = {}) {
    super(message);
    this.name = 'ApiClientError';
    this.code = code;
    this.status = status;
    this.fields = fields;
  }
}

/**
 * Thin fetch wrapper that unwraps the `{ ok, data }` envelope every route
 * handler returns, and turns failures into a typed `ApiClientError`.
 */
export async function apiFetch<T>(input: string, init?: RequestInit): Promise<T> {
  let response: Response;

  try {
    response = await fetch(input, {
      ...init,
      headers: {
        // FormData (a file upload) needs the browser to set its own
        // Content-Type with a multipart boundary — forcing JSON here would
        // send the file as an unparseable body.
        ...(init?.body && !(init.body instanceof FormData) ? { 'Content-Type': 'application/json' } : {}),
        ...init?.headers,
      },
    });
  } catch (error) {
    if (error instanceof DOMException && error.name === 'AbortError') throw error;
    throw new ApiClientError('Could not reach the server.', 'network_error', 0);
  }

  let payload: ApiResult<T> | null = null;

  try {
    payload = (await response.json()) as ApiResult<T>;
  } catch {
    throw new ApiClientError(
      `Unexpected response from the server (HTTP ${response.status}).`,
      'bad_response',
      response.status,
    );
  }

  if (!payload || payload.ok !== true) {
    const error = payload && payload.ok === false ? payload.error : null;
    throw new ApiClientError(
      error?.message ?? 'Request failed.',
      error?.code ?? 'error',
      response.status,
      error?.fields ?? {},
    );
  }

  return payload.data;
}

export function buildQueryString(params: Record<string, unknown>): string {
  const search = new URLSearchParams();

  for (const [key, value] of Object.entries(params)) {
    if (value === undefined || value === null || value === '') continue;
    search.set(key, String(value));
  }

  const query = search.toString();
  return query ? `?${query}` : '';
}
