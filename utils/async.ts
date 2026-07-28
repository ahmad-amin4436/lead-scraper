export function sleep(ms: number, signal?: AbortSignal): Promise<void> {
  if (ms <= 0) return Promise.resolve();

  return new Promise((resolve, reject) => {
    if (signal?.aborted) {
      reject(new DOMException('Aborted', 'AbortError'));
      return;
    }

    const timer = setTimeout(() => {
      signal?.removeEventListener('abort', onAbort);
      resolve();
    }, ms);

    function onAbort() {
      clearTimeout(timer);
      reject(new DOMException('Aborted', 'AbortError'));
    }

    signal?.addEventListener('abort', onAbort, { once: true });
  });
}

export interface RetryOptions {
  attempts: number;
  /** Base delay in ms; grows exponentially with full jitter. */
  baseDelayMs?: number;
  maxDelayMs?: number;
  signal?: AbortSignal;
  /** Return false to fail immediately without consuming further attempts. */
  shouldRetry?: (error: unknown, attempt: number) => boolean;
  onRetry?: (error: unknown, attempt: number, delayMs: number) => void;
}

/** Runs `task` with exponential backoff + full jitter. */
export async function withRetry<T>(
  task: (attempt: number) => Promise<T>,
  options: RetryOptions,
): Promise<T> {
  const attempts = Math.max(1, options.attempts);
  const baseDelay = options.baseDelayMs ?? 500;
  const maxDelay = options.maxDelayMs ?? 15000;

  let lastError: unknown;

  for (let attempt = 1; attempt <= attempts; attempt += 1) {
    try {
      return await task(attempt);
    } catch (error) {
      lastError = error;

      if (error instanceof DOMException && error.name === 'AbortError') throw error;
      if (attempt >= attempts) break;
      if (options.shouldRetry && !options.shouldRetry(error, attempt)) break;

      const ceiling = Math.min(maxDelay, baseDelay * 2 ** (attempt - 1));
      const delay = Math.round(Math.random() * ceiling);
      options.onRetry?.(error, attempt, delay);
      await sleep(delay, options.signal);
    }
  }

  throw lastError;
}

/**
 * Maps over `items` with a bounded number of in-flight tasks.
 * Results keep the input order; individual failures are surfaced to `onError`
 * rather than cancelling the whole batch.
 */
export async function mapWithConcurrency<T, R>(
  items: readonly T[],
  limit: number,
  worker: (item: T, index: number) => Promise<R>,
  onError?: (error: unknown, item: T, index: number) => void,
): Promise<(R | undefined)[]> {
  const results = new Array<R | undefined>(items.length);
  const size = Math.max(1, Math.min(limit, items.length));
  let cursor = 0;

  async function run(): Promise<void> {
    while (cursor < items.length) {
      const index = cursor;
      cursor += 1;
      try {
        results[index] = await worker(items[index], index);
      } catch (error) {
        onError?.(error, items[index], index);
        results[index] = undefined;
      }
    }
  }

  await Promise.all(Array.from({ length: size }, run));
  return results;
}

/**
 * Serialises async access to a shared resource.
 * Used to keep concurrent writes to the same file from interleaving.
 */
export class Mutex {
  private tail: Promise<unknown> = Promise.resolve();

  run<T>(task: () => Promise<T>): Promise<T> {
    const result = this.tail.then(task, task);
    // Swallow rejection on the chain so one failure doesn't poison the queue.
    this.tail = result.then(
      () => undefined,
      () => undefined,
    );
    return result;
  }
}

/** Token-bucket limiter that spaces calls to respect a per-minute quota. */
export class RateLimiter {
  private nextSlot = 0;

  constructor(private readonly minIntervalMs: number) {}

  static perMinute(requestsPerMinute: number): RateLimiter {
    const safe = Math.max(1, requestsPerMinute);
    return new RateLimiter(Math.ceil(60_000 / safe));
  }

  async acquire(signal?: AbortSignal): Promise<void> {
    const now = Date.now();
    const runAt = Math.max(now, this.nextSlot);
    this.nextSlot = runAt + this.minIntervalMs;
    await sleep(runAt - now, signal);
  }
}
