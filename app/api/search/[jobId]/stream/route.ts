import { jobManager } from '@/services/jobs/job-manager';
import type { JobEvent } from '@/types/job';

export const dynamic = 'force-dynamic';

/** Keeps proxies from closing an idle connection. */
const HEARTBEAT_MS = 15_000;

interface RouteParams {
  params: Promise<{ jobId: string }>;
}

/**
 * Server-Sent Events stream of live job progress.
 *
 * Note: this requires a long-lived connection to the same process that owns the
 * job. It works under `next start`, Docker, or any Node host, but not on a
 * serverless platform where each request may hit a different, short-lived
 * instance — see the deployment notes in the README.
 */
export async function GET(_request: Request, { params }: RouteParams): Promise<Response> {
  const { jobId } = await params;
  const job = jobManager.find(jobId);

  if (!job) {
    return Response.json(
      { ok: false, error: { code: 'not_found', message: `No job with id "${jobId}"` } },
      { status: 404 },
    );
  }

  const encoder = new TextEncoder();
  let unsubscribe: (() => void) | null = null;
  let heartbeat: NodeJS.Timeout | null = null;

  const stream = new ReadableStream<Uint8Array>({
    start(controller) {
      let closed = false;

      const cleanup = (): void => {
        if (closed) return;
        closed = true;
        if (heartbeat) clearInterval(heartbeat);
        unsubscribe?.();
        try {
          controller.close();
        } catch {
          // Already closed by the client disconnecting.
        }
      };

      const send = (payload: string): void => {
        if (closed) return;
        try {
          controller.enqueue(encoder.encode(payload));
        } catch {
          cleanup();
        }
      };

      unsubscribe = job.subscribe((event: JobEvent) => {
        send(`event: ${event.type}\ndata: ${JSON.stringify(event)}\n\n`);
        if (event.type === 'done') {
          // Give the client a tick to process the final event before closing.
          setTimeout(cleanup, 50);
        }
      });

      heartbeat = setInterval(() => send(': keep-alive\n\n'), HEARTBEAT_MS);
      heartbeat.unref?.();

      // The job may already have finished between find() and subscribe().
      if (job.isTerminal) setTimeout(cleanup, 50);
    },

    cancel() {
      if (heartbeat) clearInterval(heartbeat);
      unsubscribe?.();
    },
  });

  return new Response(stream, {
    headers: {
      'Content-Type': 'text/event-stream; charset=utf-8',
      'Cache-Control': 'no-cache, no-transform',
      Connection: 'keep-alive',
      // Tell nginx not to buffer the stream.
      'X-Accel-Buffering': 'no',
    },
  });
}
