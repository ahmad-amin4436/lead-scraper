import { z } from 'zod';

import { LOG_EVENTS, LOG_LEVELS } from '@/types/log';

export const logQuerySchema = z.object({
  level: z.enum(LOG_LEVELS).optional(),
  event: z.enum(LOG_EVENTS).optional(),
  jobId: z.string().max(80).optional(),
  search: z.string().trim().max(200).optional(),
  page: z.coerce.number().int().min(1).default(1),
  pageSize: z.coerce.number().int().min(1).max(500).default(50),
});
