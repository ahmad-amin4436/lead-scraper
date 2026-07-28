import { z } from 'zod';

import { BUSINESS_SOURCES } from '@/types/business';
import { isValidCategory } from '@/lib/constants/categories';
import { isValidCountry } from '@/lib/constants/locations';

export const searchRequestSchema = z.object({
  categories: z
    .array(z.string())
    .min(1, { error: 'Select at least one category' })
    .max(20, { error: 'Select at most 20 categories' })
    .refine((ids) => ids.every(isValidCategory), { error: 'Unknown category selected' }),
  country: z
    .string()
    .min(2)
    .refine(isValidCountry, { error: 'Unknown country code' }),
  state: z.string().max(120).optional(),
  cities: z
    .array(z.string().trim().min(1).max(120))
    .min(1, { error: 'Add at least one city' })
    .max(25, { error: 'Add at most 25 cities' }),
  radiusMeters: z.number().int().min(500).max(50000),
  maxResults: z.number().int().min(1).max(500),
  enrichContacts: z.boolean(),
  skipDuplicates: z.boolean(),
  minRating: z.number().min(0).max(5).optional(),
  minReviews: z.number().int().min(0).max(100000).optional(),
  provider: z.enum(BUSINESS_SOURCES).optional(),
});

export type SearchRequestInput = z.infer<typeof searchRequestSchema>;

export const jobCommandSchema = z.object({
  command: z.enum(['stop']),
});
