import { z } from 'zod';

import { BUSINESS_SOURCES } from '@/types/business';
import { isValidCategory } from '@/lib/constants/categories';
import { isValidCountryCode } from '@/lib/constants/geo';
import { LEAD_KINDS } from '@/lib/constants/lead-kinds';

export const searchRequestSchema = z.object({
  categories: z
    .array(z.string())
    .min(1, { error: 'Select at least one category' })
    .max(20, { error: 'Select at most 20 categories' })
    .refine((ids) => ids.every(isValidCategory), { error: 'Unknown category selected' }),
  // Validated against the full ISO 3166 list, so any country is accepted.
  country: z
    .string()
    .min(2)
    .refine(isValidCountryCode, { error: 'Unknown country code' }),
  state: z.string().max(120).optional(),
  cities: z
    .array(z.string().trim().min(1).max(120))
    .min(1, { error: 'Add at least one city' })
    .max(25, { error: 'Add at most 25 cities' }),
  radiusMeters: z
    .number({ error: 'Enter a radius in metres' })
    .int({ error: 'Radius must be a whole number' })
    .min(500, { error: 'Radius must be at least 500 m' })
    .max(50000, { error: 'Radius cannot exceed 50,000 m' }),
  maxResults: z
    .number({ error: 'Enter how many results to fetch' })
    .int({ error: 'Max results must be a whole number' })
    .min(1, { error: 'Fetch at least 1 result' })
    .max(500, { error: 'Cannot exceed 500 results per search' }),
  enrichContacts: z.boolean(),
  skipDuplicates: z.boolean(),
  /**
   * Which quality of lead to keep. See `LeadKind` on the backend.
   *
   * Required rather than `.default()`: a default makes the schema's input and
   * output types differ, which react-hook-form's resolver cannot reconcile. The
   * form always supplies a value, so the default bought nothing.
   */
  leadKind: z.enum(LEAD_KINDS),
  minRating: z.number().min(0).max(5).optional(),
  minReviews: z.number().int().min(0).max(100000).optional(),
  provider: z.enum(BUSINESS_SOURCES).optional(),
});

export type SearchRequestInput = z.infer<typeof searchRequestSchema>;

/**
 * A relaxed version of {@link searchRequestSchema} for values persisted to
 * `localStorage` between visits. The real schema requires at least one
 * category/city because that's a submission rule — but a saved draft may
 * legitimately have neither yet (e.g. the user changed country and hasn't
 * repicked a city). Every field is optional so a partially-corrupt or
 * out-of-date record still yields whatever parts remain valid.
 */
export const searchFormFiltersSchema = z.object({
  categories: z.array(z.string()).max(20).optional(),
  country: z.string().min(2).optional(),
  state: z.string().max(120).optional(),
  cities: z.array(z.string().trim().min(1).max(120)).max(25).optional(),
  radiusMeters: z.number().int().min(500).max(50000).optional(),
  maxResults: z.number().int().min(1).max(500).optional(),
  enrichContacts: z.boolean().optional(),
  skipDuplicates: z.boolean().optional(),
  leadKind: z.enum(LEAD_KINDS).optional(),
  minRating: z.number().min(0).max(5).optional(),
  minReviews: z.number().int().min(0).max(100000).optional(),
  provider: z.enum(BUSINESS_SOURCES).optional(),
});

export type SearchFormFilters = z.infer<typeof searchFormFiltersSchema>;

export const jobCommandSchema = z.object({
  command: z.enum(['stop']),
});
