// Translation between the .NET SearchJob DTO (shared with search runs, tagged
// Kind: 'LinkedInEnrichment') and the shape the LinkedIn Enrichment page speaks.

import type { JobStatus } from '@/types/job';
import type { LinkedInJobOutcome, LinkedInJobSnapshot } from '@/types/linkedin-job';
import type { BackendSearchJob } from './search-mapper';

const JOB_STATUS_MAP: Record<string, JobStatus> = {
  Queued: 'queued',
  Running: 'running',
  Stopping: 'stopping',
  Completed: 'completed',
  Failed: 'failed',
  Stopped: 'stopped',
};

interface BackendLinkedInOutcome {
  businessId?: string;
  businessName?: string;
  companyEnriched?: boolean;
  peopleFound?: number;
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

export function toLinkedInJobSnapshot(job: BackendSearchJob): LinkedInJobSnapshot {
  const rawOutcomes = parseJson<BackendLinkedInOutcome[]>(job.recentResultsJson, []);

  const outcomes: LinkedInJobOutcome[] = rawOutcomes.map((outcome, index) => ({
    businessId: outcome.businessId ?? `${job.id}-${index}`,
    businessName: outcome.businessName ?? '',
    companyEnriched: outcome.companyEnriched ?? false,
    peopleFound: outcome.peopleFound ?? 0,
    error: outcome.error ?? null,
  }));

  return {
    id: job.id,
    status: JOB_STATUS_MAP[job.status] ?? 'queued',
    counters: {
      totalLeads: job.totalTasks,
      processedLeads: job.completedTasks,
      enriched: job.enriched,
      // Repurposed counter — see LinkedInEnrichmentRunner.RunState.BuildHeartbeat.
      peopleFound: job.saved,
      failed: job.failed,
    },
    percent: job.percentComplete,
    currentTask: job.currentTask,
    startedAt: job.startedAt,
    finishedAt: job.finishedAt,
    error: job.error,
    outcomes,
  };
}
