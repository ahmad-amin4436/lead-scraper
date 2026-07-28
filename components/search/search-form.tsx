'use client';

import * as React from 'react';
import { Controller, useForm, useWatch } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { Play, RefreshCw, TriangleAlert } from 'lucide-react';

import { CategoryPicker } from '@/components/search/category-picker';
import { CityPicker } from '@/components/search/city-picker';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
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
import { COUNTRIES, MAX_RESULT_OPTIONS, RADIUS_OPTIONS, getRegions } from '@/lib/constants/locations';
import { searchRequestSchema, type SearchRequestInput } from '@/lib/validation/search.schema';
import type { BusinessSource } from '@/types/business';
import type { SearchRequest } from '@/types/search';

const ANY_REGION = '__any__';

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

  const regions = React.useMemo(() => getRegions(country), [country]);
  const activeProvider = providers.find((item) => item.id === provider);

  // Clearing the region when the country changes keeps the city suggestions honest.
  const handleCountryChange = (next: string): void => {
    setValue('country', next, { shouldValidate: true });
    setValue('state', undefined);
    setValue('cities', []);
  };

  return (
    <form
      onSubmit={handleSubmit((values) => onSubmit(values as SearchRequest))}
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
              <Controller
                control={control}
                name="country"
                render={({ field }) => (
                  <Select value={field.value} onValueChange={handleCountryChange} disabled={disabled}>
                    <SelectTrigger id="country">
                      <SelectValue placeholder="Select a country" />
                    </SelectTrigger>
                    <SelectContent>
                      {COUNTRIES.map((item) => (
                        <SelectItem key={item.code} value={item.code}>
                          {item.name}
                        </SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                )}
              />
              {errors.country && (
                <p className="text-xs text-destructive">{errors.country.message}</p>
              )}
            </div>

            <div className="space-y-2">
              <Label htmlFor="state">State / Region</Label>
              <Controller
                control={control}
                name="state"
                render={({ field }) => (
                  <Select
                    value={field.value || ANY_REGION}
                    onValueChange={(next) => field.onChange(next === ANY_REGION ? undefined : next)}
                    disabled={disabled || regions.length === 0}
                  >
                    <SelectTrigger id="state">
                      <SelectValue placeholder={regions.length === 0 ? 'Not available' : 'Any region'} />
                    </SelectTrigger>
                    <SelectContent>
                      <SelectItem value={ANY_REGION}>Any region</SelectItem>
                      {regions.map((region) => (
                        <SelectItem key={region.name} value={region.name}>
                          {region.name}
                        </SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                )}
              />
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
                  regionName={state}
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
          <CardTitle>Limits &amp; quality</CardTitle>
          <CardDescription>
            Keep runs focused — narrower searches finish faster and burn less API quota.
          </CardDescription>
        </CardHeader>
        <CardContent className="space-y-5">
          <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
            <div className="space-y-2">
              <Label htmlFor="radius">Radius</Label>
              <Controller
                control={control}
                name="radiusMeters"
                render={({ field }) => (
                  <Select
                    value={String(field.value)}
                    onValueChange={(next) => field.onChange(Number(next))}
                    disabled={disabled}
                  >
                    <SelectTrigger id="radius">
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
              <Label htmlFor="maxResults">Max results per search</Label>
              <Controller
                control={control}
                name="maxResults"
                render={({ field }) => (
                  <Select
                    value={String(field.value)}
                    onValueChange={(next) => field.onChange(Number(next))}
                    disabled={disabled}
                  >
                    <SelectTrigger id="maxResults">
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
                      Drops businesses already in the database, matched on website, phone, or
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
