'use client';

import { useCallback, useEffect, useRef, useState } from 'react';

import type { JobEvent, JobSnapshot } from '@/types/job';

export interface JobStreamState {
  job: JobSnapshot | null;
  connected: boolean;
  error: string | null;
}

/**
 * Subscribes to a job's Server-Sent Events stream and keeps a live snapshot.
 *
 * The connection closes on the `done` event. `EventSource` reconnects on its
 * own after a transient drop, so no manual retry loop is needed; a job that no
 * longer exists (404) is treated as terminal and the stream is torn down.
 */
export function useJobStream(jobId: string | null): JobStreamState & { reset: () => void } {
  const [job, setJob] = useState<JobSnapshot | null>(null);
  const [connected, setConnected] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const sourceRef = useRef<EventSource | null>(null);

  const reset = useCallback(() => {
    sourceRef.current?.close();
    sourceRef.current = null;
    setJob(null);
    setConnected(false);
    setError(null);
  }, []);

  useEffect(() => {
    if (!jobId) return;

    const source = new EventSource(`/api/search/${jobId}/stream`);
    sourceRef.current = source;
    let closedByServer = false;

    const applyEvent = (raw: string): void => {
      let event: JobEvent;
      try {
        event = JSON.parse(raw) as JobEvent;
      } catch {
        return;
      }

      setJob((current) => {
        switch (event.type) {
          case 'snapshot':
            return event.job;

          case 'progress':
            if (!current) return current;
            return {
              ...current,
              status: event.status,
              counters: event.counters,
              progress: event.progress,
            };

          case 'status':
            if (!current) return current;
            return { ...current, status: event.status, error: event.error };

          case 'result':
            if (!current) return current;
            // Newest first, matching the server's snapshot ordering.
            return { ...current, results: [event.result, ...current.results].slice(0, 300) };

          case 'done':
            closedByServer = true;
            return current ? { ...current, status: event.status } : current;

          default:
            return current;
        }
      });
    };

    for (const type of ['snapshot', 'progress', 'status', 'result', 'done'] as const) {
      source.addEventListener(type, (messageEvent) => {
        applyEvent((messageEvent as MessageEvent<string>).data);
      });
    }

    source.onopen = () => {
      setConnected(true);
      setError(null);
    };

    source.onerror = () => {
      setConnected(false);
      // A close after `done` is the normal end of stream, not a failure.
      if (closedByServer) {
        source.close();
        return;
      }
      if (source.readyState === EventSource.CLOSED) {
        setError('Live connection lost. Progress is still being saved on the server.');
      }
    };

    return () => {
      source.close();
      sourceRef.current = null;
      setConnected(false);
    };
  }, [jobId]);

  return { job, connected, error, reset };
}
