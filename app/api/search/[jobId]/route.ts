import { handle, ok, parseJson } from '@/lib/api/response';
import { jobCommandSchema } from '@/lib/validation/search.schema';
import { jobManager } from '@/services/jobs/job-manager';

export const dynamic = 'force-dynamic';

interface RouteParams {
  params: Promise<{ jobId: string }>;
}

export function GET(_request: Request, { params }: RouteParams): Promise<Response> {
  return handle(async () => {
    const { jobId } = await params;
    return ok(jobManager.get(jobId).snapshot);
  });
}

/** Applies a pause / resume / stop command to a running job. */
export function POST(request: Request, { params }: RouteParams): Promise<Response> {
  return handle(async () => {
    const { jobId } = await params;
    const { command } = await parseJson(request, jobCommandSchema);
    return ok(jobManager.command(jobId, command));
  });
}

export function DELETE(_request: Request, { params }: RouteParams): Promise<Response> {
  return handle(async () => {
    const { jobId } = await params;
    return ok(jobManager.command(jobId, 'stop'));
  });
}
