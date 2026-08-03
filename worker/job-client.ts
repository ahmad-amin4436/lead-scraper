/**
 * Client for the backend's worker protocol.
 *
 * Every call is service-key authenticated and retried on transient failure: the
 * worker must survive the API restarting or a network blip without dropping the
 * run it is in the middle of.
 */

export interface ClaimedJob {
  id: string;
  requestJson: string;
  ownerUserId: string | null;
  attempts: number;
  stopRequested: boolean;
  completedTaskKeys: string[];
  completedTasks: number;
  found: number;
  saved: number;
  duplicates: number;
  enriched: number;
  enrichmentFailed: number;
  emailsVerified: number;
  whatsAppReachable: number;
  skipped: number;
  failed: number;
}

export interface HeartbeatPayload {
  currentTask?: string;
  completedTaskKey?: string;
  totalTasks?: number;
  found?: number;
  saved?: number;
  duplicates?: number;
  enriched?: number;
  enrichmentFailed?: number;
  emailsVerified?: number;
  whatsAppReachable?: number;
  skipped?: number;
  failed?: number;
  recentResultsJson?: string;
}

export interface HeartbeatResult {
  stopRequested: boolean;
  leaseValid: boolean;
}

export type JobOutcome = 'Completed' | 'Failed' | 'Stopped';

export interface JobClientOptions {
  baseUrl: string;
  serviceKey: string;
  workerId: string;
  leaseSeconds: number;
}

export class JobClient {
  constructor(private readonly options: JobClientOptions) {}

  /** Claims the next runnable job, or null when the queue is empty. */
  async claim(): Promise<ClaimedJob | null> {
    const response = await this.request('/api/ingest/jobs/claim', {
      workerId: this.options.workerId,
      leaseSeconds: this.options.leaseSeconds,
    });

    // 204 means nothing to do — the worker backs off and polls again.
    if (response.status === 204) return null;

    return (await response.json()) as ClaimedJob;
  }

  /**
   * Reports progress and extends the lease.
   *
   * Returns `leaseValid: false` if the lease was taken over, which the caller
   * must treat as "stop immediately" — otherwise two workers would write
   * progress for the same run.
   */
  async heartbeat(jobId: string, payload: HeartbeatPayload): Promise<HeartbeatResult> {
    try {
      const response = await this.request(`/api/ingest/jobs/${jobId}/heartbeat`, {
        workerId: this.options.workerId,
        leaseSeconds: this.options.leaseSeconds,
        ...payload,
      });

      return (await response.json()) as HeartbeatResult;
    } catch (error) {
      // A failed heartbeat is not fatal on its own: the lease has slack, and the
      // next beat may succeed. Keep working rather than abandoning the run.
      console.warn(`[worker] heartbeat failed for ${jobId}:`, describe(error));
      return { stopRequested: false, leaseValid: true };
    }
  }

  async complete(jobId: string, status: JobOutcome, error?: string): Promise<void> {
    await this.request(`/api/ingest/jobs/${jobId}/complete`, {
      workerId: this.options.workerId,
      status,
      error,
    });
  }

  /** POSTs with bounded retries and exponential backoff. */
  private async request(path: string, body: unknown, attempts = 4): Promise<Response> {
    let lastError: unknown;

    for (let attempt = 1; attempt <= attempts; attempt += 1) {
      try {
        const response = await fetch(`${this.options.baseUrl}${path}`, {
          method: 'POST',
          headers: {
            'Content-Type': 'application/json',
            'X-Service-Key': this.options.serviceKey,
          },
          body: JSON.stringify(body),
        });

        // 4xx is our bug or a rejected lease — retrying cannot help.
        if (response.status >= 400 && response.status < 500) {
          const text = await response.text().catch(() => '');
          throw new Error(`${path} -> HTTP ${response.status} ${text.slice(0, 200)}`);
        }

        if (!response.ok) throw new Error(`${path} -> HTTP ${response.status}`);

        return response;
      } catch (error) {
        lastError = error;
        if (attempt === attempts) break;

        const delay = Math.min(8000, 400 * 2 ** (attempt - 1));
        await sleep(delay);
      }
    }

    throw lastError instanceof Error ? lastError : new Error(String(lastError));
  }
}

export function sleep(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

export function describe(error: unknown): string {
  return error instanceof Error ? error.message : String(error);
}
