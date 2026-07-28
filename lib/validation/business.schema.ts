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

/**
 * Filter shape for JSON request bodies (e.g. the export route).
 *
 * `businessQuerySchema` above coerces from URL query strings, where every value
 * arrives as a string — so it uses `z.stringbool()` and `z.coerce.number()`. A
 * JSON body carries real booleans and numbers, so the same fields are typed
 * natively here. Both infer to the same `BusinessQuery` field types.
 */
export const businessJsonFilterSchema = z.object({
  search: z.string().trim().max(200).optional(),
  category: z.string().max(80).optional(),
  country: z.string().max(80).optional(),
  city: z.string().max(120).optional(),
  source: z.enum(BUSINESS_SOURCES).optional(),
  status: z.enum(BUSINESS_STATUSES).optional(),
  minRating: z.number().min(0).max(5).optional(),
  minReviews: z.number().int().min(0).optional(),
  hasEmail: z.boolean().optional(),
  hasPhone: z.boolean().optional(),
  hasWebsite: z.boolean().optional(),
  sortBy: sortKeySchema.optional(),
  sortDir: z.enum(['asc', 'desc']).optional(),
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
