'use client';

import * as React from 'react';
import { useWatch, type Control } from 'react-hook-form';

import { useDebouncedValue } from '@/hooks/use-debounced-value';
import { searchFormFiltersSchema, type SearchFormFilters, type SearchRequestInput } from '@/lib/validation/search.schema';

const STORAGE_KEY = 'leadmine:search-filters';

/**
 * Reads the last-applied search filters from this browser, if any.
 *
 * Deliberately client-only (`localStorage`, no API call): this is a per-device
 * convenience, not account data, so it survives page reloads without needing
 * a backend round trip or showing up on another machine.
 */
export function loadPersistedSearchFilters(): SearchFormFilters | null {
  if (typeof window === 'undefined') return null;

  try {
    const raw = window.localStorage.getItem(STORAGE_KEY);
    if (!raw) return null;

    const parsed = searchFormFiltersSchema.safeParse(JSON.parse(raw));
    return parsed.success ? parsed.data : null;
  } catch {
    // Corrupt or pre-schema-change JSON — treat as "nothing saved" rather
    // than crash the search page over a stale localStorage value.
    return null;
  }
}

/**
 * Saves the form's values to `localStorage` as the user edits them, so the
 * next visit to Search Businesses reopens with the same filters applied.
 * Debounced so typing in a text field doesn't hit storage on every keystroke.
 */
export function usePersistSearchFilters(control: Control<SearchRequestInput>): void {
  const values = useWatch({ control });
  const debouncedValues = useDebouncedValue(values, 500);

  React.useEffect(() => {
    if (!debouncedValues) return;

    try {
      window.localStorage.setItem(STORAGE_KEY, JSON.stringify(debouncedValues));
    } catch {
      // Private browsing / quota exceeded — filters just won't persist this time.
    }
  }, [debouncedValues]);
}
