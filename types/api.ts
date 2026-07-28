export interface ApiSuccess<T> {
  ok: true;
  data: T;
}

export interface ApiFailure {
  ok: false;
  error: {
    code: string;
    message: string;
    /** Field-level messages produced by Zod validation. */
    fields?: Record<string, string[]>;
  };
}

export type ApiResult<T> = ApiSuccess<T> | ApiFailure;
