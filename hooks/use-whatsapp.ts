'use client';

import {
  useMutation,
  useQuery,
  useQueryClient,
  type UseMutationResult,
  type UseQueryResult,
} from '@tanstack/react-query';

import { apiFetch, buildQueryString } from '@/lib/api-client';

// --- shapes mirroring the .NET DTOs ----------------------------------------

export interface WhatsAppTemplate {
  id: string;
  name: string;
  description: string;
  message: string;
  isActive: boolean;
  timesUsed: number;
  createdAt: string;
}

export interface GenerateWhatsAppLinksInput {
  templateId: string;
  businessIds: string[];
}

export interface WhatsAppLink {
  businessId: string;
  businessName: string;
  phone: string;
  message: string;
  url: string;
  skipReason: string | null;
}

export interface GenerateWhatsAppLinksResult {
  requested: number;
  links: WhatsAppLink[];
}

export interface WhatsAppPreview {
  message: string;
  toPhone: string;
}

export interface MarkWhatsAppContactedInput {
  businessId: string;
  templateId?: string;
  message?: string;
}

export const whatsAppKeys = {
  templates: (activeOnly: boolean) => ['whatsapp', 'templates', activeOnly] as const,
};

// --- templates ----------------------------------------------------------------

export function useWhatsAppTemplates(activeOnly = false): UseQueryResult<WhatsAppTemplate[]> {
  return useQuery({
    queryKey: whatsAppKeys.templates(activeOnly),
    queryFn: () =>
      apiFetch<WhatsAppTemplate[]>(`/api/backend/whatsapp/templates${buildQueryString({ activeOnly })}`),
  });
}

export function useSaveWhatsAppTemplate(): UseMutationResult<
  WhatsAppTemplate,
  Error,
  { id?: string; body: Record<string, unknown> }
> {
  const client = useQueryClient();

  return useMutation({
    mutationFn: ({ id, body }) =>
      apiFetch<WhatsAppTemplate>(
        id ? `/api/backend/whatsapp/templates/${id}` : '/api/backend/whatsapp/templates',
        { method: id ? 'PUT' : 'POST', body: JSON.stringify(body) },
      ),
    onSuccess: () => {
      void client.invalidateQueries({ queryKey: ['whatsapp', 'templates'] });
    },
  });
}

export function useDeleteWhatsAppTemplate(): UseMutationResult<unknown, Error, string> {
  const client = useQueryClient();

  return useMutation({
    mutationFn: (id) => apiFetch(`/api/backend/whatsapp/templates/${id}`, { method: 'DELETE' }),
    onSuccess: () => {
      void client.invalidateQueries({ queryKey: ['whatsapp', 'templates'] });
    },
  });
}

// --- click-to-chat links --------------------------------------------------------

export function useGenerateWhatsAppLinks(): UseMutationResult<
  GenerateWhatsAppLinksResult,
  Error,
  GenerateWhatsAppLinksInput
> {
  return useMutation({
    mutationFn: (input) =>
      apiFetch<GenerateWhatsAppLinksResult>('/api/backend/whatsapp/links', {
        method: 'POST',
        body: JSON.stringify(input),
      }),
  });
}

export function useWhatsAppPreview(): UseMutationResult<
  WhatsAppPreview,
  Error,
  { templateId: string; businessId?: string }
> {
  return useMutation({
    mutationFn: (input) =>
      apiFetch<WhatsAppPreview>('/api/backend/whatsapp/preview', {
        method: 'POST',
        body: JSON.stringify(input),
      }),
  });
}

// Records that a generated link was actually opened. Called right before
// window.open() so a click that never fires still leaves no record.
export function useMarkWhatsAppContacted(): UseMutationResult<
  unknown,
  Error,
  MarkWhatsAppContactedInput
> {
  const client = useQueryClient();

  return useMutation({
    mutationFn: (input) =>
      apiFetch('/api/backend/whatsapp/mark-contacted', {
        method: 'POST',
        body: JSON.stringify(input),
      }),
    onSuccess: () => {
      void client.invalidateQueries({ queryKey: ['leads'] });
    },
  });
}
