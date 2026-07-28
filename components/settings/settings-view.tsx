'use client';

import * as React from 'react';
import { Controller, useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { CheckCircle2, Eye, EyeOff, KeyRound, Lock, Save, ShieldCheck, TriangleAlert } from 'lucide-react';
import { toast } from 'sonner';

import { PageHeader } from '@/components/shared/page-header';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Alert, AlertDescription, Separator, Skeleton } from '@/components/ui/misc';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { Switch } from '@/components/ui/switch';
import { useSettings, useUpdateSettings, type SettingsResponse } from '@/hooks/use-api';
import { settingsSchema, type SettingsInput } from '@/lib/validation/settings.schema';
import { MAX_RESULT_OPTIONS, RADIUS_OPTIONS } from '@/lib/constants/locations';
import type { BusinessSource } from '@/types/business';

interface NumberFieldProps {
  id: keyof SettingsInput;
  label: string;
  hint: string;
  min: number;
  max: number;
  step?: number;
}

const PERFORMANCE_FIELDS: NumberFieldProps[] = [
  {
    id: 'concurrency',
    label: 'Concurrency',
    hint: 'Websites enriched in parallel. Higher is faster but heavier on target sites.',
    min: 1,
    max: 16,
  },
  {
    id: 'delayMs',
    label: 'Delay between requests (ms)',
    hint: 'Pause applied between requests. A site’s own crawl-delay always wins if longer.',
    min: 0,
    max: 60000,
    step: 50,
  },
  {
    id: 'rateLimitPerMinute',
    label: 'Provider rate limit (per minute)',
    hint: 'Caps calls to the search provider so you stay inside quota.',
    min: 1,
    max: 600,
  },
  {
    id: 'retryAttempts',
    label: 'Retry attempts',
    hint: 'Retries on timeouts and 5xx responses, with exponential backoff.',
    min: 1,
    max: 10,
  },
  {
    id: 'requestTimeoutMs',
    label: 'Request timeout (ms)',
    hint: 'How long to wait for a single HTTP response.',
    min: 1000,
    max: 120000,
    step: 500,
  },
  {
    id: 'maxPagesPerSite',
    label: 'Max pages per website',
    hint: 'Homepage plus this many contact/about pages during enrichment.',
    min: 1,
    max: 12,
  },
];

function toFormValues(data: SettingsResponse): SettingsInput {
  const { settings } = data;

  return {
    // Always blank: the real key is never sent to the browser. Leaving it empty
    // on save preserves whatever is stored.
    googleApiKey: '',
    defaultProvider: settings.defaultProvider,
    crawlerContactEmail: settings.crawlerContactEmail,
    concurrency: settings.concurrency,
    delayMs: settings.delayMs,
    rateLimitPerMinute: settings.rateLimitPerMinute,
    retryAttempts: settings.retryAttempts,
    requestTimeoutMs: settings.requestTimeoutMs,
    maxPagesPerSite: settings.maxPagesPerSite,
    respectRobotsTxt: settings.respectRobotsTxt,
    enrichmentEnabledByDefault: settings.enrichmentEnabledByDefault,
    skipDuplicatesByDefault: settings.skipDuplicatesByDefault,
    defaultRadiusMeters: settings.defaultRadiusMeters,
    defaultMaxResults: settings.defaultMaxResults,
  };
}

export function SettingsView() {
  const query = useSettings();
  const update = useUpdateSettings();
  const [showKey, setShowKey] = React.useState(false);

  return (
    <>
      <PageHeader
        title="Settings"
        description="API keys, rate limits, and how the enrichment crawler behaves."
      />

      {query.isError && (
        <Alert variant="destructive" className="mb-6">
          <AlertDescription>{query.error.message}</AlertDescription>
        </Alert>
      )}

      {query.isPending || !query.data ? (
        <div className="space-y-6">
          <Skeleton className="h-56 w-full" />
          <Skeleton className="h-80 w-full" />
        </div>
      ) : (
        <SettingsForm
          data={query.data}
          showKey={showKey}
          onToggleKey={() => setShowKey((current) => !current)}
          submitting={update.isPending}
          onSubmit={(values) =>
            update.mutate(values, {
              onSuccess: () => toast.success('Settings saved.'),
              onError: (error) => toast.error(error.message),
            })
          }
        />
      )}
    </>
  );
}

interface SettingsFormProps {
  data: SettingsResponse;
  showKey: boolean;
  onToggleKey: () => void;
  submitting: boolean;
  onSubmit: (values: SettingsInput) => void;
}

