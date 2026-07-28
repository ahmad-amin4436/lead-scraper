import { z } from 'zod';

import { BUSINESS_SOURCES } from '@/types/business';

export const settingsSchema = z.object({
  /** Empty string means "leave unchanged" when the key is already stored. */
  googleApiKey: z.string().trim().max(200).optional(),
  defaultProvider: z.enum(BUSINESS_SOURCES),
  crawlerContactEmail: z.union([z.email(), z.literal('')]),
  concurrency: z.number().int().min(1).max(16),
  delayMs: z.number().int().min(0).max(60000),
  rateLimitPerMinute: z.number().int().min(1).max(600),
  retryAttempts: z.number().int().min(1).max(10),
  requestTimeoutMs: z.number().int().min(1000).max(120000),
  maxPagesPerSite: z.number().int().min(1).max(12),
  respectRobotsTxt: z.boolean(),
  enrichmentEnabledByDefault: z.boolean(),
  skipDuplicatesByDefault: z.boolean(),
  defaultRadiusMeters: z.number().int().min(500).max(50000),
  defaultMaxResults: z.number().int().min(1).max(500),
});

export type SettingsInput = z.infer<typeof settingsSchema>;
