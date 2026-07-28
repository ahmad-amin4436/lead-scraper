/**
 * Core business/lead domain model.
 *
 * The field order of `BUSINESS_COLUMNS` is the physical column order of the
 * `Businesses.xlsx` worksheet. Changing it changes the sheet layout, so the
 * repository re-reads existing sheets by header name rather than by index.
 */

export const BUSINESS_SOURCES = ['google-places', 'openstreetmap', 'manual'] as const;
export type BusinessSource = (typeof BUSINESS_SOURCES)[number];

export const BUSINESS_STATUSES = [
  'new',
  'enriched',
  'no-website',
  'enrichment-failed',
  'partial',
] as const;
export type BusinessStatus = (typeof BUSINESS_STATUSES)[number];

export interface BusinessContact {
  email: string | null;
  /** Additional addresses beyond the primary `email`, deduplicated. */
  additionalEmails: string[];
  whatsapp: string | null;
  facebook: string | null;
  instagram: string | null;
  linkedin: string | null;
  twitter: string | null;
  youtube: string | null;
  contactFormUrl: string | null;
  /** Phone numbers discovered on the website (distinct from the provider phone). */
  websitePhones: string[];
}

export interface BusinessRecord {
  id: string;
  name: string;
  category: string;
  country: string;
  state: string;
  city: string;
  address: string;
  phone: string;
  website: string;
  email: string;
  whatsapp: string;
  facebook: string;
  instagram: string;
  linkedin: string;
  latitude: number | null;
  longitude: number | null;
  rating: number | null;
  reviewCount: number | null;
  mapsUrl: string;
  source: BusinessSource;
  dateAdded: string;
  status: BusinessStatus;
  notes: string;
}

/** Column header -> record key mapping used by the Excel repository. */
export const BUSINESS_COLUMNS = [
  { header: 'ID', key: 'id', width: 22 },
  { header: 'Business Name', key: 'name', width: 34 },
  { header: 'Category', key: 'category', width: 20 },
  { header: 'Country', key: 'country', width: 16 },
  { header: 'State', key: 'state', width: 18 },
  { header: 'City', key: 'city', width: 18 },
  { header: 'Address', key: 'address', width: 42 },
  { header: 'Phone', key: 'phone', width: 20 },
  { header: 'Website', key: 'website', width: 32 },
  { header: 'Email', key: 'email', width: 30 },
  { header: 'WhatsApp', key: 'whatsapp', width: 26 },
  { header: 'Facebook', key: 'facebook', width: 30 },
  { header: 'Instagram', key: 'instagram', width: 30 },
  { header: 'LinkedIn', key: 'linkedin', width: 30 },
  { header: 'Latitude', key: 'latitude', width: 12 },
  { header: 'Longitude', key: 'longitude', width: 12 },
  { header: 'Google Rating', key: 'rating', width: 13 },
  { header: 'Review Count', key: 'reviewCount', width: 13 },
  { header: 'Maps URL', key: 'mapsUrl', width: 34 },
  { header: 'Source', key: 'source', width: 16 },
  { header: 'Date Added', key: 'dateAdded', width: 22 },
  { header: 'Status', key: 'status', width: 18 },
  { header: 'Notes', key: 'notes', width: 40 },
] as const satisfies readonly {
  header: string;
  key: keyof BusinessRecord;
  width: number;
}[];

export type BusinessColumnKey = (typeof BUSINESS_COLUMNS)[number]['key'];

export interface BusinessQuery {
  search?: string;
  category?: string;
  country?: string;
  city?: string;
  source?: BusinessSource;
  status?: BusinessStatus;
  minRating?: number;
  minReviews?: number;
  hasEmail?: boolean;
  hasPhone?: boolean;
  hasWebsite?: boolean;
  sortBy?: BusinessColumnKey;
  sortDir?: 'asc' | 'desc';
  page?: number;
  pageSize?: number;
}

export interface BusinessPage {
  rows: BusinessRecord[];
  total: number;
  page: number;
  pageSize: number;
  pageCount: number;
}

export interface BusinessStats {
  total: number;
  withEmail: number;
  withPhone: number;
  withWebsite: number;
  withSocial: number;
  enriched: number;
  averageRating: number | null;
  byCategory: { label: string; count: number }[];
  byCountry: { label: string; count: number }[];
  bySource: { label: string; count: number }[];
  addedLast7Days: { date: string; count: number }[];
}