function SettingsForm({ data, showKey, onToggleKey, submitting, onSubmit }: SettingsFormProps) {
  const { settings, providers } = data;

  const {
    control,
    register,
    handleSubmit,
    formState: { errors },
  } = useForm<SettingsInput>({
    resolver: zodResolver(settingsSchema),
    defaultValues: toFormValues(data),
  });

  return (
    <form onSubmit={handleSubmit(onSubmit)} className="space-y-6">
      <Card>
        <CardHeader>
          <CardTitle className="flex items-center gap-2">
            <KeyRound className="size-4" />
            API keys
          </CardTitle>
          <CardDescription>
            Google Places unlocks ratings, review counts and richer address data. Without a key,
            LeadMine falls back to OpenStreetMap, which needs no credentials.
          </CardDescription>
        </CardHeader>
        <CardContent className="space-y-5">
          <div className="space-y-2">
            <div className="flex flex-wrap items-center gap-2">
              <Label htmlFor="googleApiKey">Google Places API key</Label>
              {settings.googleApiKeyConfigured ? (
                <Badge variant="success">
                  <CheckCircle2 />
                  Configured
                </Badge>
              ) : (
                <Badge variant="muted">Not set</Badge>
              )}
              {settings.googleApiKeyFromEnv && (
                <Badge variant="warning">
                  <Lock />
                  Set by environment variable
                </Badge>
              )}
            </div>

            <div className="flex gap-2">
              <Input
                id="googleApiKey"
                type={showKey ? 'text' : 'password'}
                autoComplete="off"
                spellCheck={false}
                placeholder={
                  settings.googleApiKeyConfigured
                    ? `${settings.googleApiKeyMasked} — leave blank to keep`
                    : 'Paste your API key'
                }
                disabled={settings.googleApiKeyFromEnv}
                {...register('googleApiKey')}
              />
              <Button
                type="button"
                variant="outline"
                size="icon"
                onClick={onToggleKey}
                disabled={settings.googleApiKeyFromEnv}
                aria-label={showKey ? 'Hide API key' : 'Show API key'}
              >
                {showKey ? <EyeOff /> : <Eye />}
              </Button>
            </div>

            <p className="text-xs text-muted-foreground">
              {settings.googleApiKeyFromEnv
                ? 'GOOGLE_PLACES_API_KEY is set in the environment and takes precedence over anything entered here.'
                : 'Stored in your local settings file and never sent back to the browser.'}
            </p>
            {errors.googleApiKey && (
              <p className="text-xs text-destructive">{errors.googleApiKey.message}</p>
            )}
          </div>

          <Separator />

          <div className="space-y-2">
            <Label>Provider status</Label>
            <div className="grid gap-2 sm:grid-cols-2">
              {providers.map((provider) => (
                <div
                  key={provider.id}
                  className="flex items-start gap-2 rounded-lg border border-border p-3"
                >
                  {provider.ready ? (
                    <CheckCircle2 className="mt-0.5 size-4 shrink-0 text-success" />
                  ) : (
                    <TriangleAlert className="mt-0.5 size-4 shrink-0 text-warning" />
                  )}
                  <div className="min-w-0">
                    <p className="text-sm font-medium">{provider.label}</p>
                    <p className="text-xs text-muted-foreground">
                      {provider.reason ?? 'Ready to use'}
                    </p>
                  </div>
                </div>
              ))}
            </div>
          </div>
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle className="flex items-center gap-2">
            <ShieldCheck className="size-4" />
            Crawler behaviour
          </CardTitle>
          <CardDescription>
            How the enrichment crawler identifies itself and how hard it works.
          </CardDescription>
        </CardHeader>
        <CardContent className="space-y-5">
          <div className="space-y-2">
            <Label htmlFor="crawlerContactEmail">Crawler contact email</Label>
            <Input
              id="crawlerContactEmail"
              type="email"
              placeholder="you@yourcompany.com"
              {...register('crawlerContactEmail')}
            />
            <p className="text-xs text-muted-foreground">
              Included in the crawler&apos;s User-Agent so site owners can reach you. Strongly
              recommended — it is the difference between a courteous bot and an anonymous one.
            </p>
            {errors.crawlerContactEmail && (
              <p className="text-xs text-destructive">{errors.crawlerContactEmail.message}</p>
            )}
          </div>

          <Controller
            control={control}
            name="respectRobotsTxt"
            render={({ field }) => (
              <label className="flex cursor-pointer items-start justify-between gap-4 rounded-lg border border-border p-3">
                <span className="space-y-0.5">
                  <span className="block text-sm font-medium">Respect robots.txt</span>
                  <span className="block text-xs text-muted-foreground">
                    Checks each site&apos;s crawl rules before fetching and honours its crawl-delay.
                    Leave this on unless you own the sites being crawled.
                  </span>
                </span>
                <Switch
                  checked={field.value}
                  onCheckedChange={field.onChange}
                  aria-label="Respect robots.txt"
                />
              </label>
            )}
          />

          {/* A live warning is more useful than burying this in docs. */}
          <Controller
            control={control}
            name="respectRobotsTxt"
            render={({ field }) =>
              field.value ? (
                <></>
              ) : (
                <Alert variant="warning">
                  <TriangleAlert />
                  <AlertDescription>
                    With robots.txt checks off, the crawler will fetch pages that site owners have
                    asked automated clients not to read. Only do this for sites you own or have
                    permission to crawl.
                  </AlertDescription>
                </Alert>
              )
            }
          />

          <Separator />

          <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
            {PERFORMANCE_FIELDS.map((field) => (
              <div key={field.id} className="space-y-2">
                <Label htmlFor={field.id}>{field.label}</Label>
                <Input
                  id={field.id}
                  type="number"
                  min={field.min}
                  max={field.max}
                  step={field.step ?? 1}
                  {...register(field.id, { valueAsNumber: true })}
                />
                <p className="text-xs text-muted-foreground">{field.hint}</p>
                {errors[field.id] && (
                  <p className="text-xs text-destructive">{errors[field.id]?.message}</p>
                )}
              </div>
            ))}
          </div>
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle>Search defaults</CardTitle>
          <CardDescription>Pre-filled values for every new search.</CardDescription>
        </CardHeader>
        <CardContent className="space-y-5">
          <div className="grid gap-4 sm:grid-cols-3">
            <div className="space-y-2">
              <Label htmlFor="defaultProvider">Default data source</Label>
              <Controller
                control={control}
                name="defaultProvider"
                render={({ field }) => (
                  <Select
                    value={field.value}
                    onValueChange={(next) => field.onChange(next as BusinessSource)}
                  >
                    <SelectTrigger id="defaultProvider">
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                      {providers.map((provider) => (
                        <SelectItem key={provider.id} value={provider.id}>
                          {provider.label}
                        </SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                )}
              />
            </div>

            <div className="space-y-2">
              <Label htmlFor="defaultRadiusMeters">Default radius</Label>
              <Controller
                control={control}
                name="defaultRadiusMeters"
                render={({ field }) => (
                  <Select
                    value={String(field.value)}
                    onValueChange={(next) => field.onChange(Number(next))}
                  >
                    <SelectTrigger id="defaultRadiusMeters">
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                      {RADIUS_OPTIONS.map((option) => (
                        <SelectItem key={option.value} value={String(option.value)}>
                          {option.label}
                        </SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                )}
              />
            </div>

            <div className="space-y-2">
              <Label htmlFor="defaultMaxResults">Default max results</Label>
              <Controller
                control={control}
                name="defaultMaxResults"
                render={({ field }) => (
                  <Select
                    value={String(field.value)}
                    onValueChange={(next) => field.onChange(Number(next))}
                  >
                    <SelectTrigger id="defaultMaxResults">
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                      {MAX_RESULT_OPTIONS.map((option) => (
                        <SelectItem key={option} value={String(option)}>
                          {option}
                        </SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                )}
              />
            </div>
          </div>

          <div className="space-y-3">
            <Controller
              control={control}
              name="enrichmentEnabledByDefault"
              render={({ field }) => (
                <label className="flex cursor-pointer items-center justify-between gap-4">
                  <span className="text-sm">Enable contact enrichment by default</span>
                  <Switch
                    checked={field.value}
                    onCheckedChange={field.onChange}
                    aria-label="Enable contact enrichment by default"
                  />
                </label>
              )}
            />
            <Controller
              control={control}
              name="skipDuplicatesByDefault"
              render={({ field }) => (
                <label className="flex cursor-pointer items-center justify-between gap-4">
                  <span className="text-sm">Skip duplicates by default</span>
                  <Switch
                    checked={field.value}
                    onCheckedChange={field.onChange}
                    aria-label="Skip duplicates by default"
                  />
                </label>
              )}
            />
          </div>
        </CardContent>
      </Card>

      <div className="flex items-center gap-3">
        <Button type="submit" size="lg" loading={submitting}>
          <Save />
          Save settings
        </Button>
        {settings.updatedAt && new Date(settings.updatedAt).getTime() > 0 && (
          <p className="text-xs text-muted-foreground">
            Last saved {new Date(settings.updatedAt).toLocaleString()}
          </p>
        )}
      </div>
    </form>
  );
}
