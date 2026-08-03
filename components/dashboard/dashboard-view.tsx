'use client';

import Link from 'next/link';
import {
  AtSign,
  Database,
  Globe,
  Phone,
  Search,
  Share2,
  MessageCircle,
  ShieldCheck,
  Star,
  TrendingUp,
} from 'lucide-react';

import { PageHeader } from '@/components/shared/page-header';
import { StatCard } from '@/components/shared/stat-card';
import { JobStatusBadge } from '@/components/shared/status-badge';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
import { Alert, AlertDescription, EmptyState, Skeleton } from '@/components/ui/misc';
import { Progress } from '@/components/ui/progress';
import { Table, TableBody, TableCell, TableContainer, TableHead, TableHeader, TableRow } from '@/components/ui/table';
import { useBusinessStats, useHistory } from '@/hooks/use-api';
import { getCountryName } from '@/lib/constants/locations';
import { formatDuration, formatNumber, formatRelativeTime } from '@/utils/format';

function coveragePercent(part: number, total: number): number {
  return total === 0 ? 0 : Math.round((part / total) * 100);
}

export function DashboardView() {
  const stats = useBusinessStats();
  const history = useHistory({ pageSize: 6 });

  const data = stats.data;
  const total = data?.total ?? 0;

  const coverage = [
    { label: 'Email', value: data?.withEmail ?? 0, icon: AtSign },
    { label: 'Phone', value: data?.withPhone ?? 0, icon: Phone },
    { label: 'Website', value: data?.withWebsite ?? 0, icon: Globe },
    { label: 'Social profile', value: data?.withSocial ?? 0, icon: Share2 },
  ];

  const peakDay = Math.max(1, ...(data?.addedLast7Days ?? []).map((d) => d.count));

  return (
    <>
      <PageHeader
        title="Dashboard"
        description="Everything currently sitting in your Excel lead database."
        actions={
          <Button asChild>
            <Link href="/search">
              <Search />
              New search
            </Link>
          </Button>
        }
      />

      {stats.isError && (
        <Alert variant="destructive" className="mb-6">
          <AlertDescription>
            Could not load statistics: {stats.error.message}
          </AlertDescription>
        </Alert>
      )}

      <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-3 2xl:grid-cols-5">
        <StatCard
          label="Total leads"
          value={formatNumber(total)}
          hint="Saved in your database"
          icon={Database}
          loading={stats.isPending}
        />
        <StatCard
          label="With email"
          value={formatNumber(data?.withEmail ?? 0)}
          hint={`${coveragePercent(data?.withEmail ?? 0, total)}% of database`}
          icon={AtSign}
          tone="success"
          loading={stats.isPending}
        />
        <StatCard
          label="Deliverable emails"
          value={formatNumber(data?.verifiedEmails ?? 0)}
          hint="Domain accepts mail"
          icon={ShieldCheck}
          tone="success"
          loading={stats.isPending}
        />
        <StatCard
          label="WhatsApp reachable"
          value={formatNumber(data?.whatsappReachable ?? 0)}
          hint="Confirmed or likely"
          icon={MessageCircle}
          tone="warning"
          loading={stats.isPending}
        />
        <StatCard
          label="Average rating"
          value={data?.averageRating ? data.averageRating.toFixed(2) : '—'}
          hint="Across rated businesses"
          icon={Star}
          tone="muted"
          loading={stats.isPending}
        />
      </div>

      <div className="mt-6 grid gap-6 lg:grid-cols-3">
        <Card className="lg:col-span-2">
          <CardHeader>
            <CardTitle>Contact coverage</CardTitle>
            <CardDescription>
              How much of the database is actually reachable, by channel.
            </CardDescription>
          </CardHeader>
          <CardContent className="space-y-5">
            {stats.isPending ? (
              <div className="space-y-4">
                {[0, 1, 2, 3].map((key) => (
                  <Skeleton key={key} className="h-10 w-full" />
                ))}
              </div>
            ) : total === 0 ? (
              <EmptyState
                icon={<Database />}
                title="No leads yet"
                description="Run your first search to start filling the database."
                action={
                  <Button asChild size="sm">
                    <Link href="/search">Start a search</Link>
                  </Button>
                }
              />
            ) : (
              coverage.map((item) => {
                const percent = coveragePercent(item.value, total);
                const Icon = item.icon;

                return (
                  <div key={item.label} className="space-y-1.5">
                    <div className="flex items-center justify-between gap-3 text-sm">
                      <span className="flex items-center gap-2 font-medium">
                        <Icon className="size-4 text-muted-foreground" />
                        {item.label}
                      </span>
                      <span className="tabular-nums text-muted-foreground">
                        {formatNumber(item.value)} / {formatNumber(total)} ({percent}%)
                      </span>
                    </div>
                    <Progress value={percent} />
                  </div>
                );
              })
            )}
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle>Leads added</CardTitle>
            <CardDescription>Last 7 days</CardDescription>
          </CardHeader>
          <CardContent>
            {stats.isPending ? (
              <Skeleton className="h-40 w-full" />
            ) : (
              <div className="flex h-40 items-end justify-between gap-2">
                {(data?.addedLast7Days ?? []).map((day) => {
                  const height = Math.round((day.count / peakDay) * 100);
                  const weekday = new Date(`${day.date}T00:00:00Z`).toLocaleDateString('en-US', {
                    weekday: 'short',
                    timeZone: 'UTC',
                  });

                  return (
                    <div key={day.date} className="flex flex-1 flex-col items-center gap-2">
                      <div className="flex w-full flex-1 items-end">
                        <div
                          className="w-full rounded-t-md bg-primary/80 transition-all"
                          style={{ height: `${Math.max(height, day.count > 0 ? 6 : 2)}%` }}
                          title={`${day.count} lead(s) on ${day.date}`}
                        />
                      </div>
                      <span className="text-[10px] text-muted-foreground">{weekday}</span>
                    </div>
                  );
                })}
              </div>
            )}
          </CardContent>
        </Card>
      </div>

      <div className="mt-6 grid gap-6 lg:grid-cols-2">
        <Card>
          <CardHeader>
            <CardTitle>Top categories</CardTitle>
            <CardDescription>Where your leads are concentrated</CardDescription>
          </CardHeader>
          <CardContent>
            {stats.isPending ? (
              <Skeleton className="h-32 w-full" />
            ) : (data?.byCategory.length ?? 0) === 0 ? (
              <p className="text-sm text-muted-foreground">No data yet.</p>
            ) : (
              <ul className="space-y-3">
                {data?.byCategory.map((item) => (
                  <li key={item.label} className="space-y-1.5">
                    <div className="flex items-center justify-between gap-3 text-sm">
                      <span className="truncate">{item.label}</span>
                      <span className="tabular-nums text-muted-foreground">
                        {formatNumber(item.count)}
                      </span>
                    </div>
                    <Progress value={coveragePercent(item.count, total)} className="h-1.5" />
                  </li>
                ))}
              </ul>
            )}
          </CardContent>
        </Card>

        <Card>
          <CardHeader className="flex-row items-center justify-between gap-3">
            <div className="space-y-1.5">
              <CardTitle>Recent searches</CardTitle>
              <CardDescription>Your latest runs</CardDescription>
            </div>
            <Button asChild variant="ghost" size="sm">
              <Link href="/history">View all</Link>
            </Button>
          </CardHeader>
          <CardContent className="px-0 pb-0">
            {history.isPending ? (
              <div className="space-y-2 px-5 pb-5">
                {[0, 1, 2].map((key) => (
                  <Skeleton key={key} className="h-10 w-full" />
                ))}
              </div>
            ) : (history.data?.items.length ?? 0) === 0 ? (
              <div className="px-5 pb-5">
                <EmptyState
                  icon={<TrendingUp />}
                  title="No searches yet"
                  description="Run a search and it will show up here."
                />
              </div>
            ) : (
              <TableContainer>
                <Table>
                  <TableHeader>
                    <TableRow>
                      <TableHead className="pl-5">Search</TableHead>
                      <TableHead>Saved</TableHead>
                      <TableHead>Time</TableHead>
                      <TableHead className="pr-5">Status</TableHead>
                    </TableRow>
                  </TableHeader>
                  <TableBody>
                    {history.data?.items.map((entry) => (
                      <TableRow key={entry.id}>
                        <TableCell className="pl-5">
                          <p className="max-w-[220px] truncate font-medium">
                            {entry.request.categories.length} categories ·{' '}
                            {getCountryName(entry.request.country)}
                          </p>
                          <p className="max-w-[220px] truncate text-xs text-muted-foreground">
                            {entry.request.cities.join(', ')}
                          </p>
                        </TableCell>
                        <TableCell className="tabular-nums">{formatNumber(entry.saved)}</TableCell>
                        <TableCell className="whitespace-nowrap text-xs text-muted-foreground">
                          <p>{formatRelativeTime(entry.startedAt)}</p>
                          <p>{formatDuration(entry.elapsedMs)}</p>
                        </TableCell>
                        <TableCell className="pr-5">
                          <JobStatusBadge status={entry.status} />
                        </TableCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
              </TableContainer>
            )}
          </CardContent>
        </Card>
      </div>
    </>
  );
}
