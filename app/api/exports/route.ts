import { handle, ok, parseJson } from '@/lib/api/response';
import { exportRequestSchema } from '@/lib/validation/export.schema';
import { exportRepository } from '@/repositories/export.repository';
import { exportService } from '@/services/export/export.service';

export const dynamic = 'force-dynamic';

export function GET(): Promise<Response> {
  return handle(async () => ok(await exportRepository.list()));
}

/** Generates an export file and returns its history record. */
export function POST(request: Request): Promise<Response> {
  return handle(async () => {
    const input = await parseJson(request, exportRequestSchema);
    const { record } = await exportService.create(input);
    return ok(record, { status: 201 });
  });
}

export function DELETE(): Promise<Response> {
  return handle(async () => {
    await exportRepository.clear();
    return ok({ cleared: true });
  });
}
