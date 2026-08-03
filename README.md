# LeadMine AI

A lead generation suite built with Next.js 16 (App Router), TypeScript and Tailwind CSS v4.

Discover businesses by category and location, enrich them with **publicly listed** contact
details, and export clean lead lists to Excel or CSV. Leads persist as JSON — to Netlify
Blobs in production, or the local `database/` folder in development — and Excel/CSV files
are generated on demand when you export.

---

## Quick start

```bash
npm install
npm run dev          # http://localhost:3000
```

That's it — **no API key is required**. LeadMine falls back to OpenStreetMap (Overpass +
Nominatim), which needs no credentials. Add a Google Places key later for ratings and
review counts.

```bash
cp .env.example .env.local   # optional
```

| Script              | What it does                                |
| ------------------- | ------------------------------------------- |
| `npm run dev`       | Dev server (Turbopack)                      |
| `npm run build`     | Production build                            |
| `npm start`         | Serve the production build                  |
| `npm run typecheck` | `tsc --noEmit`                              |
| `npm run lint`      | ESLint                                      |
| `npm run check`     | typecheck + lint + build                    |

Requires **Node.js 20.9+** (a Next.js 16 requirement).

---

## How it works

```
Search request
   ↓  geocode each city (Google Geocoding, else Nominatim — cached per process)
   ↓  for every (city × category) pair
   ↓      provider search  ── Google Places (New) v1  or  OpenStreetMap Overpass
   ↓      quality filters  ── minimum rating / review count
   ↓      duplicate check  ── website host, phone, or name-within-city
   ↓      contact enrichment (optional, bounded concurrency)
   ↓  batch write → database/Businesses.xlsx
   ↓  live progress streamed to the browser over SSE
```

### Data sources

| Provider          | Key required | Ratings & reviews | Notes                                          |
| ----------------- | ------------ | ----------------- | ---------------------------------------------- |
| **OpenStreetMap** | No           | No                | Default. Community Overpass servers throttle heavy use. |
| **Google Places** | Yes          | Yes               | Needs *Places API (New)* + *Geocoding API*.    |

OpenStreetMap carries no rating data, so the rating/review filters will exclude every
result when it is the active provider. The search form warns about this inline.

### Contact enrichment

When a business has a website, the crawler fetches the homepage plus a few
contact/about pages and extracts what the site publishes:

- emails (from `mailto:` links, page text, and JSON-LD `Organization` blocks), ranked so
  `sales@` / `info@` / `contact@` on the business's own domain win over a generic inbox
- phone numbers from `tel:` links
- Facebook, Instagram, LinkedIn, X/Twitter, YouTube and WhatsApp links
- a public contact-form URL

**Boundaries the crawler respects.** It reads only public marketing pages and stops there.
It honours `robots.txt` (including `Crawl-delay`), sends an identifying User-Agent with your
contact email, treats `401`/`403` as "not public" and moves on, paces requests, and caps
pages per site. It never authenticates, submits forms, solves challenges, or works around
any access control. Only publicly listed information is stored.

An SSRF guard resolves every target hostname and refuses private, loopback, link-local and
reserved ranges — website URLs come from third-party providers and are treated as untrusted.

### Contact verification

Every lead carries an **Email Status** and a **WhatsApp Status**, both filterable on the
Database and Export pages. Read them for what they actually are:

| Email status    | Meaning                                                              |
| --------------- | -------------------------------------------------------------------- |
| **Deliverable** | The domain publishes a mail server (MX record), so mail can route.    |
| **Risky**       | Disposable provider, or no MX so delivery is uncertain.               |
| **Dead**        | Malformed, or the domain does not exist / accepts no mail.            |
| **Unknown**     | The DNS lookup was inconclusive — worth re-checking.                  |
| **Unchecked**   | Not verified yet.                                                     |

> **"Deliverable" is not "this mailbox exists."** Proving that needs an SMTP `RCPT TO`
> probe, which is deliberately not implemented: major providers refuse or lie about it,
> catch-all domains accept everything, and probing at volume gets your IP blacklisted —
> damaging the deliverability of the very campaigns this data feeds. Treat it as "safe to
> attempt". An inconclusive lookup is never reported as dead.

