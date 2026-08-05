import { z } from 'zod';

export const startLinkedInEnrichmentSchema = z.object({
  businessIds: z
    .array(z.string().min(1))
    .min(1, { error: 'Select at least one lead' })
    .max(25, { error: 'Select at most 25 leads' }),
  maxDecisionMakersPerCompany: z.number().int().min(1).max(50).optional(),
});

export type StartLinkedInEnrichmentInput = z.infer<typeof startLinkedInEnrichmentSchema>;
