'use client';

import * as React from 'react';
import { MapPin, Plus, X } from 'lucide-react';

import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { getCities } from '@/lib/constants/locations';

interface CityPickerProps {
  countryCode: string;
  regionName?: string;
  value: string[];
  onChange: (next: string[]) => void;
  max?: number;
  disabled?: boolean;
}

/**
 * Cities can be picked from the curated list for the selected country/region or
 * typed freely — anything entered is geocoded server-side, so unlisted towns
 * work just as well.
 */
export function CityPicker({
  countryCode,
  regionName,
  value,
  onChange,
  max = 25,
  disabled,
}: CityPickerProps) {
  const [draft, setDraft] = React.useState('');

  const suggestions = React.useMemo(() => {
    const all = getCities(countryCode, regionName);
    const chosen = new Set(value.map((city) => city.toLowerCase()));
    return all.filter((city) => !chosen.has(city.toLowerCase())).slice(0, 14);
  }, [countryCode, regionName, value]);

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

  return (
    <div className="space-y-3">
      <div className="flex gap-2">
        <Input
          value={draft}
          onChange={(event) => setDraft(event.target.value)}
          onKeyDown={(event) => {
            // Enter adds a city without submitting the surrounding form.
            if (event.key === 'Enter') {
              event.preventDefault();
              add(draft);
            }
          }}
          placeholder="Type a city and press Enter"
          disabled={disabled || value.length >= max}
          aria-label="Add a city"
        />
        <Button
          type="button"
          variant="outline"
          size="icon"
          onClick={() => add(draft)}
          disabled={disabled || !draft.trim() || value.length >= max}
          aria-label="Add city"
        >
          <Plus />
        </Button>
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

      {suggestions.length > 0 && value.length < max && (
        <div className="space-y-1.5">
          <p className="text-xs text-muted-foreground">Quick add</p>
          <div className="flex flex-wrap gap-1.5">
            {suggestions.map((city) => (
              <button
                key={city}
                type="button"
                onClick={() => add(city)}
                disabled={disabled}
                className="rounded-full border border-dashed border-border px-2.5 py-0.5 text-xs text-muted-foreground transition-colors hover:border-primary hover:text-primary disabled:opacity-50"
              >
                + {city}
              </button>
            ))}
          </div>
        </div>
      )}

      <p className="text-xs text-muted-foreground">
        {value.length} / {max} cities. Any city worldwide works — it doesn&apos;t need to be in the
        list.
      </p>
    </div>
  );
}