| WhatsApp status | Meaning                                                              |
| --------------- | -------------------------------------------------------------------- |
| **On WhatsApp** | The business published a WhatsApp link on its own website.            |
| **Likely**      | Valid mobile line, so WhatsApp is probable — inferred, not checked.   |
| **Unlikely**    | Landline, or the number could not be validated.                       |
| **No number**   | No phone on the record.                                               |

> **WhatsApp registration cannot be verified for a number you don't own.** The Business API
> only answers for your own numbers, and the unofficial endpoints that claim otherwise
> violate WhatsApp's terms and get numbers banned. Only *On WhatsApp* is evidence; the rest
> is line-type inference from [libphonenumber](https://github.com/google/libphonenumber),
> which is accurate per country. A `wa.me` link is generated for mobile numbers, but
> clicking it is still the only way to confirm.

New leads are verified during the run. To backfill existing ones, use **Verify contacts**
on the Database page (or `POST /api/businesses/verify`), which works in bounded batches so
it can't outrun a serverless timeout.

---

## Excel database

`database/Businesses.xlsx`, sheet `Businesses`, created automatically on first boot with a
frozen, filterable header row and these 23 columns:

`ID · Business Name · Category · Country · State · City · Address · Phone · Website ·
Email · WhatsApp · Facebook · Instagram · LinkedIn · Latitude · Longitude · Google Rating ·
Review Count · Maps URL · Source · Date Added · Status · Notes`

Rows are re-read by **header name**, so you can reorder columns in Excel without breaking
imports. Writes go to a temp file and are then renamed, so a crash never leaves a
half-written workbook.

Lead `Status` is one of `new`, `enriched`, `partial`, `no-website`, `enrichment-failed`.

### Other files in `database/`

| File                  | Purpose                        | Tracked in git?               |
| --------------------- | ------------------------------ | ----------------------------- |
| `Businesses.xlsx`     | Primary lead datastore         | Yes                           |
| `settings.json`       | API key, limits, crawler config| **No — holds a secret**       |
| `logs.jsonl`          | Append-only activity log       | No                            |
| `search-history.json` | Past runs                      | No                            |
| `exports/`            | Generated export files         | No (folder kept)              |

---

## Architecture

Clean, layered, one direction of dependency: **routes → services → repositories**.

```
app/
  (dashboard)/        dashboard · search · database · history · export · logs · settings
  api/                route handlers (search, SSE stream, businesses, exports, history, logs, settings, health)
components/
  ui/                 shadcn-style primitives on Radix
  layout/ shared/ …   sidebar, topbar, per-feature views
hooks/                React Query hooks, SSE job stream, debounce
lib/                  config, constants, Zod schemas, API helpers, errors, paths
services/             search providers, geocoding, enrichment, jobs, export, logging
repositories/         Excel workbook + JSON stores (all disk access)
types/                domain models
utils/                async, format, normalize, geo, csv, id
database/             the datastore (created at runtime)
```

Design notes worth knowing:

- **Excel has no partial write**, so the sheet is cached in memory and every persist
  rewrites the file. Mutations mark the store dirty; `flush()` coalesces concurrent
  writers, and a mutex serialises all disk access.
- **Jobs are in-process.** A `Job` owns a state machine (`queued → running ⇄ paused →
  completed/stopped/failed`). Pause is a gate the runner awaits at checkpoints; stop
  aborts a shared `AbortSignal` that unwinds in-flight HTTP. Only one run at a time —
  concurrent runs would contend on the workbook and blow through provider quotas.
- **Retries** use exponential backoff with full jitter, and only for timeouts, 429s and
  5xx. Client errors fail fast.
- **Secrets never reach the browser.** `GET /api/settings` returns a masked key
  (`AIza••••fXYZ`) and a `configured` flag, never the raw value.

### API

| Method | Route                          | Purpose                          |
| ------ | ------------------------------ | -------------------------------- |
| `POST` | `/api/search`                  | Start a run (returns `202`)      |
| `GET`  | `/api/search`                  | List jobs                        |
| `GET`  | `/api/search/[jobId]`          | Job snapshot                     |
| `POST` | `/api/search/[jobId]`          | `pause` / `resume` / `stop`      |
| `GET`  | `/api/search/[jobId]/stream`   | SSE live progress                |
| `GET`  | `/api/businesses`              | Query leads (filter/sort/page)   |
| `GET`  | `/api/businesses/stats`        | Dashboard statistics             |
| `POST` | `/api/exports`                 | Generate an export               |
| `GET`  | `/api/exports/[id]`            | Download an export               |
| `GET`  | `/api/logs`, `/api/history`    | Activity trail, past runs        |
| `GET`  | `/api/health`                  | Status, data dir, provider readiness |

All responses use the envelope `{ ok: true, data }` or
`{ ok: false, error: { code, message, fields? } }`. Every input is validated server-side
with Zod — the client schema is the same module, so validation cannot drift.

---

## Export

Excel (`.xlsx`) or CSV, with three column templates:

- **LeadMine** — all 23 fields
- **HubSpot** — column names matching HubSpot company-import properties
- **Salesforce** — standard Lead fields

CSV output carries a UTF-8 BOM (so Excel detects the encoding) and escapes cells beginning
with `= + - @` to prevent formula injection when the file is opened in a spreadsheet.

---

## Deployment

Two pieces, deployed separately:

| Piece | Runs on | Holds |
| --- | --- | --- |
| **Next.js front end** | Netlify (or any Node host) | Nothing. Renders the UI and proxies to the API. |
| **.NET 8 API** | A .NET application host + SQL Server | Leads, users, search runs — and the scraper. |

### The scraper runs inside the .NET API

There is no worker process, container or VPS to operate. Searching is a hosted
`BackgroundService` in the API: `POST /api/searches` writes a queued row, the worker claims
it under a lease, and progress is checkpointed per (city × category) task.

That design is what makes a restart survivable. If the app pool recycles mid-sweep, the lease
lapses, a reaper returns the job to the queue, and the next instance **resumes from the last
checkpoint** instead of re-scraping. See [backend/README.md](backend/README.md#the-scraper)
for the settings and the app-host checklist — the short version is **turn Always On on**, or a
queued run waits until the next request wakes the process.

### Front end on Netlify

Connect the repo; `netlify.toml` and `@netlify/plugin-nextjs` handle the build. Set
`BACKEND_API_URL` to the deployed API. Route handlers attach the caller's bearer token from an
httpOnly cookie and forward — no search work happens in a Netlify function, so the 15-minute
background-function ceiling no longer applies to anything.

---

## Configuration

Scraper behaviour is configured on the API, under the `Scraper` section — see
[backend/README.md](backend/README.md#settings) for the full table.

| Setting | Default | Notes |
| --- | --- | --- |
| `EnrichmentConcurrency` | 4 | Parallel website fetches during enrichment |
| `DelayMs` | 400 ms | A site's own `Crawl-delay` wins if longer |
| `RateLimitPerMinute` | 120 | Keeps you inside provider quota |
| `RetryAttempts` | 3 | Exponential backoff with jitter |
| `RequestTimeoutMs` | 15 s | Per HTTP request |
| `MaxPagesPerSite` | 4 | Homepage + contact/about pages |
| `RespectRobotsTxt` | on | Leave on unless you own the sites being crawled |
| `CrawlerContactEmail` | — | Advertised in the User-Agent so site owners can reach you |

`Scraper:GoogleApiKey` is a secret: supply it via user-secrets or the
`Scraper__GoogleApiKey` environment variable. Leave it empty to run on OpenStreetMap, which
needs no key.

---

## Notes on this build

- **`npm audit` reports advisories** in transitive dependencies of `next`, `eslint` and
  `exceljs` (`brace-expansion`, `postcss`, `sharp`, `archiver`). None have a
  semver-compatible fix; `npm audit fix --force` would downgrade Next.js, which is worse.
  They are build/dev-time paths, not request-handling code.
- **React Table and SheetJS were dropped.** Filtering, sorting and pagination are done
  server-side against the in-memory sheet, so the tables are plain markup with no client
  table engine needed; ExcelJS alone covers both reading and writing `.xlsx`, so a second
  spreadsheet library would have been duplicated logic.
- **Puppeteer was not used.** Enrichment is HTTP + Cheerio, which is faster, far lighter,
  and sufficient for reading published contact details.

## Use it responsibly

This tool collects business contact information that organisations have chosen to publish.
Before you send anything, check that your intended use complies with GDPR, CAN-SPAM, PECR
or whatever applies where you operate — lawful basis, an unsubscribe path, and honouring
opt-outs are your responsibility, not the tool's. Set a real crawler contact email in
Settings so site owners can reach you.
