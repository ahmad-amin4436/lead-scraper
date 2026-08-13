import { z } from 'zod';

export const startLinkedInEnrichmentSchema = z.object({
  businessIds: z
    .array(z.string().min(1))
    .min(1, { error: 'Select at least one lead' })
    .max(25, { error: 'Select at most 25 leads' }),
  maxDecisionMakersPerCompany: z.number().int().min(1).max(50).optional(),
});

export type StartLinkedInEnrichmentInput = z.infer<typeof startLinkedInEnrichmentSchema>;

export const startPeopleSearchSchema = z.object({
  companyName: z.string().trim().min(1, { error: 'Enter a company name' }).max(256),
  keywords: z.string().trim().max(256).optional(),
  location: z.string().trim().max(128).optional(),
  maxResults: z.number().int().min(1).max(100).optional(),
});

export type StartPeopleSearchInput = z.infer<typeof startPeopleSearchSchema>;

export const linkedInLoginSchema = z.object({
  linkedInEmail: z.string().trim().min(1, { error: 'Enter your LinkedIn email' }).max(256),
  linkedInPassword: z.string().min(1, { error: 'Enter your LinkedIn password' }).max(256),
});

export type LinkedInLoginInput = z.infer<typeof linkedInLoginSchema>;

export const linkedInLoginVerifySchema = z.object({
  code: z.string().trim().min(1, { error: 'Enter the verification code' }).max(32),
});

export type LinkedInLoginVerifyInput = z.infer<typeof linkedInLoginVerifySchema>;
