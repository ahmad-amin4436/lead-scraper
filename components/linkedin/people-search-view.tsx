'use client';

import * as React from 'react';
import { AlertTriangle, Building2, CheckCircle2, Loader2, Plus, Search, XCircle } from 'lucide-react';
import { toast } from 'sonner';
import { useQueryClient } from '@tanstack/react-query';

import { PageHeader } from '@/components/shared/page-header';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Alert, AlertDescription, EmptyState } from '@/components/ui/misc';
import {
  useActivePeopleSearchJobs,
  usePeopleSearchJobCommand,
  useStartPeopleSearch,
} from '@/hooks/use-people-search';
import { usePeopleSearchJobPoll } from '@/hooks/use-people-search-job-poll';

function isActiveStatus(status: string): boolean {
  return status === 'running' || status === 'queued' || status === 'stopping';
}

/**
 * Searches one named company's LinkedIn People tab and saves every match as a
 * standalone person — no lead required first.
 *
 * Scoped to one company at a time rather than a general LinkedIn search: a
 * cross-company search returns blurred "LinkedIn Member" placeholders with no
 * profile link for an account without much of a network (confirmed live
 * against this app's own LinkedIn account), but a company's own People tab
 * does not have that restriction. Results are saved automatically as the
 * search runs — there is no separate "select and save" step, since LinkedIn
 * exposes no email/phone/website/WhatsApp on a people search to choose
 * between in the first place.
 *
 * Same start-then-poll shape as `LinkedInEnrichmentView`, but the search
 * itself is one atomic browser operation rather than a per-lead loop, so
 * there is no meaningful task-by-task percentage — progress is communicated
 * through `currentTask` instead of a progress bar.
 */
