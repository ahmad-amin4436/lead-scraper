import type { JobStatus } from './job';

/** One person found, appended to a people-search job's live results as it runs. */
export interface PeopleSearchOutcome {
  fullName: string;
  jobTitle: string;
  linkedInUrl: string;
  location: string;
  isDecisionMaker: boolean;
}

export interface PeopleSearchCounters {
  /** Requested max — the job's own idea of "done" once found reaches this. */
  totalTasks: number;
  found: number;
}

/**
 * A company-scoped LinkedIn people-search job's live progress. Polled the same
 * way {@link import('./linkedin-job').LinkedInJobSnapshot} is for an
 * enrichment batch — the search runs in the scraper worker, not in the
 * request that started it.
 */
export interface PeopleSearchJobSnapshot {
  id: string;
  status: JobStatus;
  counters: PeopleSearchCounters;
  /** 0-100. */
  percent: number;
  currentTask: string;
  startedAt: string;
  finishedAt: string | null;
  error: string | null;
  /** Every person found so far — all saved automatically as the job runs. */
  outcomes: PeopleSearchOutcome[];
}

export interface StartPeopleSearchRequest {
  companyName: string;
  keywords?: string;
  location?: string;
  maxResults?: number;
}
