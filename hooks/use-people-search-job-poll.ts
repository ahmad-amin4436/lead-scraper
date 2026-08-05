'use client';

import { useCallback, useEffect, useRef, useState } from 'react';

import { apiFetch } from '@/lib/api-client';
import type { PeopleSearchJobSnapshot } from '@/types/people-search-job';

export interface PeopleSearchJobPollState {
  job: PeopleSearchJobSnapshot | null;
  error: string | null;
  reset: () => void;
}

/** How often to poll while a search is active. */
const POLL_INTERVAL_MS = 1_500;

function isTerminal(status: PeopleSearchJobSnapshot['status']): boolean {
  return status === 'completed' || status === 'failed' || status === 'stopped';
}

/**
 * Polls a people-search job's snapshot endpoint for live progress. Same pull
 * model as {@link import('./use-linkedin-job-poll').useLinkedInJobPoll} and
 * for the same reason: the search runs in the scraper worker, not in this
 * request.
 */
export function usePeopleSearchJobPoll(jobId: string | null): PeopleSearchJobPollState {
  const [job, setJob] = useState<PeopleSearchJobSnapshot | null>(null);
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
        const snapshot = await apiFetch<PeopleSearchJobSnapshot>(`/api/linkedin/people-search-jobs/${jobId}`);
        if (cancelled) return;
        setJob(snapshot);
        setError(null);

        if (!isTerminal(snapshot.status)) {
          timerRef.current = setTimeout(() => void poll(), POLL_INTERVAL_MS);
        }
      } catch (err) {
        if (cancelled) return;
        // Keep the last known snapshot on a transient error and retry; the
        // search continues on the server regardless of this connection.
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
