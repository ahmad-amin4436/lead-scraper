import type { BusinessRecord, BusinessSource } from './business';

/** A single geographic target resolved to coordinates. */
export interface ResolvedLocation {
  label: string;
  country: string;
  state: string;
  city: string;
  latitude: number;
  longitude: number;
}

export interface SearchRequest {
  categories: string[];
  country: string;
  state?: string;
  cities: string[];
  radiusMeters: number;
  maxResults: number;
  enrichContacts: boolean;
  skipDuplicates: boolean;
  /**
   * Which quality of lead to keep. Applied while the run is in flight, so the
   * database is not filled with rows the user already said they don't want.
   */
  leadKind?:
    | 'Any'
    | 'New'
    | 'Enriched'
    | 'NoWebsite'
    | 'Partial'
    | 'WhatsAppOnly'
    | 'EmailOnly'
    | 'DeliverableEmail';
  minRating?: number;
  minReviews?: number;
  provider?: BusinessSource;
}

/** Normalised provider output, before it becomes a `BusinessRecord`. */
export interface ProviderBusiness {
  externalId: string;
  name: string;
  category: string;
  address: string;
  country: string;
  state: string;
  city: string;
  phone: string;
  website: string;
  latitude: number | null;
  longitude: number | null;
  rating: number | null;
  reviewCount: number | null;
  mapsUrl: string;
  source: BusinessSource;
}

export interface ProviderQuery {
  category: string;
  /** Human-readable category label, used to build text queries. */
  categoryLabel: string;
  location: ResolvedLocation;
  radiusMeters: number;
  maxResults: number;
}

export interface SearchHistoryEntry {
  id: string;
  jobId: string;
  request: SearchRequest;
  status: 'completed' | 'failed' | 'stopped';
  startedAt: string;
  finishedAt: string;
  elapsedMs: number;
  found: number;
  saved: number;
  duplicates: number;
  enriched: number;
  failed: number;
  error: string | null;
}

export interface LiveResult extends BusinessRecord {
  /** Set when the record was skipped because it already exists. */
  duplicate: boolean;
}

export interface SearchHistoryPage {
  items: SearchHistoryEntry[];
  total: number;
  page: number;
  pageSize: number;
  pageCount: number;
}
