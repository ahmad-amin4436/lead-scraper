# LeadMine API

.NET 8 Web API backend for LeadMine, using **EF Core Code First** against SQL Server,
with JWT authentication and permission-based authorization.

---

## Why Code First (and not Database First)

Code First is the right fit here, and it isn't a close call:

- There is **no existing schema** to reverse-engineer. Database First earns its keep when
  you're wrapping a legacy database you don't control; that isn't the situation.
- **ASP.NET Core Identity ships as entities**, not as a schema. Trying to hand-author its
  tables and then scaffold them backwards is strictly more work and easy to get subtly wrong.
- **Migrations are version-controlled and reviewable.** A schema change arrives in the same
  pull request as the code that needs it, and deploys the same way to every environment.
- **The C# model stays the source of truth**, so relationships, indexes and constraints live
  next to the code that depends on them.

Use Database First instead if you are ever pointed at a database owned by another team, where
the schema is an external contract you must conform to rather than define.

---

## Quick start

```bash
cd backend

# 1. Secrets — never committed. Adjust to your machine.
cd src/LeadMine.Api
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=.\SQLEXPRESS;Database=LeadMineDb;User Id=sa;Password=YOUR_PASSWORD;TrustServerCertificate=True;MultipleActiveResultSets=True"
dotnet user-secrets set "Jwt:Key" "$(openssl rand -base64 48)"
dotnet user-secrets set "Seed:AdminPassword" "Admin@12345"
cd ../..

# 2. Run. Migrations and seeding happen automatically at startup.
dotnet run --project src/LeadMine.Api
```

Swagger UI (Development only): `http://localhost:5080/swagger`

Migrations also apply on startup, so this is only needed to create a new one:

```bash
dotnet ef migrations add <Name> --project src/LeadMine.Infrastructure --startup-project src/LeadMine.Api --output-dir Persistence/Migrations
dotnet ef database update --project src/LeadMine.Infrastructure --startup-project src/LeadMine.Api
```

### Configuration

