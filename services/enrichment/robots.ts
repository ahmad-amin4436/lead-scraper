import 'server-only';

import { createHttpClient } from '../http/http-client';

interface RobotsRule {
  /** True for Allow, false for Disallow. */
  allow: boolean;
  path: string;
}

interface RobotsPolicy {
  rules: RobotsRule[];
  crawlDelayMs: number | null;
  /** True when robots.txt was missing or unreadable — crawling is permitted. */
  permissive: boolean;
}

const CACHE_TTL_MS = 15 * 60 * 1000;

const globalForRobots = globalThis as unknown as {
  leadmineRobotsCache?: Map<string, { policy: RobotsPolicy; expiresAt: number }>;
};

const cache =
  globalForRobots.leadmineRobotsCache ??
  new Map<string, { policy: RobotsPolicy; expiresAt: number }>();
globalForRobots.leadmineRobotsCache = cache;

const PERMISSIVE: RobotsPolicy = { rules: [], crawlDelayMs: null, permissive: true };

/**
 * Minimal robots.txt parser covering the directives that matter here:
 * User-agent grouping, Allow, Disallow and Crawl-delay.
 *
 * Group selection follows the standard: the most specific matching user-agent
 * group wins, falling back to `*`.
 */
function parseRobots(text: string, userAgentToken: string): RobotsPolicy {
  const lines = text.split(/\r?\n/);

  const groups: { agents: string[]; rules: RobotsRule[]; crawlDelay: number | null }[] = [];
  let current: (typeof groups)[number] | null = null;
  let lastLineWasAgent = false;

  for (const rawLine of lines) {
    const line = rawLine.split('#')[0].trim();
    if (!line) continue;

    const separator = line.indexOf(':');
    if (separator === -1) continue;

    const field = line.slice(0, separator).trim().toLowerCase();
    const value = line.slice(separator + 1).trim();

    if (field === 'user-agent') {
      // Consecutive User-agent lines share one rule block.
      if (!current || !lastLineWasAgent) {
        current = { agents: [], rules: [], crawlDelay: null };
        groups.push(current);
      }
      current.agents.push(value.toLowerCase());
      lastLineWasAgent = true;
      continue;
    }

    lastLineWasAgent = false;
    if (!current) continue;

    if (field === 'disallow') current.rules.push({ allow: false, path: value });
    else if (field === 'allow') current.rules.push({ allow: true, path: value });
    else if (field === 'crawl-delay') {
      const seconds = Number.parseFloat(value);
      if (Number.isFinite(seconds)) current.crawlDelay = seconds;
    }
  }

  const token = userAgentToken.toLowerCase();
  const specific = groups.find((g) => g.agents.some((a) => a !== '*' && token.includes(a)));
  const wildcard = groups.find((g) => g.agents.includes('*'));
  const chosen = specific ?? wildcard;

  if (!chosen) return PERMISSIVE;

  return {
    rules: chosen.rules,
    crawlDelayMs: chosen.crawlDelay === null ? null : Math.round(chosen.crawlDelay * 1000),
    permissive: false,
  };
}

/** Converts a robots path pattern (`*` and `$` wildcards) to a RegExp. */
function patternToRegExp(pattern: string): RegExp {
  const escaped = pattern
    .replace(/[.+?^${}()|[\]\\]/g, '\\$&')
    .replace(/\*/g, '.*');

  const anchored = escaped.endsWith('\\$') ? `${escaped.slice(0, -2)}$` : escaped;
  return new RegExp(`^${anchored}`);
}

function isPathAllowed(policy: RobotsPolicy, pathname: string): boolean {
  if (policy.permissive || policy.rules.length === 0) return true;

  let bestMatch: { rule: RobotsRule; length: number } | null = null;

  for (const rule of policy.rules) {
    // An empty Disallow means "allow everything" and matches nothing.
    if (rule.path === '') continue;

    if (patternToRegExp(rule.path).test(pathname)) {
      const length = rule.path.length;
      // Longest matching pattern wins; Allow beats Disallow on a tie.
      if (!bestMatch || length > bestMatch.length || (length === bestMatch.length && rule.allow)) {
        bestMatch = { rule, length };
      }
    }
  }

  return bestMatch ? bestMatch.rule.allow : true;
}

export interface RobotsChecker {
  isAllowed(url: string): boolean;
  crawlDelayMs: number | null;
}

/**
 * Fetches and caches the robots.txt policy for an origin.
 *
 * A missing, erroring, or unparseable robots.txt is treated as permissive,
 * which matches how mainstream crawlers behave. Any network failure fails open
 * for availability but never bypasses an explicit Disallow.
 */
export async function getRobotsChecker(
  origin: string,
  userAgent: string,
  timeoutMs: number,
  signal: AbortSignal,
): Promise<RobotsChecker> {
  const cached = cache.get(origin);
  const now = Date.now();

  let policy: RobotsPolicy;

  if (cached && cached.expiresAt > now) {
    policy = cached.policy;
  } else {
    policy = PERMISSIVE;

    try {
      const client = createHttpClient({
        timeoutMs: Math.min(timeoutMs, 10_000),
        userAgent,
        maxContentLength: 512 * 1024,
      });
      const response = await client.get<string>(`${origin}/robots.txt`, {
        signal,
        responseType: 'text',
        transformResponse: [(data: unknown) => data],
      });

      if (response.status === 200 && typeof response.data === 'string') {
        policy = parseRobots(response.data, userAgent);
      }
    } catch {
      // Unreachable robots.txt -> permissive, consistent with common practice.
    }

    cache.set(origin, { policy, expiresAt: now + CACHE_TTL_MS });
  }

  return {
    crawlDelayMs: policy.crawlDelayMs,
    isAllowed(url: string): boolean {
      try {
        const parsed = new URL(url);
        return isPathAllowed(policy, `${parsed.pathname}${parsed.search}`);
      } catch {
        return false;
      }
    },
  };
}
