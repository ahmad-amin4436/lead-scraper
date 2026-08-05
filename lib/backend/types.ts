// TypeScript mirrors of the .NET API DTOs (LeadMine.Application/DTOs).
//
// The backend serializes enums as their PascalCase names (JsonStringEnumConverter),
// so these are string unions rather than numbers.

// --- enums -----------------------------------------------------------------

export type BackendSource = 'Manual' | 'GooglePlaces' | 'OpenStreetMap' | 'GoogleMapsBrowser';

export type BackendStatus = 'New' | 'Enriched' | 'Partial' | 'NoWebsite' | 'EnrichmentFailed';

export type BackendEmailStatus = 'Unverified' | 'Valid' | 'Risky' | 'Invalid' | 'Unknown';

export type BackendWhatsAppStatus = 'Unverified' | 'Confirmed' | 'Likely' | 'Unlikely' | 'None';

// --- auth ------------------------------------------------------------------

export interface BackendUser {
  id: string;
  email: string;
  firstName: string;
  lastName: string;
  fullName: string;
  isActive: boolean;
  emailConfirmed: boolean;
  createdAt: string;
  lastLoginAt: string | null;
  roles: string[];
  permissions: string[];
}

export interface BackendAuthResponse {
  accessToken: string;
  refreshToken: string;
  expiresAt: string;
  tokenType: string;
  user: BackendUser;
}

export interface BackendPagedResult<T> {
  items: T[];
  total: number;
  page: number;
  pageSize: number;
  pageCount: number;
}

// --- roles & permissions ----------------------------------------------------

export interface BackendRole {
  id: string;
  name: string;
  description: string;
  isSystemRole: boolean;
  userCount: number;
  permissions: string[];
}

export interface BackendPermission {
  name: string;
  group: string;
  description: string;
}

// --- leads -----------------------------------------------------------------

export interface BackendBusiness {
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
  emailStatus: BackendEmailStatus;
  whatsApp: string;
  whatsAppStatus: BackendWhatsAppStatus;
  facebook: string;
  instagram: string;
  linkedIn: string;
  latitude: number | null;
  longitude: number | null;
  rating: number | null;
  reviewCount: number | null;
  mapsUrl: string;
  source: BackendSource;
  status: BackendStatus;
  notes: string;
  createdAt: string;

  // Google Maps enrichment (browser)
  postalCode: string;
  openingHoursJson: string;
  placeId: string;
  imageUrlsJson: string;
  permanentlyClosed: boolean | null;
  lastMapsEnrichedAt: string | null;

  // LinkedIn company enrichment (browser)
  industry: string;
  employeeCount: number | null;
  companyDescription: string;
  lastLinkedInEnrichedAt: string | null;
}

// --- people ------------------------------------------------------------------

export interface BackendPerson {
  id: string;
  businessId: string | null;
  companyName: string;
  fullName: string;
  jobTitle: string;
  headline: string;
  linkedInUrl: string;
  location: string;
  experienceJson: string;
  educationJson: string;
  skillsJson: string;
  email: string;
  phone: string;
  isDecisionMaker: boolean;
  decisionMakerRole: string;
  createdAt: string;
}

export interface BackendBusinessStats {
  total: number;
  withEmail: number;
  withPhone: number;
  withWebsite: number;
  withSocial: number;
  enriched: number;
  verifiedEmails: number;
  whatsAppReachable: number;
  averageRating: number | null;
  byCategory: { label: string; count: number }[];
  byCountry: { label: string; count: number }[];
  bySource: { label: string; count: number }[];
  addedLast7Days: { date: string; count: number }[];
}

// --- errors ----------------------------------------------------------------

/** RFC 7807 problem details body, as returned by the .NET API. */
export interface BackendProblem {
  title?: string;
  detail?: string;
  status?: number;
  /** Field-level messages from model validation. */
  errors?: Record<string, string[]>;
}
