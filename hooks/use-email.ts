'use client';

import {
  useMutation,
  useQuery,
  useQueryClient,
  type UseMutationResult,
  type UseQueryResult,
} from '@tanstack/react-query';

import { apiFetch, buildQueryString } from '@/lib/api-client';
import { toEmailSendJobSnapshot } from '@/lib/backend/email-send-job-mapper';
import type { BackendSearchJob } from '@/lib/backend/search-mapper';
import type { BackendPagedResult } from '@/lib/backend/types';
import type { EmailSendJobSnapshot } from '@/types/email-send-job';

// --- shapes mirroring the .NET DTOs ----------------------------------------

export interface EmailTemplate {
  id: string;
  name: string;
  description: string;
  subject: string;
  bodyHtml: string;
  bodyText: string;
  isActive: boolean;
  signatureId: string | null;
  signatureName: string | null;
  timesSent: number;
  createdAt: string;
}

export interface EmailSignature {
  id: string;
  name: string;
  bodyHtml: string;
  bodyText: string;
  ownerUserId: string | null;
  isDefault: boolean;
  isActive: boolean;
}

export type EmailSendStatus = 'Queued' | 'Sent' | 'Failed';

export interface EmailLogEntry {
  id: number;
  senderUserId: string | null;
  senderEmail: string;
  senderName: string;
  toEmail: string;
  toName: string;
  subject: string;
  bodyHtml: string;
  templateName: string;
  businessId: string | null;
  status: EmailSendStatus;
  error: string | null;
  createdAt: string;
  sentAt: string | null;
}

export interface EmailLogQuery {
  senderUserId?: string;
  search?: string;
  templateId?: string;
  status?: EmailSendStatus;
  from?: string;
  to?: string;
  page?: number;
  pageSize?: number;
}

export interface EmailStats {
  totalSent: number;
  totalFailed: number;
  sentToday: number;
  sentLast7Days: number;
  bySender: { label: string; count: number }[];
  byTemplate: { label: string; count: number }[];
}

export interface SendEmailInput {
  templateId: string;
  businessIds: string[];
  signatureId?: string;
  cc?: string;
  bcc?: string;
  dryRun?: boolean;
}

export interface SendEmailResult {
  requested: number;
  sent: number;
  failed: number;
  skipped: number;
  outcomes: { businessId: string; toEmail: string; status: string; error: string | null }[];
}

export interface EmailPreview {
  subject: string;
  bodyHtml: string;
  toEmail: string;
}

export const emailKeys = {
  templates: (activeOnly: boolean) => ['email', 'templates', activeOnly] as const,
  signatures: ['email', 'signatures'] as const,
  log: (query: EmailLogQuery) => ['email', 'log', query] as const,
  stats: (from?: string, to?: string) => ['email', 'stats', from, to] as const,
};

// --- templates & signatures -------------------------------------------------

export function useEmailTemplates(activeOnly = false): UseQueryResult<EmailTemplate[]> {
  return useQuery({
    queryKey: emailKeys.templates(activeOnly),
    queryFn: () =>
      apiFetch<EmailTemplate[]>(`/api/backend/email/templates${buildQueryString({ activeOnly })}`),
  });
}

export function useSaveEmailTemplate(): UseMutationResult<
  EmailTemplate,
  Error,
  { id?: string; body: Record<string, unknown> }
> {
  const client = useQueryClient();

  return useMutation({
    mutationFn: ({ id, body }) =>
      apiFetch<EmailTemplate>(
        id ? `/api/backend/email/templates/${id}` : '/api/backend/email/templates',
        { method: id ? 'PUT' : 'POST', body: JSON.stringify(body) },
      ),
    onSuccess: () => {
      void client.invalidateQueries({ queryKey: ['email', 'templates'] });
    },
  });
}

export function useDeleteEmailTemplate(): UseMutationResult<unknown, Error, string> {
  const client = useQueryClient();

  return useMutation({
    mutationFn: (id) => apiFetch(`/api/backend/email/templates/${id}`, { method: 'DELETE' }),
    onSuccess: () => {
      void client.invalidateQueries({ queryKey: ['email', 'templates'] });
    },
  });
}

export function useEmailSignatures(): UseQueryResult<EmailSignature[]> {
  return useQuery({
    queryKey: emailKeys.signatures,
    queryFn: () => apiFetch<EmailSignature[]>('/api/backend/email/signatures'),
  });
}

