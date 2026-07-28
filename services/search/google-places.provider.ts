import 'server-only';

import { ProviderError } from '@/lib/errors';
import type { ProviderBusiness, ProviderQuery, ResolvedLocation } from '@/types/search';
import type { AppSettings } from '@/types/settings';
import { withRetry } from '@/utils/async';
import { normalizeUrl } from '@/utils/normalize';
import { createHttpClient, describeHttpError, isRetryableError } from '../http/http-client';
import type { GeocodingProvider, ProviderContext, SearchProvider } from './provider';

const SEARCH_TEXT_URL = 'https://places.googleapis.com/v1/places:searchText';
const GEOCODE_URL = 'https://maps.googleapis.com/maps/api/geocode/json';

/** Google caps a Text Search page at 20 results and paginates up to 3 pages. */
const PAGE_SIZE = 20;
const MAX_PAGES = 3;

const FIELD_MASK = [
  'places.id',
  'places.displayName',
  'places.formattedAddress',
  'places.addressComponents',
  'places.location',
  'places.rating',
  'places.userRatingCount',
  'places.nationalPhoneNumber',
  'places.internationalPhoneNumber',
  'places.websiteUri',
  'places.googleMapsUri',
  'places.primaryTypeDisplayName',
  'places.businessStatus',
  'nextPageToken',
].join(',');

interface GoogleAddressComponent {
  longText?: string;
  shortText?: string;
  types?: string[];
}

interface GooglePlace {
  id?: string;
  displayName?: { text?: string };
  formattedAddress?: string;
  addressComponents?: GoogleAddressComponent[];
  location?: { latitude?: number; longitude?: number };
  rating?: number;
  userRatingCount?: number;
  nationalPhoneNumber?: string;
  internationalPhoneNumber?: string;
  websiteUri?: string;
  googleMapsUri?: string;
  primaryTypeDisplayName?: { text?: string };
  businessStatus?: string;
}

interface SearchTextResponse {
  places?: GooglePlace[];
  nextPageToken?: string;
  error?: { message?: string; status?: string };
}

interface GeocodeResponse {
  status: string;
  error_message?: string;
  results?: {
    formatted_address?: string;
    geometry?: { location?: { lat?: number; lng?: number } };
    address_components?: { long_name?: string; short_name?: string; types?: string[] }[];
  }[];
}

function componentValue(
  components: GoogleAddressComponent[] | undefined,
  type: string,
  useShort = false,
): string {
  const match = components?.find((c) => c.types?.includes(type));
  if (!match) return '';
  return (useShort ? match.shortText : match.longText) ?? match.longText ?? '';
}

export class GooglePlacesProvider implements SearchProvider, GeocodingProvider {
  readonly id = 'google-places' as const;
  readonly label = 'Google Places';

  readiness(settings: AppSettings): string | null {
    return settings.googleApiKey
      ? null
      : 'Google Places needs an API key. Add one in Settings or set GOOGLE_PLACES_API_KEY.';
  }

