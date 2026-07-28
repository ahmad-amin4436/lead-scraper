'use client';

import * as React from 'react';
import Link from 'next/link';
import { useSearchParams } from 'next/navigation';
import { Database, Plus } from 'lucide-react';
import { toast } from 'sonner';

import { JobMonitor } from '@/components/search/job-monitor';
import { SearchForm } from '@/components/search/search-form';
import { PageHeader } from '@/components/shared/page-header';
import { Button } from '@/components/ui/button';
import { Alert, AlertDescription, Skeleton } from '@/components/ui/misc';
import { useActiveJobs, useJobCommand, useSettings, useStartSearch } from '@/hooks/use-api';
import { useJobPoll } from '@/hooks/use-job-poll';
import { isValidCategory } from '@/lib/constants/categories';
import { isValidCountry } from '@/lib/constants/locations';
import type { SearchRequestInput } from '@/lib/validation/search.schema';
import type { SearchRequest } from '@/types/search';
import { useQueryClient } from '@tanstack/react-query';

/**
 * Reads a rerun payload from the query string (`?rerun=<encoded JSON>`), which
 * the History page uses to prefill this form.
 */
function parseRerun(raw: string | null): Partial<SearchRequestInput> | null {
  if (!raw) return null;

  try {
    const parsed = JSON.parse(decodeURIComponent(raw)) as Partial<SearchRequestInput>;
    // Trust nothing from the URL: keep only well-formed, known-good values.
    const categories = Array.isArray(parsed.categories)
      ? parsed.categories.filter((id): id is string => typeof id === 'string' && isValidCategory(id))
      : [];
    const cities = Array.isArray(parsed.cities)
      ? parsed.cities.filter((city): city is string => typeof city === 'string').slice(0, 25)
      : [];

    return {
      ...parsed,
      categories,
      cities,
      country: typeof parsed.country === 'string' && isValidCountry(parsed.country)
        ? parsed.country
        : undefined,
    };
  } catch {
    return null;
  }
}

export function SearchView() {
  const searchParams = useSearchParams();
  const queryClient = useQueryClient();

  const settings = useSettings();
  const startSearch = useStartSearch();
  const jobCommand = useJobCommand();

  // The job we explicitly started this session, and a flag set when the user
  // dismisses the panel ("New search") so we don't re-adopt a finished run.
  const [startedJobId, setStartedJobId] = React.useState<string | null>(null);
  const [dismissed, setDismissed] = React.useState(false);

  // Reconnect to a run already in progress (e.g. after a page reload) so its
  // monitor panel comes back and can be stopped, rather than dropping to the
  // form. Derived, not effect-driven, so it stays in sync without extra renders.
  const activeJobs = useActiveJobs();
  const adoptedJobId = React.useMemo(() => {
    if (startedJobId || dismissed) return null;
    return (
      activeJobs.data?.find(
        (j) => j.status === 'running' || j.status === 'queued' || j.status === 'stopping',
      )?.id ?? null
    );
  }, [startedJobId, dismissed, activeJobs.data]);

  const jobId = startedJobId ?? adoptedJobId;
  const { job, error: streamError, reset } = useJobPoll(jobId);

  const rerun = React.useMemo(() => parseRerun(searchParams.get('rerun')), [searchParams]);

  const defaults = React.useMemo<SearchRequestInput | null>(() => {
    if (!settings.data) return null;
    const { settings: config } = settings.data;

    return {
      categories: rerun?.categories?.length ? rerun.categories : ['restaurant'],
      country: rerun?.country ?? 'US',
      state: rerun?.state,
      cities: rerun?.cities?.length ? rerun.cities : [],
      radiusMeters: rerun?.radiusMeters ?? config.defaultRadiusMeters,
      maxResults: rerun?.maxResults ?? config.defaultMaxResults,
      enrichContacts: rerun?.enrichContacts ?? config.enrichmentEnabledByDefault,
      skipDuplicates: rerun?.skipDuplicates ?? config.skipDuplicatesByDefault,
      minRating: rerun?.minRating,
      minReviews: rerun?.minReviews,
      provider: rerun?.provider ?? config.defaultProvider,
    };
  }, [settings.data, rerun]);

  const isActive =
    job !== null &&
    (job.status === 'running' || job.status === 'queued' || job.status === 'stopping');

  // Refresh the database views once a run finishes so counts stay accurate.
  const previousStatus = React.useRef<string | null>(null);
  React.useEffect(() => {
    if (!job) return;
    if (previousStatus.current === job.status) return;
    previousStatus.current = job.status;

    if (job.status === 'completed' || job.status === 'stopped' || job.status === 'failed') {
      void queryClient.invalidateQueries({ queryKey: ['businesses'] });
      void queryClient.invalidateQueries({ queryKey: ['history'] });

      if (job.status === 'completed') {
        toast.success(`Search finished — ${job.counters.saved} lead(s) saved.`);
      } else if (job.status === 'failed') {
        toast.error(job.error ?? 'Search failed.');
      } else {
        toast.info(`Search stopped — ${job.counters.saved} lead(s) saved.`);
      }
    }
  }, [job, queryClient]);

  const handleSubmit = (request: SearchRequest): void => {
    startSearch.mutate(request, {
      onSuccess: (snapshot) => {
        setDismissed(false);
        setStartedJobId(snapshot.id);
        toast.success('Search started.');
      },
      onError: (error) => {
        toast.error(error.message);
      },
    });
  };

  const handleStop = (): void => {
    if (!jobId) return;

    jobCommand.mutate(
      { jobId, command: 'stop' },
      {
        onError: (error) => toast.error(error.message),
      },
    );
  };

  return (
    <>
      <PageHeader
        title="Search businesses"
        description="Sweep categories across cities, enrich contacts, and save leads to your database."
        actions={
          job && !isActive ? (
            <>
              <Button
                variant="outline"
                onClick={() => {
                  setStartedJobId(null);
                  setDismissed(true);
                  reset();
                }}
              >
                <Plus />
                New search
              </Button>
              <Button asChild>
                <Link href="/database">
                  <Database />
                  View database
                </Link>
              </Button>
            </>
          ) : null
        }
      />

      {settings.isError && (
        <Alert variant="destructive" className="mb-6">
          <AlertDescription>Could not load settings: {settings.error.message}</AlertDescription>
        </Alert>
      )}

      {job ? (
        <JobMonitor
          job={job}
          streamError={streamError}
          onStop={handleStop}
          commandPending={jobCommand.isPending}
        />
      ) : settings.isPending || !defaults || (activeJobs.isPending && !jobId) ? (
        <div className="space-y-6">
          <Skeleton className="h-64 w-full" />
          <Skeleton className="h-80 w-full" />
        </div>
      ) : (
        <SearchForm
          defaults={defaults}
          providers={settings.data?.providers ?? []}
          submitting={startSearch.isPending}
          disabled={startSearch.isPending}
          onSubmit={handleSubmit}
        />
      )}
    </>
  );
}
