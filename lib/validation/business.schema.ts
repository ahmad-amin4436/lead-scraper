import { z } from 'zod';

import {
  BUSINESS_COLUMNS,
  BUSINESS_SOURCES,
  BUSINESS_STATUSES,
  type BusinessColumnKey,
} from '@/types/business';

// z.enum needs a non-empty tuple; the assertion preserves the literal union
// so `sortBy` stays typed as BusinessColumnKey rather than widening to string.
const columnKeys = BUSINESS_COLUMNS.map((c) => c.key) as [
  BusinessColumnKey,
  ...BusinessColumnKey[],
];
const sortKeySchema = z.enum(columnKeys);

/** Coerces query-string values, which always arrive as strings. */
export const businessQuerySchema = z.object({
  search: z.string().trim().max(200).optional(),
  category: z.string().max(80).optional(),
  country: z.string().max(80).optional(),
  city: z.string().max(120).optional(),
  source: z.enum(BUSINESS_SOURCES).optional(),
  status: z.enum(BUSINESS_STATUSES).optional(),
  minRating: z.coerce.number().min(0).max(5).optional(),
  minReviews: z.coerce.number().int().min(0).optional(),
  hasEmail: z.stringbool().optional(),
  hasPhone: z.stringbool().optional(),
  hasWebsite: z.stringbool().optional(),
  sortBy: sortKeySchema.optional(),
  sortDir: z.enum(['asc', 'desc']).optional(),
  page: z.coerce.number().int().min(1).default(1),
  pageSize: z.coerce.number().int().min(1).max(500).default(25),
});

export const businessUpdateSchema = z.object({
  status: z.enum(BUSINESS_STATUSES).optional(),
  notes: z.string().max(2000).optional(),
  email: z.union([z.email(), z.literal('')]).optional(),
  phone: z.string().max(60).optional(),
});

export const businessDeleteSchema = z.object({
  ids: z.array(z.string().min(1)).min(1).max(5000),
});
