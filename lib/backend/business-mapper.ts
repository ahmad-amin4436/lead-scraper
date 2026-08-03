// Translation between the .NET business/lead DTOs and the shapes the UI
// already speaks — the same role search-mapper.ts plays for search jobs.
//
// The .NET API (SQL Server) is the source of truth for leads. This module
// exists so that stays true without every component learning .NET's
// PascalCase enum names.

import type {
  BusinessPage,
  BusinessQuery,
  BusinessRecord,
  BusinessStats,
} from '@/types/business';
import type {
  BackendBusiness,
  BackendBusinessStats,
  BackendEmailStatus,
  BackendPagedResult,
  BackendSource,
  BackendStatus,
  BackendWhatsAppStatus,
} from './types';

const SOURCE_MAP: Record<BackendSource, BusinessRecord['source']> = {
  Manual: 'manual',
  GooglePlaces: 'google-places',
  OpenStreetMap: 'openstreetmap',
};

const STATUS_MAP: Record<BackendStatus, BusinessRecord['status']> = {
  New: 'new',
  Enriched: 'enriched',
  Partial: 'partial',
  NoWebsite: 'no-website',
  EnrichmentFailed: 'enrichment-failed',
};

const EMAIL_STATUS_MAP: Record<BackendEmailStatus, BusinessRecord['emailStatus']> = {
  Unverified: 'unverified',
  Valid: 'valid',
  Risky: 'risky',
  Invalid: 'invalid',
  Unknown: 'unknown',
};

const WHATSAPP_STATUS_MAP: Record<BackendWhatsAppStatus, BusinessRecord['whatsappStatus']> = {
  Unverified: 'unverified',
  Confirmed: 'confirmed',
  Likely: 'likely',
  Unlikely: 'unlikely',
  None: 'none',
};

// Reverse of the maps above — the query and update endpoints take the
// frontend's lowercase-hyphen enum values but the backend expects PascalCase.
export const SOURCE_TO_BACKEND = invert(SOURCE_MAP);
export const STATUS_TO_BACKEND = invert(STATUS_MAP);
export const EMAIL_STATUS_TO_BACKEND = invert(EMAIL_STATUS_MAP);
export const WHATSAPP_STATUS_TO_BACKEND = invert(WHATSAPP_STATUS_MAP);

function invert<K extends string, V extends string>(map: Record<K, V>): Record<V, K> {
  const result = {} as Record<V, K>;
  for (const key in map) result[map[key]] = key;
  return result;
}

export function toBusinessRecord(business: BackendBusiness): BusinessRecord {
  return {
    id: business.id,
    name: business.name,
    category: business.category,
    country: business.country,
    state: business.state,
    city: business.city,
    address: business.address,
    phone: business.phone,
    website: business.website,
    email: business.email,
    whatsapp: business.whatsApp,
    facebook: business.facebook,
    instagram: business.instagram,
    linkedin: business.linkedIn,
    latitude: business.latitude,
    longitude: business.longitude,
    rating: business.rating,
    reviewCount: business.reviewCount,
    mapsUrl: business.mapsUrl,
    source: SOURCE_MAP[business.source] ?? 'manual',
    dateAdded: business.createdAt,
    status: STATUS_MAP[business.status] ?? 'new',
    emailStatus: EMAIL_STATUS_MAP[business.emailStatus] ?? 'unverified',
    whatsappStatus: WHATSAPP_STATUS_MAP[business.whatsAppStatus] ?? 'unverified',
    notes: business.notes,
  };
}

export function toBusinessPage(page: BackendPagedResult<BackendBusiness>): BusinessPage {
  return {
    rows: page.items.map(toBusinessRecord),
    total: page.total,
    page: page.page,
    pageSize: page.pageSize,
    pageCount: page.pageCount,
  };
}

export function toBusinessStats(stats: BackendBusinessStats): BusinessStats {
  return {
    total: stats.total,
    withEmail: stats.withEmail,
    withPhone: stats.withPhone,
    withWebsite: stats.withWebsite,
    withSocial: stats.withSocial,
    enriched: stats.enriched,
    verifiedEmails: stats.verifiedEmails,
    whatsappReachable: stats.whatsAppReachable,
    averageRating: stats.averageRating,
    byCategory: stats.byCategory,
    byCountry: stats.byCountry,
    bySource: stats.bySource,
    addedLast7Days: stats.addedLast7Days,
  };
}

/**
 * Builds the query string for the backend's `GET/DELETE /api/businesses`
 * endpoints from the frontend's filter shape — the one place that translates
 * lowercase-hyphen enum values to the PascalCase names .NET expects.
 */
export function toBackendQuery(query: BusinessQuery): Record<string, string | number | boolean> {
  const params: Record<string, string | number | boolean> = {};

  if (query.search) params.search = query.search;
  if (query.category) params.category = query.category;
  if (query.country) params.country = query.country;
  if (query.city) params.city = query.city;
  if (query.source) params.source = SOURCE_TO_BACKEND[query.source];
  if (query.status) params.status = STATUS_TO_BACKEND[query.status];
  if (query.emailStatus) params.emailStatus = EMAIL_STATUS_TO_BACKEND[query.emailStatus];
  if (query.whatsappStatus) params.whatsAppStatus = WHATSAPP_STATUS_TO_BACKEND[query.whatsappStatus];
  if (query.minRating !== undefined) params.minRating = query.minRating;
  if (query.minReviews !== undefined) params.minReviews = query.minReviews;
  if (query.hasEmail !== undefined) params.hasEmail = query.hasEmail;
  if (query.hasPhone !== undefined) params.hasPhone = query.hasPhone;
  if (query.hasWebsite !== undefined) params.hasWebsite = query.hasWebsite;
  if (query.sortBy) params.sortBy = query.sortBy;
  if (query.sortDir) params.sortDir = query.sortDir;
  if (query.page !== undefined) params.page = query.page;
  if (query.pageSize !== undefined) params.pageSize = query.pageSize;

  return params;
}
