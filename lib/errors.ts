/** Base class for errors that are safe to surface to the client verbatim. */
export class AppError extends Error {
  readonly code: string;
  readonly status: number;

  constructor(message: string, code = 'app_error', status = 500) {
    super(message);
    this.name = new.target.name;
    this.code = code;
    this.status = status;
  }
}

export class ValidationError extends AppError {
  readonly fields: Record<string, string[]>;

  constructor(message: string, fields: Record<string, string[]> = {}) {
    super(message, 'validation_error', 400);
    this.fields = fields;
  }
}

export class NotFoundError extends AppError {
  constructor(message = 'Resource not found') {
    super(message, 'not_found', 404);
  }
}

export class ConfigurationError extends AppError {
  constructor(message: string) {
    super(message, 'configuration_error', 400);
  }
}

export class ProviderError extends AppError {
  constructor(message: string, code = 'provider_error') {
    super(message, code, 502);
  }
}

/** Narrows anything thrown into a readable message without leaking stack traces. */
export function toErrorMessage(error: unknown): string {
  if (error instanceof Error) return error.message;
  if (typeof error === 'string') return error;
  return 'An unexpected error occurred';
}