export function useSaveEmailSignature(): UseMutationResult<
  EmailSignature,
  Error,
  { id?: string; body: Record<string, unknown> }
> {
  const client = useQueryClient();

  return useMutation({
    mutationFn: ({ id, body }) =>
      apiFetch<EmailSignature>(
        id ? `/api/backend/email/signatures/${id}` : '/api/backend/email/signatures',
        { method: id ? 'PUT' : 'POST', body: JSON.stringify(body) },
      ),
    onSuccess: () => {
      void client.invalidateQueries({ queryKey: emailKeys.signatures });
    },
  });
}

// --- sending ----------------------------------------------------------------

export function useSendEmail(): UseMutationResult<SendEmailResult, Error, SendEmailInput> {
  const client = useQueryClient();

  return useMutation({
    mutationFn: (input) =>
      apiFetch<SendEmailResult>('/api/backend/email/send', {
        method: 'POST',
        body: JSON.stringify(input),
      }),
    onSuccess: () => {
      // A send stamps LastContactedAt on the lead, so both views are stale.
      void client.invalidateQueries({ queryKey: ['email', 'log'] });
      void client.invalidateQueries({ queryKey: ['email', 'stats'] });
      void client.invalidateQueries({ queryKey: ['leads'] });
    },
  });
}

// --- sending as a background job (what the Send Email page actually uses) --
// useSendEmail above blocks on the whole batch inline and can 504 through the
// Next.js proxy (a standard Netlify Function, ~10-26s) once paced sequential
// sends run long — confirmed live. This queues the batch instead and polls
// it, the same fire-and-forget-then-poll shape LinkedIn enrichment uses.

const emailSendJobKeys = {
  active: ['email-send-jobs'] as const,
};

export function useStartEmailSendJob(): UseMutationResult<EmailSendJobSnapshot, Error, SendEmailInput> {
  const client = useQueryClient();

  return useMutation({
    mutationFn: (input) =>
      apiFetch<BackendSearchJob>('/api/backend/email/send-jobs', {
        method: 'POST',
        body: JSON.stringify(input),
      }).then(toEmailSendJobSnapshot),
    onSuccess: () => {
      void client.invalidateQueries({ queryKey: emailSendJobKeys.active });
    },
  });
}

/** Batches that are queued, running or stopping — the caller's own. */
export function useActiveEmailSendJobs(): UseQueryResult<EmailSendJobSnapshot[]> {
  return useQuery({
    queryKey: emailSendJobKeys.active,
    queryFn: () =>
      apiFetch<BackendSearchJob[]>('/api/backend/email/send-jobs/active').then((jobs) =>
        jobs.map(toEmailSendJobSnapshot),
      ),
  });
}

export function useEmailSendJobCommand(): UseMutationResult<
  EmailSendJobSnapshot,
  Error,
  { jobId: string; command: 'stop' }
> {
  return useMutation({
    mutationFn: ({ jobId }) =>
      apiFetch<BackendSearchJob>(`/api/backend/email/send-jobs/${jobId}/stop`, {
        method: 'POST',
      }).then(toEmailSendJobSnapshot),
  });
}

export function useEmailPreview(): UseMutationResult<
  EmailPreview,
  Error,
  { templateId: string; businessId?: string; signatureId?: string }
> {
  return useMutation({
    mutationFn: (input) =>
      apiFetch<EmailPreview>('/api/backend/email/preview', {
        method: 'POST',
        body: JSON.stringify(input),
      }),
  });
}

// --- audit ------------------------------------------------------------------

export function useEmailLog(query: EmailLogQuery): UseQueryResult<BackendPagedResult<EmailLogEntry>> {
  return useQuery({
    queryKey: emailKeys.log(query),
    queryFn: () =>
      apiFetch<BackendPagedResult<EmailLogEntry>>(
        `/api/backend/email/log${buildQueryString(query as Record<string, unknown>)}`,
      ),
    placeholderData: (previous) => previous,
  });
}

export function useEmailStats(from?: string, to?: string): UseQueryResult<EmailStats> {
  return useQuery({
    queryKey: emailKeys.stats(from, to),
    queryFn: () => apiFetch<EmailStats>(`/api/backend/email/stats${buildQueryString({ from, to })}`),
  });
}