| Setting | Where | Notes |
| --- | --- | --- |
| `ConnectionStrings:DefaultConnection` | user-secrets / env | SQL Server connection. |
| `Jwt:Key` | user-secrets / env | **32+ characters.** Validated at startup, not first login. |
| `Jwt:AccessTokenMinutes` | appsettings | Default 15. |
| `Jwt:RefreshTokenDays` | appsettings | Default 7. |
| `Seed:AdminEmail` | appsettings | Default `admin@leadmine.local`. |
| `Seed:AdminPassword` | user-secrets / env | **No admin is created if unset** — a default here would be a published credential. |
| `Cors:AllowedOrigins` | appsettings | Defaults to the Next.js dev origin. |
| `Scraper:GoogleApiKey` | user-secrets / env | Optional. Empty ⇒ OpenStreetMap only, which needs no key. |
| `Scraper:*` | appsettings | Scraper tuning — see [The scraper](#the-scraper). |

`appsettings.json` intentionally contains **no secrets**. In production supply them as
environment variables (`ConnectionStrings__DefaultConnection`, `Jwt__Key`,
`Scraper__GoogleApiKey`, …).

---

## The scraper

The scraper runs **inside this API**, as a hosted `BackgroundService`. There is no separate
worker process, container or VPS: the deployment target is a .NET application host, so
anything that cannot run in-process cannot run at all.

```
POST /api/searches ──▶ SearchJobs row (Queued)
                            │
      ScraperWorkerService ──┤ claims it (lease + owner)
                            │
                     SearchRunner
                       ├─ geocode each city        (Google, else Nominatim)
                       ├─ per (city × category):
                       │    ├─ provider search     (Google Places / Overpass)
                       │    ├─ enrich websites     (robots.txt, contact pages)
                       │    ├─ verify              (MX lookup, phone line type)
                       │    ├─ save batch          ──▶ Businesses
                       │    └─ checkpoint task key ──▶ SearchJobs
                       └─ report Completed / Failed / Stopped
```

### Why this survives a restart

Queuing a run only writes a row, and every task is checkpointed as it finishes. So an app-pool
recycle, a deploy or a crash mid-sweep leaves a job that is still `Running` with a lease nobody
is renewing. `StaleJobReaperService` (every 30 s) returns it to `Queued`, a worker re-claims it,
and `SearchRunner` **skips the task keys already recorded** — it resumes rather than restarts.
A job that keeps dying is failed after five attempts rather than occupying the queue forever.

Host shutdown is handled separately from a crash: the runner leaves the job non-terminal on
`SIGTERM` instead of reporting a status, so a routine restart does not turn into a failed run.

### Settings

| Setting | Default | Notes |
| --- | --- | --- |
| `WorkerEnabled` | `true` | Set `false` on instances that should only serve HTTP. |
| `Concurrency` | `1` | Runs executed at once. This process also serves requests. |
| `LeaseSeconds` | `120` | Lower ⇒ faster recovery after a recycle; higher ⇒ tolerates slower tasks. |
| `EnrichmentConcurrency` | `4` | Websites crawled in parallel. |
| `DelayMs` | `400` | Pause between requests to the same host. |
| `RateLimitPerMinute` | `120` | Cap on provider calls, to stay inside quota. |
| `MaxPagesPerSite` | `4` | Homepage plus linked contact/about pages. |
| `RespectRobotsTxt` | `true` | Leave on unless you own the sites being crawled. |
| `CrawlerContactEmail` | — | Advertised in the User-Agent so site owners can reach you. |
| `SaveBatchSize` | `25` | Leads buffered before a write. |

### Deploying to an application host

- **Enable Always On** (or your host's equivalent). The worker is a `BackgroundService`: if the
  host idles the process out, queued runs simply wait until the next request wakes it. Nothing
  is lost — the reaper and the checkpoint make that safe — but a run can stall for as long as
  the app stays cold.
- **Two instances behind a load balancer is supported.** Claims are leased per job with a
  guarded `UPDATE`, so exactly one worker runs a given search. Set `WorkerEnabled: false` if you
  would rather keep scraping off a particular instance.
- **Outbound HTTPS must be allowed** to the search providers and to arbitrary business websites.
  The crawler resolves every hostname and refuses private, loopback, link-local and
  cloud-metadata addresses before connecting — it runs in the same process as the API and on the
  same network as the database, so SSRF is a real risk rather than a theoretical one.
- **Google Places is optional.** With no key the app runs on OpenStreetMap, which needs none but
  carries no ratings or review counts (so the rating/review filters do nothing there). Community
  Overpass mirrors also throttle heavy use: a task that fails for that reason is checkpointed and
  the sweep continues.

### What the crawler will not do

It fetches the homepage and a few linked contact/about pages over plain HTTP, reads what the
site already publishes to an anonymous visitor, and stops. It honours robots.txt, identifies
itself, paces requests, and never authenticates, submits a form, or works around any access
control. A page behind a login is simply not read. Only publicly listed contact details are
stored.

---

## Architecture

Clean Architecture, dependencies pointing inward only:

```
LeadMine.Domain          entities + enums. No EF, no ASP.NET (bar Identity base classes).
LeadMine.Application     DTOs, Result<T>, service contracts, the permission catalogue.
LeadMine.Infrastructure  EF Core, Identity, JWT, authorization handler, service impls.
LeadMine.Api             controllers, middleware, composition root.
```

`Api → Infrastructure → Application → Domain`. Nothing points back the other way.

---

## Authentication

Short-lived JWT access tokens plus rotating refresh tokens.

| Endpoint | Purpose |
| --- | --- |
| `POST /api/auth/register` | Self-service signup; lands in `Viewer`. |
| `POST /api/auth/login` | Returns an access + refresh pair. |
| `POST /api/auth/refresh` | Rotates the refresh token. |
| `POST /api/auth/logout` | Revokes a refresh token. |
| `GET  /api/auth/me` | Current user with effective roles and permissions. |
| `POST /api/auth/change-password` | Ends all other sessions. |

Decisions worth knowing:

- **Refresh tokens are stored hashed** (SHA-256). A database leak cannot be replayed. They are
  256-bit CSPRNG values, so there is nothing to brute-force and the hash can stay unsalted —
  which is what makes the indexed O(1) lookup on refresh possible.
- **Rotation with theft detection.** Each refresh issues a new token and marks the old one
  replaced. Presenting an *already-used* token means it was captured, so the **entire family is
  revoked**, not just that token.
- **`SecurityVersion` invalidates live tokens.** A signed JWT cannot normally be recalled before
  it expires. Changing a password, roles, permissions, or deactivating an account bumps this
  counter, and `OnTokenValidated` rejects any token carrying a stale value — so a permission
  change takes effect on the **next request**, not in fifteen minutes.
- **Login is not an account oracle.** Unknown email and wrong password return an identical
  401, so the endpoint cannot be used to enumerate who has an account.
- `ClockSkew` is zero. The default five minutes would keep a revoked token alive well past
  its stated expiry.

---

## Authorization: roles and rights

Endpoints check **permissions, never role names**:

```csharp
[HasPermission(Permissions.Leads.Delete)]
public async Task<IActionResult> Delete(Guid id) { … }
```

Roles are just bundles of permissions, so a deployment can re-shape roles without touching a
single attribute. `PermissionPolicyProvider` manufactures a policy per permission on demand —
adding a right means adding one constant, not editing a registration list too.

### Resolution order

```
role grants  →  + per-user grants  →  − per-user denies
```

An explicit per-user **deny always wins**. That asymmetry is deliberate: revoking a sensitive
right from one person must not depend on auditing every role they happen to hold.

The JWT carries the user's rights, so the usual check needs no database round-trip; the handler
falls back to a live lookup only when a token carries no permission claims at all.

### Seeded roles

| Role | Rights |
| --- | --- |
| **Administrator** | Everything — including permissions added in future releases. |
| **Manager** | Full lead + search lifecycle, view users/settings/logs. No user or role administration. |
| **Analyst** | Create/update/verify/export leads, run searches. No deletion. |
| **Viewer** | `leads.view`, `searches.view`. |

Administrator is always granted the full catalogue, so the role can never fall behind and lock
admins out of new features. Seeding only ever **adds** grants to the other roles — if you
deliberately narrow `Manager`, a restart will not undo it.

26 permissions across Leads, Searches, Users, Roles, Settings and System. `GET /api/permissions`
returns them grouped.

### Lockout guards

Enforced server-side, not just hidden in a UI:

- The **last active administrator** cannot be deleted, deactivated, or stripped of the role.
- You cannot **delete or deactivate your own account**.
- **System roles** cannot be renamed or deleted.
- A role **still assigned to users** cannot be deleted (409).

---

## API surface

| Area | Endpoints |
| --- | --- |
| Auth | `register`, `login`, `refresh`, `logout`, `me`, `change-password` |
| Users | CRUD, `PUT {id}/roles`, `PUT {id}/permissions`, `DELETE {id}/permissions/{name}`, `POST {id}/reset-password` |
| Roles | CRUD, `PUT {id}/permissions` |
| Permissions | `GET /api/permissions`, `GET /api/permissions/mine` |
| Leads | query/filter/page, stats, CRUD, `POST bulk-delete` |
| Searches | `POST /api/searches`, `GET active`, `GET` (history), `GET {id}`, `POST {id}/stop`, `DELETE {id}`, `DELETE` (clear) |
| Health | `GET /api/health` (anonymous) |

Failures return RFC 7807 problem details. Exception detail is included **only outside
production** — a stack trace reaching a client is an information-disclosure bug.

---

## Data model notes

- **Soft delete.** Auditable entities set `IsDeleted` instead of being removed, and a global
  query filter hides them. History survives without every call site remembering.
- **Automatic audit stamping.** `SaveChanges` fills created/updated/deleted metadata from the
  current user.
- **Append-only audit trail** (`AuditEntries`) records logins, permission changes and deletions.
  Auditing failures are logged but never break the operation being audited.
- **Persisted dedupe keys.** Website host, phone and name+city are normalised into indexed
  columns, so duplicate detection is an index seek rather than a table scan per insert.
- Enums persist as `int`; the API serialises them as **names**, so clients aren't coupled to
  numeric values that would shift if an enum were reordered.

> **Note on `sqlcmd`:** the `Businesses` table uses filtered indexes, which require
> `QUOTED_IDENTIFIER ON` for DML. `sqlcmd` defaults it off — pass `-I`. EF Core and SqlClient
> set it correctly on their own, so this only affects ad-hoc command-line queries.

---

## Verified behaviour

Exercised against SQL Server 2022 Express over real HTTP:

- Anonymous request → 401; admin login → 26 permissions on the token.
- Viewer with `leads.view` only: `GET /api/businesses` 200, `POST` 403, `/api/users` 403, `/api/roles` 403.
- Per-user **grant** of `leads.create` → POST succeeds; per-user **deny** of `leads.view` → 403
  despite the role granting it.
- Permission change bumps `SecurityVersion` → the previously issued token is rejected 401.
- Refresh rotates; replaying the old token revokes the family (both tokens 401).
- Duplicate lead → 409. Lead with no email → 201. Malformed email → 400.
- Last-admin, self-delete and system-role guards all → 400.

Scraper, against live OpenStreetMap and real business websites:

- A two-category sweep of Bath returned 8 businesses, crawled their sites, and saved 8 leads
  with 4 MX-verified addresses (`appointments@ba1hair.co.uk`, `reception@artizanbath.co.uk`, …).
- **Crash recovery:** the API was hard-killed (`Stop-Process -Force`) 2 tasks into a 4-task run
  holding 18 saved leads. On restart the reaper released the lease, the worker re-claimed the
  job (`attempts=2`) and resumed at task 3 — final `4/4, 27 found, 25 saved, 2 duplicates`, with
  the first two tasks never re-scraped.
- Re-running an identical search saved 0 and reported the overlap as duplicates.
- A run whose every task failed reports `Failed` with the provider error, not `Completed, 0
  found` — the latter reads as "there are none there", which is a costlier wrong conclusion.
