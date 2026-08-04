'use client';

import * as React from 'react';
import { Controller, useForm, useWatch } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { Play, RefreshCw, TriangleAlert } from 'lucide-react';

import { CategoryPicker } from '@/components/search/category-picker';
import { CityPicker } from '@/components/search/city-picker';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
import { Combobox } from '@/components/ui/combobox';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Alert, AlertDescription, Separator } from '@/components/ui/misc';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { Switch } from '@/components/ui/switch';
import type { ProviderStatus } from '@/hooks/use-api';
import { usePersistSearchFilters } from '@/hooks/use-persisted-search-filters';
import {
  MAX_RESULT_PRESETS,
  RADIUS_PRESETS,
  getCountryOptions,
  getStateOptions,
} from '@/lib/constants/geo';
import { LEAD_KIND_META, LEAD_KIND_OPTIONS, type LeadKind } from '@/lib/constants/lead-kinds';
import { cn } from '@/lib/utils';
import { searchRequestSchema, type SearchRequestInput } from '@/lib/validation/search.schema';
import type { BusinessSource } from '@/types/business';
import type { SearchRequest } from '@/types/search';

interface SearchFormProps {
  defaults: SearchRequestInput;
  providers: ProviderStatus[];
  submitting: boolean;
  disabled: boolean;
  onSubmit: (request: SearchRequest) => void;
}

