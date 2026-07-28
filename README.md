# LeadMine AI

A lead generation suite built with Next.js 16 (App Router), TypeScript and Tailwind CSS v4.

Discover businesses by category and location, enrich them with **publicly listed** contact
details, and export clean lead lists to Excel or CSV. Excel is the primary datastore:
every saved lead lands in `database/Businesses.xlsx`.

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

### Container / VPS — recommended

The full feature set needs a long-lived Node process with a writable disk: SSE progress,
in-process job control, and Excel writes all depend on it.

```bash
npm run build
LEADMINE_DATA_DIR=/data npm start
```

Mount a persistent volume at `/data`. Any container platform, VPS, Fly.io, Render or
Railway works.

### Netlify — with real caveats

`netlify.toml` is included and the UI deploys fine, but be aware of what serverless costs
you here:

1. **The filesystem is ephemeral.** The data directory falls back to the OS temp dir, so
   the Excel database does not survive between invocations.
2. **SSE and job control break.** Jobs live in the memory of one process; a later request
   may hit a different instance, which won't know the job.
3. **Function timeouts** (10–26s) will kill any non-trivial search mid-run.

Netlify is a reasonable host for the dashboard if you move search execution to a
persistent worker. For the app as built, use a container or VPS.

---

## Configuration

Settings live in the UI (**Settings**) and persist to `database/settings.json`.

| Setting                | Default | Notes                                            |
| ---------------------- | ------- | ------------------------------------------------ |
| Concurrency            | 4       | Parallel website fetches during enrichment       |
| Delay between requests | 400 ms  | A site's own `Crawl-delay` wins if longer        |
| Provider rate limit    | 120/min | Keeps you inside API quota                       |
| Retry attempts         | 3       | Exponential backoff with jitter                  |
| Request timeout        | 15 s    | Per HTTP request                                 |
| Max pages per site     | 4       | Homepage + contact/about pages                   |
| Respect robots.txt     | on      | Leave on unless you own the sites being crawled  |

`GOOGLE_PLACES_API_KEY` in the environment overrides the stored key and makes the UI field
read-only — the right setup for production.

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
