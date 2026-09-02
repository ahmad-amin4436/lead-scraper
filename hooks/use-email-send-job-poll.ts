'use client';

import { useCallback, useEffect, useRef, useState } from 'react';

import { apiFetch } from '@/lib/api-client';
import { toEmailSendJobSnapshot } from '@/lib/backend/email-send-job-mapper';
import type { BackendSearchJob } from '@/lib/backend/search-mapper';
import type { EmailSendJobSnapshot } from '@/types/email-send-job';

export interface EmailSendJobPollState {
  job: EmailSendJobSnapshot | null;
  error: string | null;
  reset: () => void;
}

/** How often to poll while a batch is active. */
const POLL_INTERVAL_MS = 1_500;

function isTerminal(status: EmailSendJobSnapshot['status']): boolean {
  return status === 'completed' || status === 'failed' || status === 'stopped';
}

/**
 * Polls an email-send batch's snapshot for live progress.
 *
 * Same pull model as {@link import('./use-linkedin-job-poll').useLinkedInJobPoll}
 * and for the same reason: the batch runs in the scraper worker, not in this
 * request, so there is no connection to stream results down even if one
 * stayed open across polls.
 */
export function useEmailSendJobPoll(jobId: string | null): EmailSendJobPollState {
  const [job, setJob] = useState<EmailSendJobSnapshot | null>(null);
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
        const raw = await apiFetch<BackendSearchJob>(`/api/backend/email/send-jobs/${jobId}`);
        if (cancelled) return;

        const snapshot = toEmailSendJobSnapshot(raw);
        setJob(snapshot);
        setError(null);

        if (!isTerminal(snapshot.status)) {
          timerRef.current = setTimeout(() => void poll(), POLL_INTERVAL_MS);
        }
      } catch (err) {
        if (cancelled) return;
        // Keep the last known snapshot on a transient error and retry; the
        // batch continues on the server regardless of this connection.
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