export function PeopleSearchView() {
  const queryClient = useQueryClient();

  const [companyName, setCompanyName] = React.useState('');
  const [keywords, setKeywords] = React.useState('');
  const [location, setLocation] = React.useState('');
  const [maxResults, setMaxResults] = React.useState(25);

  const [startedJobId, setStartedJobId] = React.useState<string | null>(null);
  const [dismissed, setDismissed] = React.useState(false);

  const startSearch = useStartPeopleSearch();
  const jobCommand = usePeopleSearchJobCommand();

  // Reconnect to a search already in progress (e.g. after a page reload).
  const activeJobs = useActivePeopleSearchJobs();
  const adoptedJobId = React.useMemo(() => {
    if (startedJobId || dismissed) return null;
    return activeJobs.data?.find((j) => isActiveStatus(j.status))?.id ?? null;
  }, [startedJobId, dismissed, activeJobs.data]);

  const jobId = startedJobId ?? adoptedJobId;
  const { job, error: pollError, reset } = usePeopleSearchJobPoll(jobId);

  const isActive = job !== null && isActiveStatus(job.status);

  // Refresh the People list once a search finishes so the new rows show up.
  const previousStatus = React.useRef<string | null>(null);
  React.useEffect(() => {
    if (!job) return;
    if (previousStatus.current === job.status) return;
    previousStatus.current = job.status;

    if (job.status === 'completed' || job.status === 'stopped' || job.status === 'failed') {
      void queryClient.invalidateQueries({ queryKey: ['people'] });

      if (job.status === 'completed') {
        toast.success(`Found and saved ${job.counters.found} people.`);
      } else if (job.status === 'failed') {
        toast.error(job.error ?? 'The search failed.');
      } else {
        toast.info('Stopped.');
      }
    }
  }, [job, queryClient]);

  const handleSearch = (): void => {
    if (!companyName.trim()) return;

    startSearch.mutate(
      {
        companyName: companyName.trim(),
        keywords: keywords.trim() || undefined,
        location: location.trim() || undefined,
        maxResults,
      },
      {
        onSuccess: (snapshot) => {
          setDismissed(false);
          setStartedJobId(snapshot.id);
          toast.success('Search started.');
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

  const handleNewSearch = (): void => {
    setStartedJobId(null);
    setDismissed(true);
    reset();
  };

  return (
    <>
      <PageHeader
        title="LinkedIn People Search"
        description="Search one company's LinkedIn People tab and save every match automatically."
        actions={
          job && !isActive ? (
            <Button variant="outline" onClick={handleNewSearch}>
              <Plus />
              New search
            </Button>
          ) : undefined
        }
      />

      {job ? (
        <div className="grid gap-6 lg:grid-cols-[minmax(0,1.5fr)_minmax(0,1fr)]">
          <Card>
            <CardHeader>
              <CardTitle>{isActive ? 'Running' : 'Last search'}</CardTitle>
              <CardDescription className="flex items-center gap-2">
                {isActive && <Loader2 className="size-3.5 animate-spin" />}
                {job.currentTask || (isActive ? 'Working…' : 'Finished.')}
              </CardDescription>
            </CardHeader>
            <CardContent className="space-y-4">
              <p className="text-sm text-muted-foreground">
                {job.counters.found} of up to {job.counters.totalTasks} requested people found and saved
              </p>

              {pollError && (
                <Alert variant="warning">
                  <AlertTriangle />
                  <AlertDescription>
                    Lost touch with the server ({pollError}) — still retrying. The search keeps running
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

              {job.outcomes.length > 0 ? (
                <ul className="scrollbar-thin max-h-[26rem] space-y-1 overflow-y-auto pr-1">
                  {job.outcomes.map((outcome, index) => (
                    <li
                      key={`${outcome.linkedInUrl}-${index}`}
                      className="flex items-start gap-2 rounded-lg border border-border p-2.5 text-sm"
                    >
                      <CheckCircle2 className="mt-0.5 size-4 shrink-0 text-success" />
                      <span className="min-w-0 flex-1">
                        <span className="block truncate font-medium">{outcome.fullName}</span>
                        <span className="block truncate text-xs text-muted-foreground">
                          {[outcome.jobTitle, outcome.location].filter(Boolean).join(' · ')}
                        </span>
                      </span>
                      {outcome.isDecisionMaker && (
                        <Badge variant="success" className="shrink-0">
                          Decision-maker
                        </Badge>
                      )}
                    </li>
                  ))}
                </ul>
              ) : (
                !isActive && (
                  <EmptyState
                    icon={<Building2 />}
                    title="No matches"
                    description="Nobody on this company's People tab matched your filters."
                  />
                )
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
                Saved people appear under People as the search runs — no need to wait for it to finish.
              </p>
            </CardContent>
          </Card>
        </div>
      ) : (
        <div className="mx-auto max-w-xl">
          <Card>
            <CardHeader>
              <CardTitle>Search a company</CardTitle>
              <CardDescription>
                Every match is saved automatically — name, title, LinkedIn profile URL, location, and
                whether they look like a decision-maker. LinkedIn does not expose email, phone or
                website on a people search, so those stay blank until enriched separately.
              </CardDescription>
            </CardHeader>
            <CardContent className="space-y-5">
              <div className="space-y-2">
                <Label htmlFor="companyName">Company name</Label>
                <Input
                  id="companyName"
                  value={companyName}
                  onChange={(event) => setCompanyName(event.target.value)}
                  placeholder="e.g. Acme Corporation"
                />
              </div>

              <div className="space-y-2">
                <Label htmlFor="keywords">Job title / keywords</Label>
                <Input
                  id="keywords"
                  value={keywords}
                  onChange={(event) => setKeywords(event.target.value)}
                  placeholder="e.g. Marketing Manager"
                />
              </div>

              <div className="space-y-2">
                <Label htmlFor="location">Location</Label>
                <Input
                  id="location"
                  value={location}
                  onChange={(event) => setLocation(event.target.value)}
                  placeholder="e.g. London"
                />
                <p className="text-xs text-muted-foreground">
                  Matched against each result&apos;s own displayed location text.
                </p>
              </div>

              <div className="max-w-[10rem] space-y-2">
                <Label htmlFor="maxResults">Max results</Label>
                <Input
                  id="maxResults"
                  type="number"
                  inputMode="numeric"
                  min={1}
                  max={100}
                  step={1}
                  value={maxResults}
                  onChange={(event) => setMaxResults(Number(event.target.value) || 1)}
                />
              </div>

              <Button
                className="w-full"
                onClick={handleSearch}
                loading={startSearch.isPending}
                disabled={!companyName.trim()}
              >
                <Search />
                Search &amp; save
              </Button>

              <p className="text-xs text-muted-foreground">
                Runs in the background — you can navigate away and come back; the search keeps going
                either way.
              </p>
            </CardContent>
          </Card>
        </div>
      )}
    </>
  );
}