export function SearchForm({ defaults, providers, submitting, disabled, onSubmit }: SearchFormProps) {
  const {
    control,
    handleSubmit,
    setValue,
    reset,
    formState: { errors },
  } = useForm<SearchRequestInput>({
    resolver: zodResolver(searchRequestSchema),
    defaultValues: defaults,
  });

  // useWatch subscribes per-field, so only the parts that depend on these
  // values re-render (and unlike `watch()` it is safe to memoize around).
  const country = useWatch({ control, name: 'country' });
  const state = useWatch({ control, name: 'state' });
  const provider = useWatch({ control, name: 'provider' });
  const leadKind = useWatch({ control, name: 'leadKind' });

  const countryOptions = React.useMemo(() => getCountryOptions(), []);
  const stateOptions = React.useMemo(() => getStateOptions(country), [country]);

  usePersistSearchFilters(control);

  const activeProvider = providers.find((item) => item.id === provider);

  /** Changing country invalidates the region and every chosen city. */
  const handleCountryChange = (next: string | null): void => {
    if (!next) return;
    setValue('country', next, { shouldValidate: true });
    setValue('state', undefined);
    setValue('cities', []);
  };

  /** Changing region invalidates the cities picked under the old one. */
  const handleStateChange = (next: string | null): void => {
    setValue('state', next ?? undefined);
    setValue('cities', []);
  };

  return (
    <form
      onSubmit={handleSubmit((values) => onSubmit(values satisfies SearchRequest))}
      className="space-y-6"
    >
      <Card>
        <CardHeader>
          <CardTitle>Where to look</CardTitle>
          <CardDescription>
            Pick a data source and the places to sweep. Every city is searched for every category.
          </CardDescription>
        </CardHeader>
        <CardContent className="space-y-5">
          <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
            <div className="space-y-2">
              <Label htmlFor="provider">Data source</Label>
              <Controller
                control={control}
                name="provider"
                render={({ field }) => (
                  <Select
                    value={field.value ?? 'openstreetmap'}
                    onValueChange={(next) => field.onChange(next as BusinessSource)}
                    disabled={disabled}
                  >
                    <SelectTrigger id="provider">
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                      {providers.map((item) => (
                        <SelectItem key={item.id} value={item.id}>
                          {item.label}
                          {!item.ready && ' (needs API key)'}
                        </SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                )}
              />
            </div>

            <div className="space-y-2">
              <Label htmlFor="country">Country</Label>
              <Combobox
                id="country"
                options={countryOptions}
                value={country ?? null}
                onChange={handleCountryChange}
                placeholder="Select a country"
                searchPlaceholder="Search 250 countries…"
                disabled={disabled}
                aria-label="Country"
              />
              {errors.country && (
                <p className="text-xs text-destructive">{errors.country.message}</p>
              )}
            </div>

            <div className="space-y-2">
              <Label htmlFor="state">State / Region</Label>
              <Combobox
                id="state"
                options={stateOptions}
                value={state ?? null}
                onChange={handleStateChange}
                placeholder={
                  stateOptions.length === 0 ? 'No regions for this country' : 'Any region'
                }
                searchPlaceholder="Search regions…"
                emptyMessage="No matching region."
                disabled={disabled || stateOptions.length === 0}
                clearable
                aria-label="State or region"
              />
              {stateOptions.length > 0 && (
                <p className="text-xs text-muted-foreground">
                  {stateOptions.length} region(s). Leave blank to search the whole country.
                </p>
              )}
            </div>
          </div>

          <div className="space-y-2">
            <Label>Cities</Label>
            <Controller
              control={control}
              name="cities"
              render={({ field }) => (
                <CityPicker
                  countryCode={country}
                  stateCode={state ?? null}
                  value={field.value}
                  onChange={field.onChange}
                  disabled={disabled}
                />
              )}
            />
            {errors.cities && <p className="text-xs text-destructive">{errors.cities.message}</p>}
          </div>
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle>What to look for</CardTitle>
          <CardDescription>
            Each selected category is searched separately in every city.
          </CardDescription>
        </CardHeader>
        <CardContent className="space-y-4">
          <Controller
            control={control}
            name="categories"
            render={({ field }) => (
              <CategoryPicker value={field.value} onChange={field.onChange} disabled={disabled} />
            )}
          />
          {errors.categories && (
            <p className="text-xs text-destructive">{errors.categories.message}</p>
          )}
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle>Which leads do you want to keep?</CardTitle>
          <CardDescription>
            Filtering during the run means the database only fills with leads you can actually use.
          </CardDescription>
        </CardHeader>
        <CardContent>
          <Controller
            control={control}
            name="leadKind"
            render={({ field }) => (
              <div className="grid gap-2 sm:grid-cols-2">
                {LEAD_KIND_OPTIONS.map((option) => {
                  const active = field.value === option.value;

                  return (
                    <button
                      key={option.value}
                      type="button"
                      onClick={() => field.onChange(option.value as LeadKind)}
                      disabled={disabled}
                      aria-pressed={active}
                      className={cn(
                        'rounded-lg border p-3 text-left transition-colors disabled:opacity-50',
                        active
                          ? 'border-primary bg-primary/8'
                          : 'border-border hover:bg-accent',
                      )}
                    >
                      <span
                        className={cn(
                          'block text-sm font-medium',
                          active && 'text-primary',
                        )}
                      >
                        {option.label}
                      </span>
                      <span className="mt-0.5 block text-xs text-muted-foreground">
                        {option.description}
                      </span>
                    </button>
                  );
                })}
              </div>
            )}
          />

          {/* Enrichment is what discovers emails, so these choices are unreachable without it. */}
          {(leadKind === 'Enriched' ||
            leadKind === 'EmailOnly' ||
            leadKind === 'DeliverableEmail' ||
            leadKind === 'WhatsAppOnly') && (
            <Alert variant="info" className="mt-4">
              <TriangleAlert />
              <AlertDescription>
                “{LEAD_KIND_META[leadKind].label}” relies on contact enrichment. Keep
                <strong> Enrich contacts</strong> switched on below, or the run will keep nothing.
              </AlertDescription>
            </Alert>
          )}
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle>Limits &amp; quality</CardTitle>
          <CardDescription>
            Keep runs focused — narrower searches finish faster and burn less API quota.
          </CardDescription>
        </CardHeader>
        <CardContent className="space-y-5">
          <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
            <div className="space-y-2">
              <Label htmlFor="radiusMeters">Radius (metres)</Label>
              <Controller
                control={control}
                name="radiusMeters"
                render={({ field }) => (
                  <>
                    <Input
                      id="radiusMeters"
                      type="number"
                      inputMode="numeric"
                      min={500}
                      max={50000}
                      step={100}
                      placeholder="10000"
                      disabled={disabled}
                      value={Number.isFinite(field.value) ? field.value : ''}
                      onChange={(event) =>
                        field.onChange(
                          event.target.value === '' ? undefined : Number(event.target.value),
                        )
                      }
                    />
                    <div className="flex flex-wrap gap-1">
                      {RADIUS_PRESETS.map((preset) => (
                        <button
                          key={preset}
                          type="button"
                          onClick={() => field.onChange(preset)}
                          disabled={disabled}
                          className="rounded-full border border-dashed border-border px-2 py-0.5 text-[11px] text-muted-foreground transition-colors hover:border-primary hover:text-primary disabled:opacity-50"
                        >
                          {preset / 1000} km
                        </button>
                      ))}
                    </div>
                  </>
                )}
              />
              {errors.radiusMeters && (
                <p className="text-xs text-destructive">{errors.radiusMeters.message}</p>
              )}
            </div>

            <div className="space-y-2">
              <Label htmlFor="maxResults">Max results per search</Label>
              <Controller
                control={control}
                name="maxResults"
                render={({ field }) => (
                  <>
                    <Input
                      id="maxResults"
                      type="number"
                      inputMode="numeric"
                      min={1}
                      max={500}
                      step={1}
                      placeholder="60"
                      disabled={disabled}
                      value={Number.isFinite(field.value) ? field.value : ''}
                      onChange={(event) =>
                        field.onChange(
                          event.target.value === '' ? undefined : Number(event.target.value),
                        )
                      }
                    />
                    <div className="flex flex-wrap gap-1">
                      {MAX_RESULT_PRESETS.map((preset) => (
                        <button
                          key={preset}
                          type="button"
                          onClick={() => field.onChange(preset)}
                          disabled={disabled}
                          className="rounded-full border border-dashed border-border px-2 py-0.5 text-[11px] text-muted-foreground transition-colors hover:border-primary hover:text-primary disabled:opacity-50"
                        >
                          {preset}
                        </button>
                      ))}
                    </div>
                  </>
                )}
              />
              {errors.maxResults && (
                <p className="text-xs text-destructive">{errors.maxResults.message}</p>
              )}
            </div>

            <div className="space-y-2">
              <Label htmlFor="minRating">Minimum rating</Label>
              <Controller
                control={control}
                name="minRating"
                render={({ field }) => (
                  <Input
                    id="minRating"
                    type="number"
                    min={0}
                    max={5}
                    step={0.1}
                    placeholder="Any"
                    disabled={disabled}
                    value={field.value ?? ''}
                    onChange={(event) =>
                      field.onChange(event.target.value === '' ? undefined : Number(event.target.value))
                    }
                  />
                )}
              />
            </div>

            <div className="space-y-2">
              <Label htmlFor="minReviews">Minimum reviews</Label>
              <Controller
                control={control}
                name="minReviews"
                render={({ field }) => (
                  <Input
                    id="minReviews"
                    type="number"
                    min={0}
                    step={1}
                    placeholder="Any"
                    disabled={disabled}
                    value={field.value ?? ''}
                    onChange={(event) =>
                      field.onChange(event.target.value === '' ? undefined : Number(event.target.value))
                    }
                  />
                )}
              />
            </div>
          </div>

          {provider === 'openstreetmap' && (
            <Alert variant="info">
              <TriangleAlert />
              <AlertDescription>
                OpenStreetMap has no ratings or review counts, so those filters will exclude every
                result. Use them with Google Places.
              </AlertDescription>
            </Alert>
          )}

          <Separator />

          <div className="space-y-4">
            <Controller
              control={control}
              name="enrichContacts"
              render={({ field }) => (
                <label className="flex cursor-pointer items-start justify-between gap-4">
                  <span className="space-y-0.5">
                    <span className="block text-sm font-medium">Enrich contacts from websites</span>
                    <span className="block text-xs text-muted-foreground">
                      Visits each business website to collect publicly listed emails, phone numbers
                      and social profiles. Honours robots.txt. Slower, but far more useful data.
                    </span>
                  </span>
                  <Switch
                    checked={field.value}
                    onCheckedChange={field.onChange}
                    disabled={disabled}
                    aria-label="Enrich contacts from websites"
                  />
                </label>
              )}
            />

            <Controller
              control={control}
              name="skipDuplicates"
              render={({ field }) => (
                <label className="flex cursor-pointer items-start justify-between gap-4">
                  <span className="space-y-0.5">
                    <span className="block text-sm font-medium">Skip duplicates</span>
                    <span className="block text-xs text-muted-foreground">
                      Drops businesses already in your database, matched on website, phone, or
                      name-within-city.
                    </span>
                  </span>
                  <Switch
                    checked={field.value}
                    onCheckedChange={field.onChange}
                    disabled={disabled}
                    aria-label="Skip duplicates"
                  />
                </label>
              )}
            />
          </div>
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle>Enrichment (Apify)</CardTitle>
          <CardDescription>
            Optional, deeper enrichment for each lead — a billed Apify event, so leave it off unless
            you need the extra detail. (LinkedIn enrichment moved to its own page — see LinkedIn
            Enrichment in the nav — since it runs against leads you already have, not just new ones
            from this search.)
          </CardDescription>
        </CardHeader>
        <CardContent className="space-y-4">
          <Controller
            control={control}
            name="enrichGoogleMaps"
            render={({ field }) => (
              <label className="flex cursor-pointer items-start justify-between gap-4">
                <span className="space-y-0.5">
                  <span className="block text-sm font-medium">Enrich with Google Maps detail</span>
                  <span className="block text-xs text-muted-foreground">
                    Opening hours, postal code, images and a fresher rating/review count — detail
                    Google Places&apos; text search does not return.
                  </span>
                </span>
                <Switch
                  checked={field.value}
                  onCheckedChange={field.onChange}
                  disabled={disabled}
                  aria-label="Enrich with Google Maps detail"
                />
              </label>
            )}
          />
        </CardContent>
      </Card>

      {activeProvider && !activeProvider.ready && (
        <Alert variant="warning">
          <TriangleAlert />
          <AlertDescription>{activeProvider.reason}</AlertDescription>
        </Alert>
      )}

      <div className="flex flex-wrap items-center gap-2">
        <Button type="submit" size="lg" loading={submitting} disabled={disabled}>
          <Play />
          Start search
        </Button>
        <Button
          type="button"
          variant="ghost"
          onClick={() => reset(defaults)}
          disabled={disabled || submitting}
        >
          <RefreshCw />
          Reset
        </Button>
      </div>
    </form>
  );
}
