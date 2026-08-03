# Scraper worker.
#
# Deliberately a long-running container rather than a serverless function:
# Netlify Background Functions are killed at 15 minutes, which is what made
# large sweeps die part-way through. Here the process simply keeps running, and
# if it does die the orchestrator restarts it — the lease in SQL means the job it
# was holding returns to the queue and resumes from its last checkpoint.

FROM node:20-alpine AS deps

WORKDIR /app

COPY package.json package-lock.json ./
RUN npm ci --omit=dev --ignore-scripts

# tsx runs the TypeScript directly, so the worker shares the app's scraping,
# enrichment and verification code with no duplicate build pipeline.
RUN npm install --no-save tsx@4

FROM node:20-alpine AS runtime

WORKDIR /app

ENV NODE_ENV=production

COPY --from=deps /app/node_modules ./node_modules
COPY package.json tsconfig.json ./
COPY worker ./worker
COPY lib ./lib
COPY services ./services
COPY repositories ./repositories
COPY types ./types
COPY utils ./utils

# Don't run as root.
USER node

# Node handles SIGTERM itself; the worker traps it to finish the current task
# and checkpoint before exiting, so a rolling restart loses no progress.
STOPSIGNAL SIGTERM

CMD ["npx", "tsx", "worker/index.ts"]
