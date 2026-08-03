/**
 * LeadMine scraper worker.
 *
 * A long-running process that claims queued search runs from the database and
 * executes them. It exists because serverless functions cannot host this work:
 * Netlify Background Functions are killed at 15 minutes, so a large sweep was
 * always going to die part-way through.
 *
 * Reliability model:
 *   - Jobs live in SQL, not process memory, so nothing is lost if this dies.
 *   - A claim is a lease. Progress heartbeats extend it; if the process stops,
 *     the lease lapses and the API's reaper returns the job to the queue.
 *   - Every finished task is checkpointed, so a resumed run continues instead of
 *     starting over.
 *   - Several workers can run at once; the claim is atomic, so a job is only
 *     ever executed by one of them.
 *
 * Run it under a supervisor that restarts on exit (Docker `restart: always`,
 * systemd, PM2). Combined with the lease, that is what "never down" means here:
 * the worker is disposable and the queue is durable.
 */

import { hostname } from 'node:os';

import { runQueuedSearch } from './run-search';
import { JobClient, describe, sleep } from './job-client';

const BASE_URL = (process.env.LEADMINE_API_URL ?? 'http://localhost:5080').replace(/\/+$/, '');
const SERVICE_KEY = process.env.LEADMINE_SERVICE_KEY?.trim() ?? '';

/** Lease length. Long enough to ride out a slow task, short enough to recover fast. */
const LEASE_SECONDS = Number(process.env.WORKER_LEASE_SECONDS ?? 120);

/** Pause between polls when the queue is empty. */
const IDLE_POLL_MS = Number(process.env.WORKER_IDLE_POLL_MS ?? 5000);

/** How many runs this process executes at once. */
const CONCURRENCY = Math.max(1, Number(process.env.WORKER_CONCURRENCY ?? 1));

const WORKER_ID = process.env.WORKER_ID ?? `${hostname()}-${process.pid}`;

let shuttingDown = false;

async function main(): Promise<void> {
  if (!SERVICE_KEY) {
    console.error(
      '[worker] LEADMINE_SERVICE_KEY is not set. It must match ServiceKey in the backend configuration.',
    );
    process.exit(1);
  }

  console.log(`[worker] ${WORKER_ID} starting`);
  console.log(`[worker] api=${BASE_URL} lease=${LEASE_SECONDS}s concurrency=${CONCURRENCY}`);

  const client = new JobClient({
    baseUrl: BASE_URL,
    serviceKey: SERVICE_KEY,
    workerId: WORKER_ID,
    leaseSeconds: LEASE_SECONDS,
  });

  // Stop claiming new work on a signal, but let in-flight runs finish so their
  // progress is checkpointed rather than abandoned.
  for (const signal of ['SIGINT', 'SIGTERM'] as const) {
    process.on(signal, () => {
      if (shuttingDown) {
        console.warn('[worker] second signal — exiting now');
        process.exit(1);
      }

      console.log(`[worker] ${signal} received; finishing current work then exiting`);
      shuttingDown = true;
    });
  }

  // An unhandled rejection must not silently wedge a lane; log and keep going.
  process.on('unhandledRejection', (reason) => {
    console.error('[worker] unhandled rejection:', describe(reason));
  });

  const lanes = Array.from({ length: CONCURRENCY }, (_, index) => lane(client, index));
  await Promise.all(lanes);

  console.log('[worker] stopped');
}

/** One claim-and-run loop. Several run in parallel for concurrency > 1. */
async function lane(client: JobClient, index: number): Promise<void> {
  // Stagger start-up so lanes don't all hammer the claim endpoint together.
  await sleep(index * 250);

  let backoff = IDLE_POLL_MS;

  while (!shuttingDown) {
    try {
      const job = await client.claim();

      if (!job) {
        await sleep(backoff);
        continue;
      }

      // Reset after a successful claim: the API is clearly healthy.
      backoff = IDLE_POLL_MS;

      const resuming = job.completedTaskKeys.length > 0;
      console.log(
        `[worker] lane ${index} ${resuming ? 'resuming' : 'starting'} job ${job.id}` +
          (resuming ? ` (${job.completedTaskKeys.length} task(s) already done)` : ''),
      );

      await runQueuedSearch(client, job);
    } catch (error) {
      // Back off when the API is unreachable, so a restart doesn't get hammered.
      console.error(`[worker] lane ${index} error:`, describe(error));
      backoff = Math.min(60_000, backoff * 2);
      await sleep(backoff);
    }
  }
}

void main().catch((error: unknown) => {
  console.error('[worker] fatal:', describe(error));
  // Non-zero so the supervisor restarts us.
  process.exit(1);
});
