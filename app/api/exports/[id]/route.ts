import { NotFoundError } from '@/lib/errors';
import { handle, ok } from '@/lib/api/response';
import { exportRepository } from '@/repositories/export.repository';
import { exportService, mimeTypeFor } from '@/services/export/export.service';

export const dynamic = 'force-dynamic';

interface RouteParams {
  params: Promise<{ id: string }>;
}

/** Streams a previously generated export back as a file download. */
export async function GET(_request: Request, { params }: RouteParams): Promise<Response> {
  const { id } = await params;
  const found = await exportService.read(id);

  if (!found) {
    return Response.json(
      { ok: false, error: { code: 'not_found', message: 'Export not found or its file was removed.' } },
      { status: 404 },
    );
  }

  const { record, buffer } = found;

  return new Response(new Uint8Array(buffer), {
    headers: {
      'Content-Type': mimeTypeFor(record.format),
      'Content-Length': String(buffer.byteLength),
      'Content-Disposition': `attachment; filename="${record.fileName}"`,
      'Cache-Control': 'no-store',
    },
  });
}

export function DELETE(_request: Request, { params }: RouteParams): Promise<Response> {
  return handle(async () => {
    const { id } = await params;
    const removed = await exportRepository.remove(id);
    if (!removed) throw new NotFoundError('Export not found');
    return ok({ removed: true });
  });
}
