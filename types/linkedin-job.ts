import type { JobStatus } from './job';

/** One lead's result within a LinkedIn enrichment batch. */
export interface LinkedInJobOutcome {
  businessId: string;
  businessName: string;
  companyEnriched: boolean;
  peopleFound: number;
  error: string | null;
}

export interface LinkedInJobCounters {
  /** Leads in this batch. */
  totalLeads: number;
  /** Leads processed so far (success or failure either way). */
  processedLeads: number;
  /** Leads whose company detail was found. */
  enriched: number;
  /** Decision-makers found across every processed lead. */
  peopleFound: number;
  failed: number;
}

/**
 * A LinkedIn enrichment batch's live progress — the async replacement for the
 * single blocking request the LinkedIn Enrichment page used to make, which
 * could not reliably finish inside the proxy's timeout. Polled the same way
 * {@link import('./job').JobSnapshot} is for a search run.
 */
export interface LinkedInJobSnapshot {
  id: string;
  status: JobStatus;
  counters: LinkedInJobCounters;
  /** 0-100. */
  percent: number;
  currentTask: string;
  startedAt: string;
  finishedAt: string | null;
  error: string | null;
  /** Most recent outcomes, newest first, capped by the runner. */
  outcomes: LinkedInJobOutcome[];
}

export interface StartLinkedInEnrichmentRequest {
  businessIds: string[];
  maxDecisionMakersPerCompany?: number;
}

/**
 * The caller's own LinkedIn session status — never the session content
 * itself. `null` (from the hook, not this shape) means no session has been
 * uploaded yet.
 */
export interface LinkedInSessionStatus {
  uploadedAt: string;
  searchesToday: number;
  /** Set while a restriction cooldown is active; null once it's expired or was never tripped. */
  restrictedUntil: string | null;
  restrictedReason: string | null;
}

/**
 * A short-lived code `backend/tools/LinkedInLogin` redeems to push a captured
 * session straight to the caller's account. `apiBaseUrl` is the .NET API's
 * own public URL — the tool runs on the user's machine and calls it directly,
 * never through this app's Next.js proxy.
 */
export interface LinkedInConnectToken {
  token: string;
  expiresAt: string;
  apiBaseUrl: string;
}
