// Translation between the .NET SearchJob DTO (shared with search runs and
// LinkedIn jobs, tagged Kind: 'EmailSend') and the shape the Send Email page
// speaks. No dedicated Next.js route sits in front of this — the email
// feature calls /api/backend/email/* directly, same as every other email
// hook — so the mapping happens here instead of in a route handler.

import type { JobStatus } from '@/types/job';
import type { EmailSendJobOutcome, EmailSendJobSnapshot } from '@/types/email-send-job';
import type { BackendSearchJob } from './search-mapper';

const JOB_STATUS_MAP: Record<string, JobStatus> = {
  Queued: 'queued',
  Running: 'running',
  Stopping: 'stopping',
  Completed: 'completed',
  Failed: 'failed',
  Stopped: 'stopped',
};

interface BackendEmailSendOutcome {
  businessId?: string;
  toEmail?: string;
  status?: string;
  error?: string | null;
}

function parseJson<T>(raw: string | null | undefined, fallback: T): T {
  if (!raw) return fallback;
  try {
    return JSON.parse(raw) as T;
  } catch {
    // A malformed row costs a progress panel, not the run.
    return fallback;
  }
}

export function toEmailSendJobSnapshot(job: BackendSearchJob): EmailSendJobSnapshot {
  const rawOutcomes = parseJson<BackendEmailSendOutcome[]>(job.recentResultsJson, []);

  const outcomes: EmailSendJobOutcome[] = rawOutcomes.map((outcome, index) => ({
    businessId: outcome.businessId ?? `${job.id}-${index}`,
    toEmail: outcome.toEmail ?? '',
    status: outcome.status ?? 'skipped',
    error: outcome.error ?? null,
  }));

  return {
    id: job.id,
    status: JOB_STATUS_MAP[job.status] ?? 'queued',
    counters: {
      totalLeads: job.totalTasks,
      processedLeads: job.completedTasks,
      // Repurposed counter — see EmailSendRunner.RunState.BuildHeartbeat.
      sent: job.saved,
      failed: job.failed,
      skipped: job.skipped,
    },
    percent: job.percentComplete,
    currentTask: job.currentTask,
    startedAt: job.startedAt,
    finishedAt: job.finishedAt,
    error: job.error,
    outcomes,
  };
}
