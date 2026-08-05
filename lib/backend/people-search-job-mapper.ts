// Translation between the .NET SearchJob DTO (shared with search runs and
// LinkedIn enrichment, tagged Kind: 'LinkedInPeopleSearch') and the shape the
// People Search page speaks.

import type { JobStatus } from '@/types/job';
import type { PeopleSearchOutcome, PeopleSearchJobSnapshot } from '@/types/people-search-job';
import type { BackendSearchJob } from './search-mapper';

const JOB_STATUS_MAP: Record<string, JobStatus> = {
  Queued: 'queued',
  Running: 'running',
  Stopping: 'stopping',
  Completed: 'completed',
  Failed: 'failed',
  Stopped: 'stopped',
};

interface BackendPeopleSearchOutcome {
  fullName?: string;
  jobTitle?: string;
  linkedInUrl?: string;
  location?: string;
  isDecisionMaker?: boolean;
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

export function toPeopleSearchJobSnapshot(job: BackendSearchJob): PeopleSearchJobSnapshot {
  const rawOutcomes = parseJson<BackendPeopleSearchOutcome[]>(job.recentResultsJson, []);

  const outcomes: PeopleSearchOutcome[] = rawOutcomes.map((outcome) => ({
    fullName: outcome.fullName ?? '',
    jobTitle: outcome.jobTitle ?? '',
    linkedInUrl: outcome.linkedInUrl ?? '',
    location: outcome.location ?? '',
    isDecisionMaker: outcome.isDecisionMaker ?? false,
  }));

  return {
    id: job.id,
    status: JOB_STATUS_MAP[job.status] ?? 'queued',
    counters: {
      totalTasks: job.totalTasks,
      // Repurposed counter — see LinkedInPeopleSearchRunner.SaveResultsAsync.
      found: job.saved,
    },
    percent: job.percentComplete,
    currentTask: job.currentTask,
    startedAt: job.startedAt,
    finishedAt: job.finishedAt,
    error: job.error,
    outcomes,
  };
}
