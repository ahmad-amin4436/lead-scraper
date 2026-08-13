'use client';

import * as React from 'react';
import { AlertTriangle, CheckCircle2, Contact2, Plus, Search, Sparkles, XCircle } from 'lucide-react';
import { toast } from 'sonner';

import { PageHeader } from '@/components/shared/page-header';
import { LinkedInConnectDialog } from '@/components/linkedin/linkedin-connect-dialog';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
import { Checkbox } from '@/components/ui/checkbox';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Alert, AlertDescription, EmptyState, Separator, Skeleton } from '@/components/ui/misc';
import { Progress } from '@/components/ui/progress';
import { useDebouncedValue } from '@/hooks/use-debounced-value';
import { useBackendBusinesses } from '@/hooks/use-leads';
import {
  useActiveLinkedInJobs,
  useLinkedInJobCommand,
  useStartLinkedInEnrichment,
} from '@/hooks/use-linkedin-enrichment';
import { useLinkedInJobPoll } from '@/hooks/use-linkedin-job-poll';
import { formatNumber } from '@/utils/format';
import { useQueryClient } from '@tanstack/react-query';

const PAGE_SIZE = 50;
const MAX_SELECTABLE = 25;

function isActiveStatus(status: string): boolean {
  return status === 'running' || status === 'queued' || status === 'stopping';
}

/**
 * Enriches already-saved leads with LinkedIn data on demand: company detail
 * (industry, size, description) plus a decision-maker search.
 *
 * Runs as a queued, polled batch — not a single blocking request — because
 * each lead is one or two real LinkedIn browser sessions, which a batch of up
 * to {@link MAX_SELECTABLE} cannot reliably finish inside a normal HTTP
 * request's lifetime (this page used to do exactly that, and 504'd in
 * production). Same shape `SearchView` already uses for search runs: start,
 * then poll a job snapshot until it reaches a terminal status.
 *
 * Unlike Send Email/Send WhatsApp, this doesn't require the lead to already
 * carry a LinkedIn URL: the company-enrichment step searches by name first.
 */
