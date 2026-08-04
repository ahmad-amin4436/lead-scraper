'use client';

import { useMutation, useQueryClient, type UseMutationResult } from '@tanstack/react-query';

import { apiFetch } from '@/lib/api-client';

// --- shapes mirroring the .NET DTOs ----------------------------------------

export interface LinkedInEnrichmentOutcome {
  businessId: string;
  businessName: string;
  companyEnriched: boolean;
  peopleFound: number;
  error: string | null;
}

export interface EnrichLeadsWithLinkedInResult {
  requested: number;
  enriched: number;
  peopleFound: number;
  failed: number;
  outcomes: LinkedInEnrichmentOutcome[];
}

export interface EnrichLeadsWithLinkedInInput {
  businessIds: string[];
  maxDecisionMakersPerCompany?: number;
}

export function useEnrichLeadsWithLinkedIn(): UseMutationResult<
  EnrichLeadsWithLinkedInResult,
  Error,
  EnrichLeadsWithLinkedInInput
> {
  const client = useQueryClient();

  return useMutation({
    mutationFn: (input) =>
      apiFetch<EnrichLeadsWithLinkedInResult>('/api/backend/linkedin/enrich', {
        method: 'POST',
        body: JSON.stringify(input),
      }),
    onSuccess: () => {
      // Enrichment updates Business rows (industry, employee count, LastLinkedInEnrichedAt)
      // and inserts new People rows.
      void client.invalidateQueries({ queryKey: ['leads'] });
      void client.invalidateQueries({ queryKey: ['people'] });
    },
  });
}
