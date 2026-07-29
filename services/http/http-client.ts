import '@/lib/server-guard';

import axios, { AxiosError, type AxiosInstance, type AxiosRequestConfig } from 'axios';

export const DEFAULT_USER_AGENT =
  'LeadMineAI/1.0 (+https://github.com/leadmine-ai; business contact discovery bot)';

export interface HttpClientOptions {
  timeoutMs: number;
  userAgent?: string;
  /** Cap on response size in bytes; oversized bodies are rejected. */
  maxContentLength?: number;
  maxRedirects?: number;
  headers?: Record<string, string>;
}

export function createHttpClient(options: HttpClientOptions): AxiosInstance {
  return axios.create({
    timeout: options.timeoutMs,
    maxRedirects: options.maxRedirects ?? 5,
    maxContentLength: options.maxContentLength ?? 4 * 1024 * 1024,
    maxBodyLength: options.maxContentLength ?? 4 * 1024 * 1024,
    // Resolve 4xx so callers can inspect the status instead of catching.
    validateStatus: (status) => status >= 200 && status < 500,
    decompress: true,
    headers: {
      'User-Agent': options.userAgent ?? DEFAULT_USER_AGENT,
      Accept: 'text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8',
      'Accept-Language': 'en-US,en;q=0.9',
      ...options.headers,
    },
  });
}

/** A crawler User-Agent that names the operator, so site owners can contact them. */
export function buildCrawlerUserAgent(contactEmail: string): string {
  const contact = contactEmail.trim();
  return contact
    ? `LeadMineAI/1.0 (+mailto:${contact}; business contact discovery bot)`
    : DEFAULT_USER_AGENT;
}

/** Network blips and 5xx/429 are worth another attempt; 4xx generally is not. */
export function isRetryableError(error: unknown): boolean {
  if (!axios.isAxiosError(error)) return false;

  const axiosError = error as AxiosError;
  if (axiosError.code === 'ECONNABORTED' || axiosError.code === 'ETIMEDOUT') return true;
  if (axiosError.code === 'ECONNRESET' || axiosError.code === 'ENOTFOUND') return true;
  if (axiosError.code === 'EAI_AGAIN' || axiosError.code === 'ECONNREFUSED') return true;

  const status = axiosError.response?.status;
  if (status === undefined) return true;
  return status === 429 || status >= 500;
}

export function describeHttpError(error: unknown): string {
  if (axios.isAxiosError(error)) {
    const status = error.response?.status;
    if (status) return `HTTP ${status}`;
    return error.code ?? error.message;
  }
  return error instanceof Error ? error.message : String(error);
}

export type { AxiosInstance, AxiosRequestConfig };
