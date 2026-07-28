import { z } from 'zod';

import { EXPORT_FORMATS, EXPORT_TEMPLATES } from '@/types/export';
import { businessJsonFilterSchema } from './business.schema';

export const exportRequestSchema = z.object({
  format: z.enum(EXPORT_FORMATS),
  template: z.enum(EXPORT_TEMPLATES).default('leadmine'),
  filters: businessJsonFilterSchema.partial().default({}),
  ids: z.array(z.string().min(1)).max(50000).optional(),
});

export type ExportRequestInput = z.infer<typeof exportRequestSchema>;
