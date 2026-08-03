import { City, Country, State } from 'country-state-city';

import type { ComboboxOption } from '@/components/ui/combobox';

/**
 * Country / state / city data backed by the ISO 3166 dataset.
 *
 * This replaces the hand-curated list, which only covered ~20 countries and left
 * everywhere else with no regions at all. `country-state-city` ships the full
 * set (250 countries, every subdivision, ~150k cities), so the cascade works
 * for Maldives as readily as for the United States.
 *
 * Lookups are memoised per key: the city list for a state is a few hundred
 * entries and rebuilding it on every keystroke is wasted work.
 */

const stateCache = new Map<string, ComboboxOption[]>();
const cityCache = new Map<string, ComboboxOption[]>();

let countryOptions: ComboboxOption[] | null = null;

export function getCountryOptions(): ComboboxOption[] {
  countryOptions ??= Country.getAllCountries()
    .map((country) => ({
      value: country.isoCode,
      label: country.name,
      hint: country.isoCode,
    }))
    .sort((a, b) => a.label.localeCompare(b.label));

  return countryOptions;
}

/** States/provinces for a country. Empty for city-states with no subdivisions. */
export function getStateOptions(countryCode: string | null): ComboboxOption[] {
  if (!countryCode) return [];

  const cached = stateCache.get(countryCode);
  if (cached) return cached;

  const options = State.getStatesOfCountry(countryCode)
    .map((state) => ({ value: state.isoCode, label: state.name }))
    .sort((a, b) => a.label.localeCompare(b.label));

  stateCache.set(countryCode, options);
  return options;
}

/**
 * Cities for a state, or for the whole country when no state is chosen.
 *
 * The country-wide list can run to thousands, so each entry carries its state as
 * a hint — otherwise duplicate city names across regions would be ambiguous.
 */
export function getCityOptions(
  countryCode: string | null,
  stateCode: string | null,
): ComboboxOption[] {
  if (!countryCode) return [];

  const key = `${countryCode}:${stateCode ?? '*'}`;
  const cached = cityCache.get(key);
  if (cached) return cached;

  const raw = stateCode
    ? City.getCitiesOfState(countryCode, stateCode)
    : City.getCitiesOfCountry(countryCode) ?? [];

  const stateNames = new Map(
    State.getStatesOfCountry(countryCode).map((s) => [s.isoCode, s.name]),
  );

  // The dataset repeats names across regions; dedupe within the chosen scope.
  const seen = new Set<string>();
  const options: ComboboxOption[] = [];

  for (const city of raw) {
    const label = city.name;
    const hint = stateCode ? undefined : stateNames.get(city.stateCode);
    const dedupeKey = `${label}|${hint ?? ''}`;

    if (seen.has(dedupeKey)) continue;
    seen.add(dedupeKey);

    options.push({ value: label, label, hint });
  }

  options.sort((a, b) => a.label.localeCompare(b.label));
  cityCache.set(key, options);
  return options;
}

export function getCountryName(countryCode: string | null): string {
  if (!countryCode) return '';
  return Country.getCountryByCode(countryCode)?.name ?? countryCode;
}

export function getStateName(countryCode: string | null, stateCode: string | null): string {
  if (!countryCode || !stateCode) return '';
  return State.getStateByCodeAndCountry(stateCode, countryCode)?.name ?? stateCode;
}

export function isValidCountryCode(code: string): boolean {
  return Boolean(Country.getCountryByCode(code));
}

/** Radius presets offered as quick-fill chips beside the free-text input. */
export const RADIUS_PRESETS = [1000, 2000, 5000, 10000, 25000, 50000] as const;

export const MAX_RESULT_PRESETS = [20, 40, 60, 100, 150, 200] as const;