  async search(query: ProviderQuery, context: ProviderContext): Promise<ProviderBusiness[]> {
    const { settings } = context;
    const notReady = this.readiness(settings);
    if (notReady) throw new ProviderError(notReady, 'missing_api_key');

    const client = createHttpClient({ timeoutMs: settings.requestTimeoutMs });
    const results: ProviderBusiness[] = [];
    const seen = new Set<string>();
    let pageToken: string | undefined;

    for (let page = 0; page < MAX_PAGES && results.length < query.maxResults; page += 1) {
      context.signal.throwIfAborted();
      await context.rateLimiter.acquire(context.signal);

      const body: Record<string, unknown> = {
        textQuery: `${query.categoryLabel} in ${query.location.city}, ${query.location.country}`,
        maxResultCount: Math.min(PAGE_SIZE, query.maxResults - results.length),
        locationBias: {
          circle: {
            center: {
              latitude: query.location.latitude,
              longitude: query.location.longitude,
            },
            radius: Math.min(50_000, Math.max(1, query.radiusMeters)),
          },
        },
      };
      if (pageToken) body.pageToken = pageToken;

      const response = await withRetry(
        async () =>
          client.post<SearchTextResponse>(SEARCH_TEXT_URL, body, {
            signal: context.signal,
            headers: {
              'Content-Type': 'application/json',
              'X-Goog-Api-Key': settings.googleApiKey,
              'X-Goog-FieldMask': FIELD_MASK,
            },
          }),
        {
          attempts: settings.retryAttempts,
          signal: context.signal,
          shouldRetry: isRetryableError,
        },
      ).catch((error: unknown) => {
        throw new ProviderError(`Google Places request failed: ${describeHttpError(error)}`);
      });

      if (response.status === 401 || response.status === 403) {
        throw new ProviderError(
          'Google Places rejected the API key. Check that the key is valid and the Places API (New) is enabled.',
          'invalid_api_key',
        );
      }

      if (response.status >= 400) {
        const message = response.data?.error?.message ?? `HTTP ${response.status}`;
        throw new ProviderError(`Google Places error: ${message}`);
      }

      const places = response.data.places ?? [];
      for (const place of places) {
        if (results.length >= query.maxResults) break;
        // `businessStatus` marks permanently closed venues; they're not leads.
        if (place.businessStatus && place.businessStatus !== 'OPERATIONAL') continue;

        const externalId = place.id ?? '';
        if (externalId && seen.has(externalId)) continue;
        if (externalId) seen.add(externalId);

        results.push(this.toBusiness(place, query));
      }

      pageToken = response.data.nextPageToken;
      if (!pageToken || places.length === 0) break;
    }

    return results;
  }

  private toBusiness(place: GooglePlace, query: ProviderQuery): ProviderBusiness {
    const components = place.addressComponents;
    const city =
      componentValue(components, 'locality') ||
      componentValue(components, 'postal_town') ||
      componentValue(components, 'administrative_area_level_2') ||
      query.location.city;

    return {
      externalId: place.id ?? '',
      name: place.displayName?.text?.trim() ?? '',
      category: query.categoryLabel,
      address: place.formattedAddress?.trim() ?? '',
      country: componentValue(components, 'country', true) || query.location.country,
      state: componentValue(components, 'administrative_area_level_1') || query.location.state,
      city,
      phone: (place.internationalPhoneNumber ?? place.nationalPhoneNumber ?? '').trim(),
      website: normalizeUrl(place.websiteUri ?? ''),
      latitude: place.location?.latitude ?? null,
      longitude: place.location?.longitude ?? null,
      rating: typeof place.rating === 'number' ? place.rating : null,
      reviewCount: typeof place.userRatingCount === 'number' ? place.userRatingCount : null,
      mapsUrl: place.googleMapsUri ?? '',
      source: 'google-places',
    };
  }

  async resolve(
    query: { country: string; state?: string; city: string },
    context: ProviderContext,
  ): Promise<ResolvedLocation | null> {
    const { settings } = context;
    if (!settings.googleApiKey) return null;

    const client = createHttpClient({ timeoutMs: settings.requestTimeoutMs });
    const address = [query.city, query.state, query.country].filter(Boolean).join(', ');

    await context.rateLimiter.acquire(context.signal);

    const response = await withRetry(
      async () =>
        client.get<GeocodeResponse>(GEOCODE_URL, {
          signal: context.signal,
          params: { address, key: settings.googleApiKey },
        }),
      { attempts: settings.retryAttempts, signal: context.signal, shouldRetry: isRetryableError },
    ).catch((error: unknown) => {
      throw new ProviderError(`Geocoding failed: ${describeHttpError(error)}`);
    });

    const first = response.data.results?.[0];
    const location = first?.geometry?.location;
    if (!first || typeof location?.lat !== 'number' || typeof location?.lng !== 'number') {
      return null;
    }

    const components = first.address_components ?? [];
    const pick = (type: string, short = false): string => {
      const match = components.find((c) => c.types?.includes(type));
      return (short ? match?.short_name : match?.long_name) ?? '';
    };

    return {
      label: first.formatted_address ?? address,
      country: pick('country', true) || query.country,
      state: pick('administrative_area_level_1') || query.state || '',
      city: pick('locality') || pick('postal_town') || query.city,
      latitude: location.lat,
      longitude: location.lng,
    };
  }
}

export const googlePlacesProvider = new GooglePlacesProvider();