export function LinkedInEnrichmentView() {
  const queryClient = useQueryClient();

  const [search, setSearch] = React.useState('');
  const [hideEnriched, setHideEnriched] = React.useState(true);
  const [maxDecisionMakers, setMaxDecisionMakers] = React.useState(3);
  const [selected, setSelected] = React.useState<Set<string>>(new Set());

  const [startedJobId, setStartedJobId] = React.useState<string | null>(null);
  const [dismissed, setDismissed] = React.useState(false);

  const startBatch = useStartLinkedInEnrichment();
  const jobCommand = useLinkedInJobCommand();

  // Reconnect to a batch already in progress (e.g. after a page reload), the
  // same way SearchView adopts an active search job.
  const activeJobs = useActiveLinkedInJobs();
  const adoptedJobId = React.useMemo(() => {
    if (startedJobId || dismissed) return null;
    return activeJobs.data?.find((j) => isActiveStatus(j.status))?.id ?? null;
  }, [startedJobId, dismissed, activeJobs.data]);

  const jobId = startedJobId ?? adoptedJobId;
  const { job, error: pollError, reset } = useLinkedInJobPoll(jobId);

  const isActive = job !== null && isActiveStatus(job.status);

  const debouncedSearch = useDebouncedValue(search, 350);

  const leads = useBackendBusinesses({
    search: debouncedSearch || undefined,
    // On by default: a lead already enriched shouldn't be re-selected by
    // accident and burn another slow LinkedIn browser run on a repeat.
    hasBeenLinkedInEnriched: hideEnriched ? false : undefined,
    page: 1,
    pageSize: PAGE_SIZE,
    sortBy: 'createdAt',
    sortDir: 'desc',
  });

  const rows = leads.data?.items ?? [];

  const toggle = (id: string): void => {
    setSelected((current) => {
      const next = new Set(current);
      if (next.has(id)) {
        next.delete(id);
      } else {
        if (next.size >= MAX_SELECTABLE) {
          toast.warning(`You can enrich up to ${MAX_SELECTABLE} leads at once.`);
          return current;
        }
        next.add(id);
      }
      return next;
    });
  };

  const selectableRows = rows.slice(0, MAX_SELECTABLE);
  const allSelected = selectableRows.length > 0 && selectableRows.every((row) => selected.has(row.id));

  const toggleAll = (): void => {
    setSelected((current) => {
      const next = new Set(current);
      if (allSelected) {
        selectableRows.forEach((row) => next.delete(row.id));
      } else {
        selectableRows.forEach((row) => next.add(row.id));
      }
      return next;
    });
  };

  // Refresh the database views once a batch finishes so counts stay accurate.
  const previousStatus = React.useRef<string | null>(null);
  React.useEffect(() => {
    if (!job) return;
    if (previousStatus.current === job.status) return;
    previousStatus.current = job.status;

    if (job.status === 'completed' || job.status === 'stopped' || job.status === 'failed') {
      void queryClient.invalidateQueries({ queryKey: ['leads'] });
      void queryClient.invalidateQueries({ queryKey: ['people'] });

      if (job.status === 'completed') {
        toast.success(
          `Enriched ${job.counters.enriched} lead(s), found ${job.counters.peopleFound} decision-maker(s).`,
        );
      } else if (job.status === 'failed') {
        toast.error(job.error ?? 'LinkedIn enrichment failed.');
      } else {
        toast.info(`Stopped — ${job.counters.enriched} lead(s) enriched so far.`);
      }
    }
  }, [job, queryClient]);

  const handleEnrich = (): void => {
    if (selected.size === 0) return;

    startBatch.mutate(
      { businessIds: [...selected], maxDecisionMakersPerCompany: maxDecisionMakers },
      {
        onSuccess: (snapshot) => {
          setDismissed(false);
          setStartedJobId(snapshot.id);
          setSelected(new Set());
          toast.success('Enrichment started.');
        },
        onError: (error) => toast.error(error.message),
      },
    );
  };

  const handleStop = (): void => {
    if (!jobId) return;

    jobCommand.mutate(
      { jobId, command: 'stop' },
      { onError: (error) => toast.error(error.message) },
    );
  };

  const handleNewBatch = (): void => {
    setStartedJobId(null);
    setDismissed(true);
    reset();
  };

  return (
    <>
      <PageHeader
        title="LinkedIn Enrichment"
        description="Add company detail and search for decision-makers on LinkedIn for leads you already have."
        actions={
          job && !isActive ? (
            <Button variant="outline" onClick={handleNewBatch}>
              <Plus />
              New batch
            </Button>
          ) : undefined
        }
      />

      <LinkedInConnectDialog />

      {job ? (
        <div className="grid gap-6 lg:grid-cols-[minmax(0,1.5fr)_minmax(0,1fr)]">
          <Card>
            <CardHeader>
              <CardTitle>{isActive ? 'Running' : 'Last batch'}</CardTitle>
              <CardDescription>
                {job.currentTask || (isActive ? 'Working…' : 'Finished.')}
              </CardDescription>
            </CardHeader>
            <CardContent className="space-y-4">
              <Progress value={job.percent} />
              <p className="text-sm text-muted-foreground">
                {job.counters.processedLeads} / {job.counters.totalLeads} leads processed ·{' '}
                {job.counters.enriched} enriched · {job.counters.peopleFound} decision-maker(s) found
                {job.counters.failed > 0 ? ` · ${job.counters.failed} failed` : ''}
              </p>

              {pollError && (
                <Alert variant="warning">
                  <AlertTriangle />
                  <AlertDescription>
                    Lost touch with the server ({pollError}) — still retrying. The batch keeps running
                    either way.
                  </AlertDescription>
                </Alert>
              )}

              {job.status === 'failed' && job.error && (
                <Alert variant="destructive">
                  <XCircle />
                  <AlertDescription>{job.error}</AlertDescription>
                </Alert>
              )}

              {job.outcomes.length > 0 && (
                <ul className="scrollbar-thin max-h-[22rem] space-y-1 overflow-y-auto pr-1">
                  {job.outcomes.map((outcome) => (
                    <li
                      key={outcome.businessId}
                      className="flex items-center gap-2 rounded-lg border border-border p-2.5 text-sm"
                    >
                      {outcome.error ? (
                        <XCircle className="size-4 shrink-0 text-destructive" />
                      ) : outcome.companyEnriched ? (
                        <CheckCircle2 className="size-4 shrink-0 text-success" />
                      ) : (
                        <AlertTriangle className="size-4 shrink-0 text-warning" />
                      )}
                      <span className="min-w-0 flex-1 truncate font-medium">{outcome.businessName}</span>
                      <span className="shrink-0 text-xs text-muted-foreground">
                        {outcome.error ?? `${outcome.peopleFound} decision-maker(s)`}
                      </span>
                    </li>
                  ))}
                </ul>
              )}
            </CardContent>
          </Card>

          <Card className="h-fit">
            <CardHeader>
              <CardTitle>Status</CardTitle>
            </CardHeader>
            <CardContent className="space-y-4">
              <Badge variant={isActive ? 'default' : job.status === 'completed' ? 'success' : 'muted'}>
                {job.status}
              </Badge>

              {isActive && (
                <Button
                  variant="destructive"
                  className="w-full"
                  onClick={handleStop}
                  loading={jobCommand.isPending}
                >
                  Stop
                </Button>
              )}

              <p className="text-xs text-muted-foreground">
                Found people appear under People, linked to the lead, as the batch progresses — no need
                to wait for it to finish.
              </p>
            </CardContent>
          </Card>
        </div>
      ) : (
        <div className="grid gap-6 lg:grid-cols-[minmax(0,1.5fr)_minmax(0,1fr)]">
          <Card>
            <CardHeader>
              <CardTitle>Leads</CardTitle>
              <CardDescription>
                Choose up to {MAX_SELECTABLE} leads to enrich in one batch — each is a real, slow
                browser session against LinkedIn, run in the background.
              </CardDescription>
            </CardHeader>
            <CardContent className="space-y-4">
              <div className="relative">
                <Search className="pointer-events-none absolute left-2.5 top-1/2 size-4 -translate-y-1/2 text-muted-foreground" />
                <Input
                  value={search}
                  onChange={(event) => setSearch(event.target.value)}
                  placeholder="Search your leads…"
                  className="pl-8"
                  aria-label="Search leads"
                />
              </div>

              <label className="flex cursor-pointer items-center gap-2 text-sm text-muted-foreground">
                <Checkbox
                  checked={hideEnriched}
                  onCheckedChange={(value) => setHideEnriched(value === true)}
                />
                Hide leads already enriched
              </label>

              {leads.isPending ? (
                <div className="space-y-2">
                  {Array.from({ length: 6 }, (_, index) => (
                    <Skeleton key={index} className="h-12 w-full" />
                  ))}
                </div>
              ) : rows.length === 0 ? (
                <EmptyState
                  icon={<Contact2 />}
                  title={hideEnriched ? 'Nothing left to enrich' : 'No leads found'}
                  description={
                    hideEnriched
                      ? 'Every lead has already been enriched. Uncheck "Hide leads already enriched" to see them.'
                      : 'Run a search to collect leads first.'
                  }
                />
              ) : (
                <>
                  <label className="flex cursor-pointer items-center gap-2 text-sm">
                    <Checkbox checked={allSelected} onCheckedChange={toggleAll} />
                    Select all {selectableRows.length} shown
                    {rows.length > MAX_SELECTABLE ? ` (capped at ${MAX_SELECTABLE})` : ''}
                  </label>

                  <ul className="scrollbar-thin max-h-[26rem] space-y-1 overflow-y-auto pr-1">
                    {rows.map((row) => (
                      <li key={row.id}>
                        <label className="flex cursor-pointer items-start gap-2 rounded-lg border border-border p-2.5 hover:bg-accent/50">
                          <Checkbox
                            checked={selected.has(row.id)}
                            onCheckedChange={() => toggle(row.id)}
                            className="mt-0.5"
                          />
                          <span className="min-w-0 flex-1">
                            <span className="block truncate text-sm font-medium">{row.name}</span>
                            <span className="block truncate text-xs text-muted-foreground">
                              {[row.category, row.city].filter(Boolean).join(' · ')}
                            </span>
                            {row.industry && (
                              <span className="block truncate text-xs text-muted-foreground">
                                {row.industry}
                                {row.employeeCount ? ` · ${formatNumber(row.employeeCount)} employees` : ''}
                              </span>
                            )}
                          </span>
                          {row.lastLinkedInEnrichedAt && (
                            <Badge variant="success" className="shrink-0">
                              Enriched
                            </Badge>
                          )}
                        </label>
                      </li>
                    ))}
                  </ul>

                  <p className="text-xs text-muted-foreground">
                    Showing the {formatNumber(rows.length)} most recent of{' '}
                    {formatNumber(leads.data?.total ?? 0)}. Narrow with search to reach the rest.
                  </p>
                </>
              )}
            </CardContent>
          </Card>

          <Card className="h-fit">
            <CardHeader>
              <CardTitle>Enrich</CardTitle>
              <CardDescription>
                Company detail always runs. Decision-maker search runs when a LinkedIn URL is found.
              </CardDescription>
            </CardHeader>
            <CardContent className="space-y-5">
              <div className="max-w-xs space-y-2">
                <Label htmlFor="maxDecisionMakers">Decision-makers per company</Label>
                <Input
                  id="maxDecisionMakers"
                  type="number"
                  inputMode="numeric"
                  min={1}
                  max={50}
                  step={1}
                  value={maxDecisionMakers}
                  onChange={(event) => setMaxDecisionMakers(Number(event.target.value) || 1)}
                />
              </div>

              <Separator />

              <div className="rounded-lg bg-muted/40 p-3 text-sm">
                <span className="font-medium tabular-nums">{selected.size}</span> / {MAX_SELECTABLE}{' '}
                lead(s) selected
              </div>

              <Button
                className="w-full"
                onClick={handleEnrich}
                loading={startBatch.isPending}
                disabled={selected.size === 0}
              >
                <Sparkles />
                Enrich {selected.size || 0} lead(s)
              </Button>

              <p className="text-xs text-muted-foreground">
                Runs in the background — you can navigate away and come back; the batch keeps going
                either way.
              </p>
            </CardContent>
          </Card>
        </div>
      )}
    </>
  );
}
