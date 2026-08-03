# Scraper worker

A long-running process that executes queued search runs.

```bash
npm run worker          # local, reads .env.local
docker compose up -d    # api + worker together
```

---

## Why a worker at all

The scraper used to run inside a Netlify Background Function. Those are killed at
**15 minutes**, so any sweep bigger than that died part-way through and there was
no mechanism to pick it back up. No amount of retry logic fixes a process that
gets terminated.

So the work moved to a process that is allowed to run as long as it needs, and
the queue moved into SQL Server where it survives the process entirely.

## How "never down" actually works

Nothing here assumes the worker stays alive. It assumes the opposite, and makes
that survivable:

| Failure | What happens |
| --- | --- |
| Worker crashes | Its lease stops being renewed. The API's reaper returns the job to the queue within ~30s of expiry. |
| Worker is restarted / redeployed | Same path. In-flight progress was already checkpointed. |
| Two workers race for a job | The claim is a guarded `UPDATE`; exactly one wins and the loser takes the next job. |
| A worker wakes up after losing its lease | Its heartbeat is rejected, it aborts, and it is refused permission to finish the run. |
| API restarts | The worker retries with backoff and carries on. |
| A job keeps failing | After 5 attempts it is failed with an explanation, so a poison run can't occupy the queue forever. |

The two halves both matter: the **lease** makes a dead worker recoverable, and
the **supervisor** (`restart: unless-stopped`) makes a dead worker come back.
Either alone leaves a gap.

### Resume, not restart

Progress is checkpointed per `city|category` task. A worker that picks up a
partly-finished run skips what is already done:

```
CompletedTaskKeysJson: ["Sligo|florist","Sligo|pharmacy"]
```

Leads are written to the database **before** the checkpoint, so a crash between
the two re-runs one task rather than losing its leads. Duplicate detection then
absorbs the overlap — the resume is idempotent by construction.

### Verified

A run was hard-killed (`Stop-Process -Force`, no graceful shutdown) at 2 of 4
tasks with 6 leads saved. The reaper released it, another worker resumed it, and
it finished all 4 tasks with **11 leads and 11 distinct** — the resume produced no
duplicates.

---

## Configuration

| Variable | Default | Notes |
| --- | --- | --- |
| `LEADMINE_API_URL` | `http://localhost:5080` | Where the API lives. |
| `LEADMINE_SERVICE_KEY` | — | **Required.** Must match `ServiceKey` in the backend. |
| `WORKER_ID` | `<host>-<pid>` | Lease owner. Leave it unless you need stable identity. |
| `WORKER_LEASE_SECONDS` | `120` | Shorter recovers faster; longer tolerates slower tasks. |
| `WORKER_CONCURRENCY` | `1` | Runs executed in parallel by this process. |
| `WORKER_IDLE_POLL_MS` | `5000` | Poll interval when the queue is empty. |
| `GOOGLE_PLACES_API_KEY` | — | Optional; OpenStreetMap needs no key. |

## Scaling

```bash
docker compose up -d --scale worker=3
```

Claims are atomic, so no job is ever executed by two workers. Add workers to run
more searches concurrently, not to make one search faster — a single run is
paced deliberately to respect provider rate limits and `robots.txt`.

## Shutdown

`SIGTERM` stops the worker claiming new jobs but lets the current task finish and
checkpoint, so a rolling restart loses no progress. A second signal exits
immediately.
