import type { JobStatus } from './job';

/** One recipient's outcome within an email-send batch. */
export interface EmailSendJobOutcome {
  businessId: string;
  toEmail: string;
  /** "sent" | "failed" | "skipped" | "dry-run" */
  status: string;
  error: string | null;
}

export interface EmailSendJobCounters {
  totalLeads: number;
  processedLeads: number;
  sent: number;
  failed: number;
  skipped: number;
}

/**
 * An email-send batch's live progress — the async replacement for the single
 * blocking `POST /api/email/send` request, which could not reliably finish
 * inside the proxy's timeout once paced sequential sends ran long. Polled the
 * same way {@link import('./linkedin-job').LinkedInJobSnapshot} is for a
 * LinkedIn enrichment batch.
 */
export interface EmailSendJobSnapshot {
  id: string;
  status: JobStatus;
  counters: EmailSendJobCounters;
  percent: number;
  currentTask: string;
  startedAt: string;
  finishedAt: string | null;
  error: string | null;
  /** Most recent outcomes, newest first, capped by the runner. */
  outcomes: EmailSendJobOutcome[];
}
