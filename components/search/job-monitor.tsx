'use client';

import * as React from 'react';
import {
  AtSign,
  CircleSlash,
  Copy,
  ExternalLink,
  Globe,
  Pause,
  Phone,
  Play,
  Square,
  Wifi,
  WifiOff,
} from 'lucide-react';
import { toast } from 'sonner';

import { BusinessStatusBadge, JobStatusBadge } from '@/components/shared/status-badge';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
import { Alert, AlertDescription, EmptyState, Separator } from '@/components/ui/misc';
import { Progress } from '@/components/ui/progress';
import { cn } from '@/lib/utils';
import type { JobSnapshot } from '@/types/job';
import type { LiveResult } from '@/types/search';
import { formatDuration, formatNumber } from '@/utils/format';

interface JobMonitorProps {
  job: JobSnapshot;
  connected: boolean;
  streamError: string | null;
  onCommand: (command: 'pause' | 'resume' | 'stop') => void;
  commandPending: boolean;
}

export function JobMonitor({
  job,
  connected,
  streamError,
  onCommand,
  commandPending,
}: JobMonitorProps) {
  const { counters, progress, status } = job;
  const isActive = status === 'running' || status === 'paused' || status === 'stopping';

  const metrics = [
    { label: 'Found', value: counters.found },
    { label: 'Saved', value: counters.saved },
    { label: 'Duplicates', value: counters.duplicates },
    { label: 'Enriched', value: counters.enriched },
    { label: 'Skipped', value: counters.skipped },
    { label: 'Failed', value: counters.failed + counters.enrichmentFailed },
  ];

  return (
    <div className="space-y-6">
      <Card>
        <CardHeader className="flex-row items-start justify-between gap-3">
          <div className="min-w-0 space-y-1.5">
            <CardTitle className="flex items-center gap-2">
              Live progress
              <JobStatusBadge status={status} />
            </CardTitle>
            <CardDescription className="truncate">{progress.currentTask}</CardDescription>
          </div>

          <span
            className={cn(
              'flex shrink-0 items-center gap-1.5 rounded-full px-2 py-1 text-[11px] font-medium',
              connected ? 'bg-success/12 text-success' : 'bg-muted text-muted-foreground',
            )}
            title={connected ? 'Streaming live updates' : 'Not connected to the live stream'}
          >
            {connected ? <Wifi className="size-3" /> : <WifiOff className="size-3" />}
            {connected ? 'Live' : 'Offline'}
          </span>
        </CardHeader>

        <CardContent className="space-y-5">
          <div className="space-y-2">
            <div className="flex items-center justify-between text-sm">
              <span className="font-medium tabular-nums">{progress.percent.toFixed(1)}%</span>
              <span className="tabular-nums text-muted-foreground">
                {counters.completedTasks} / {counters.totalTasks} searches
              </span>
            </div>
            <Progress
              value={progress.percent}
              indicatorClassName={cn(
                status === 'paused' && 'bg-warning',
                status === 'failed' && 'bg-destructive',
                status === 'completed' && 'bg-success',
              )}
            />
            <div className="flex flex-wrap items-center justify-between gap-2 text-xs text-muted-foreground">
              <span>Elapsed {formatDuration(progress.elapsedMs)}</span>
              <span>
                {progress.etaMs !== null
                  ? `About ${formatDuration(progress.etaMs)} remaining`
                  : isActive
                    ? 'Estimating…'
                    : 'Finished'}
              </span>
            </div>
          </div>

          <Separator />

          <dl className="grid grid-cols-2 gap-4 sm:grid-cols-3 lg:grid-cols-6">
            {metrics.map((metric) => (
              <div key={metric.label}>
                <dt className="text-[11px] uppercase tracking-wide text-muted-foreground">
                  {metric.label}
                </dt>
                <dd className="text-lg font-semibold tabular-nums">{formatNumber(metric.value)}</dd>
              </div>
            ))}
          </dl>

          {job.error && (
            <Alert variant="destructive">
              <CircleSlash />
              <AlertDescription>{job.error}</AlertDescription>
            </Alert>
          )}

          {streamError && !job.error && (
            <Alert variant="warning">
              <WifiOff />
              <AlertDescription>{streamError}</AlertDescription>
            </Alert>
          )}

          <div className="flex flex-wrap gap-2">
            {status === 'running' && (
              <Button
                variant="outline"
                onClick={() => onCommand('pause')}
                disabled={commandPending}
              >
                <Pause />
                Pause
              </Button>
            )}
            {status === 'paused' && (
              <Button onClick={() => onCommand('resume')} disabled={commandPending}>
                <Play />
                Resume
              </Button>
            )}
            {isActive && (
              <Button
                variant="destructive"
                onClick={() => onCommand('stop')}
                disabled={commandPending || status === 'stopping'}
              >
                <Square />
                {status === 'stopping' ? 'Stopping…' : 'Stop'}
              </Button>
            )}
          </div>
        </CardContent>
      </Card>

      <LiveResults results={job.results} />
    </div>
  );
}

