'use client';

import * as React from 'react';
import { MapPin, Plus, X } from 'lucide-react';

import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Combobox } from '@/components/ui/combobox';
import { getCityOptions } from '@/lib/constants/geo';

interface CityPickerProps {
  countryCode: string | null;
  stateCode: string | null;
  value: string[];
  onChange: (next: string[]) => void;
  max?: number;
  disabled?: boolean;
}

/**
 * Adds cities from the ISO dataset for the chosen country/region.
 *
 * The list narrows as the user picks a region, but a free-text fallback stays
 * available: the dataset is comprehensive rather than complete, and a missing
 * town should never block a search — anything typed is geocoded server-side.
 */
export function CityPicker({
  countryCode,
  stateCode,
  value,
  onChange,
  max = 25,
  disabled,
}: CityPickerProps) {
  const [draft, setDraft] = React.useState('');

  const options = React.useMemo(() => {
    const all = getCityOptions(countryCode, stateCode);
    const chosen = new Set(value.map((city) => city.toLowerCase()));
    return all.filter((option) => !chosen.has(option.label.toLowerCase()));
  }, [countryCode, stateCode, value]);

  const add = (city: string): void => {
    const trimmed = city.trim();
    if (!trimmed || value.length >= max) return;
    if (value.some((existing) => existing.toLowerCase() === trimmed.toLowerCase())) return;

    onChange([...value, trimmed]);
    setDraft('');
  };

  const remove = (city: string): void => {
    onChange(value.filter((item) => item !== city));
  };

  const atCapacity = value.length >= max;

  return (
    <div className="space-y-3">
      <div className="grid gap-2 sm:grid-cols-[minmax(0,1fr)_minmax(0,1fr)]">
        <Combobox
          options={options}
          value={null}
          onChange={(next) => next && add(next)}
          placeholder={countryCode ? 'Pick a city from the list' : 'Choose a country first'}
          searchPlaceholder="Search cities…"
          emptyMessage="No matching city — type it below instead."
          disabled={disabled || !countryCode || atCapacity}
          aria-label="Pick a city"
        />

        <div className="flex gap-2">
          <input
            value={draft}
            onChange={(event) => setDraft(event.target.value)}
            onKeyDown={(event) => {
              // Enter adds without submitting the surrounding form.
              if (event.key === 'Enter') {
                event.preventDefault();
                add(draft);
              }
            }}
            placeholder="…or type any city"
            disabled={disabled || atCapacity}
            aria-label="Type a city"
            className="flex h-9 w-full rounded-md border border-input bg-background px-3 py-1 text-sm shadow-sm placeholder:text-muted-foreground focus-visible:border-ring focus-visible:ring-[3px] focus-visible:ring-ring/25 focus-visible:outline-none disabled:cursor-not-allowed disabled:opacity-50"
          />
          <Button
            type="button"
            variant="outline"
            size="icon"
            onClick={() => add(draft)}
            disabled={disabled || !draft.trim() || atCapacity}
            aria-label="Add city"
          >
            <Plus />
          </Button>
        </div>
      </div>

      {value.length > 0 && (
        <div className="flex flex-wrap gap-1.5">
          {value.map((city) => (
            <Badge key={city} variant="secondary" className="pr-1">
              <MapPin className="size-3" />
              {city}
              <button
                type="button"
                onClick={() => remove(city)}
                disabled={disabled}
                className="ml-0.5 rounded-full p-0.5 hover:bg-foreground/10"
                aria-label={`Remove ${city}`}
              >
                <X className="size-3" />
              </button>
            </Badge>
          ))}
        </div>
      )}

      <p className="text-xs text-muted-foreground">
        {value.length} / {max} cities
        {options.length > 0 && ` · ${options.length.toLocaleString()} available here`}
      </p>
    </div>
  );
}
