import { handle, ok, parseJson } from '@/lib/api/response';
import { settingsSchema } from '@/lib/validation/settings.schema';
import { settingsRepository } from '@/repositories/settings.repository';
import { logger } from '@/services/logging/logger.service';
import { listProviders } from '@/services/search/provider-registry';

export const dynamic = 'force-dynamic';

export function GET(): Promise<Response> {
  return handle(async () => {
    const [settings, full] = await Promise.all([
      settingsRepository.getPublic(),
      settingsRepository.get(),
    ]);

    return ok({ settings, providers: listProviders(full) });
  });
}

export function PUT(request: Request): Promise<Response> {
  return handle(async () => {
    const input = await parseJson(request, settingsSchema);
    await settingsRepository.update(input);

    await logger.info('settings.updated', 'Settings updated', {
      context: {
        provider: input.defaultProvider,
        concurrency: input.concurrency,
        respectRobotsTxt: input.respectRobotsTxt,
      },
    });

    const [settings, full] = await Promise.all([
      settingsRepository.getPublic(),
      settingsRepository.get(),
    ]);

    return ok({ settings, providers: listProviders(full) });
  });
}

/** Clears the stored API key without touching the rest of the settings. */
export function DELETE(): Promise<Response> {
  return handle(async () => {
    await settingsRepository.clearApiKey();
    return ok(await settingsRepository.getPublic());
  });
}
