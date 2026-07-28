'use client';

import { useCallback, useEffect, useRef, useState } from 'react';

import { apiFetch } from '@/lib/api-client';
import type { JobSnapshot } from '@/types/job';

export interface JobPollState {
  job: JobSnapshot | null;
  error: string | null;
  reset: () => void;
}

/** How often to poll while a job is active. */
const POLL_INTERVAL_MS = 1_500;

function isTerminal(status: JobSnapshot['status']): boolean {
  return status === 'completed' || status === 'failed' || status === 'stopped';
}

/**
 * Polls a job's snapshot endpoint for live progress.
 *
 * This replaced a Server-Sent Events stream: on serverless the job runs in a
 * different instance than any long-lived connection would land on, so a pull
 * model (read the shared job blob every {@link POLL_INTERVAL_MS}) is the only
 * thing that works. Polling stops once the job reaches a terminal status.
 */
export function useJobPoll(jobId: string | null): JobPollState {
  const [job, setJob] = useState<JobSnapshot | null>(null);
  const [error, setError] = useState<string | null>(null);
  const timerRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  const reset = useCallback(() => {
    if (timerRef.current) clearTimeout(timerRef.current);
    timerRef.current = null;
    setJob(null);
    setError(null);
  }, []);

  useEffect(() => {
    if (!jobId) return;

    let cancelled = false;

    const poll = async (): Promise<void> => {
      try {
        const snapshot = await apiFetch<JobSnapshot>(`/api/search/${jobId}`);
        if (cancelled) return;
        setJob(snapshot);
        setError(null);

        if (!isTerminal(snapshot.status)) {
          timerRef.current = setTimeout(() => void poll(), POLL_INTERVAL_MS);
        }
      } catch (err) {
        if (cancelled) return;
        // Keep the last known snapshot on a transient error and retry; the run
        // continues on the server regardless of this connection.
        setError(err instanceof Error ? err.message : 'Could not fetch progress.');
        timerRef.current = setTimeout(() => void poll(), POLL_INTERVAL_MS);
      }
    };

    void poll();

    return () => {
      cancelled = true;
      if (timerRef.current) clearTimeout(timerRef.current);
      timerRef.current = null;
    };
  }, [jobId]);

  return { job, error, reset };
}