function LiveResults({ results }: { results: LiveResult[] }) {
  const copy = async (value: string, label: string): Promise<void> => {
    try {
      await navigator.clipboard.writeText(value);
      toast.success(`${label} copied`);
    } catch {
      toast.error('Could not copy to clipboard');
    }
  };

  return (
    <Card>
      <CardHeader>
        <CardTitle>Live results</CardTitle>
        <CardDescription>
          Newest first. Saved rows are written to Businesses.xlsx as the run progresses.
        </CardDescription>
      </CardHeader>
      <CardContent>
        {results.length === 0 ? (
          <EmptyState
            icon={<Globe />}
            title="Nothing yet"
            description="Results appear here the moment they are discovered."
          />
        ) : (
          <ul className="scrollbar-thin max-h-[36rem] space-y-2 overflow-y-auto pr-1">
            {results.map((result, index) => (
              <li
                key={`${result.id}-${index}`}
                className={cn(
                  'rounded-lg border border-border p-3 transition-colors',
                  result.duplicate && 'opacity-60',
                )}
              >
                <div className="flex flex-wrap items-start justify-between gap-2">
                  <div className="min-w-0 flex-1">
                    <p className="truncate font-medium">{result.name}</p>
                    <p className="truncate text-xs text-muted-foreground">
                      {[result.category, result.city, result.country].filter(Boolean).join(' · ')}
                    </p>
                  </div>
                  <div className="flex shrink-0 items-center gap-1.5">
                    {result.duplicate ? (
                      <Badge variant="muted">Duplicate</Badge>
                    ) : (
                      <BusinessStatusBadge status={result.status} />
                    )}
                  </div>
                </div>

                {(result.email || result.phone || result.website) && (
                  <div className="mt-2 flex flex-wrap items-center gap-1.5">
                    {result.email && (
                      <button
                        type="button"
                        onClick={() => void copy(result.email, 'Email')}
                        className="inline-flex max-w-full items-center gap-1 rounded-md bg-success/12 px-2 py-1 text-xs text-success transition-colors hover:bg-success/20"
                        title="Copy email"
                      >
                        <AtSign className="size-3 shrink-0" />
                        <span className="truncate">{result.email}</span>
                        <Copy className="size-3 shrink-0 opacity-60" />
                      </button>
                    )}
                    {result.phone && (
                      <button
                        type="button"
                        onClick={() => void copy(result.phone, 'Phone')}
                        className="inline-flex items-center gap-1 rounded-md bg-muted px-2 py-1 text-xs text-muted-foreground transition-colors hover:bg-accent"
                        title="Copy phone"
                      >
                        <Phone className="size-3 shrink-0" />
                        {result.phone}
                        <Copy className="size-3 shrink-0 opacity-60" />
                      </button>
                    )}
                    {result.website && (
                      <a
                        href={result.website}
                        target="_blank"
                        rel="noopener noreferrer nofollow"
                        className="inline-flex max-w-[220px] items-center gap-1 rounded-md bg-muted px-2 py-1 text-xs text-muted-foreground transition-colors hover:bg-accent"
                      >
                        <Globe className="size-3 shrink-0" />
                        <span className="truncate">{new URL(result.website).hostname}</span>
                        <ExternalLink className="size-3 shrink-0 opacity-60" />
                      </a>
                    )}
                  </div>
                )}
              </li>
            ))}
          </ul>
        )}
      </CardContent>
    </Card>
  );
}
