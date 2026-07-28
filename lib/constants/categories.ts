/**
 * Business category catalogue.
 *
 * Each entry carries both a Google Places text query and OpenStreetMap tag
 * filters so the same user-facing selection works against either provider.
 */

export interface BusinessCategory {
  id: string;
  label: string;
  group: string;
  /** Text fragment used to build a Google Places `textQuery`. */
  googleQuery: string;
  /** `key=value` tag filters passed to Overpass; matched with OR semantics. */
  osmFilters: string[];
}

export const CATEGORY_GROUPS = [
  'Food & Drink',
  'Retail',
  'Health & Medical',
  'Beauty & Wellness',
  'Professional Services',
  'Home Services',
  'Automotive',
  'Hospitality & Travel',
  'Education',
  'Real Estate',
  'Fitness & Sports',
  'Entertainment',
] as const;

export const BUSINESS_CATEGORIES: BusinessCategory[] = [
  // Food & Drink
  { id: 'restaurant', label: 'Restaurant', group: 'Food & Drink', googleQuery: 'restaurants', osmFilters: ['amenity=restaurant'] },
  { id: 'cafe', label: 'Cafe & Coffee Shop', group: 'Food & Drink', googleQuery: 'cafes and coffee shops', osmFilters: ['amenity=cafe'] },
  { id: 'bakery', label: 'Bakery', group: 'Food & Drink', googleQuery: 'bakeries', osmFilters: ['shop=bakery'] },
  { id: 'bar', label: 'Bar & Pub', group: 'Food & Drink', googleQuery: 'bars and pubs', osmFilters: ['amenity=bar', 'amenity=pub'] },
  { id: 'fast-food', label: 'Fast Food', group: 'Food & Drink', googleQuery: 'fast food restaurants', osmFilters: ['amenity=fast_food'] },
  { id: 'catering', label: 'Catering Service', group: 'Food & Drink', googleQuery: 'catering services', osmFilters: ['craft=caterer', 'shop=caterer'] },
  { id: 'grocery', label: 'Grocery & Supermarket', group: 'Food & Drink', googleQuery: 'supermarkets and grocery stores', osmFilters: ['shop=supermarket', 'shop=grocery', 'shop=convenience'] },
  { id: 'butcher', label: 'Butcher', group: 'Food & Drink', googleQuery: 'butcher shops', osmFilters: ['shop=butcher'] },

  // Retail
  { id: 'clothing', label: 'Clothing Store', group: 'Retail', googleQuery: 'clothing stores', osmFilters: ['shop=clothes', 'shop=boutique'] },
  { id: 'shoe-store', label: 'Shoe Store', group: 'Retail', googleQuery: 'shoe stores', osmFilters: ['shop=shoes'] },
  { id: 'jewelry', label: 'Jewelry Store', group: 'Retail', googleQuery: 'jewelry stores', osmFilters: ['shop=jewelry'] },
  { id: 'electronics', label: 'Electronics Store', group: 'Retail', googleQuery: 'electronics stores', osmFilters: ['shop=electronics', 'shop=computer', 'shop=mobile_phone'] },
  { id: 'furniture', label: 'Furniture Store', group: 'Retail', googleQuery: 'furniture stores', osmFilters: ['shop=furniture', 'shop=interior_decoration'] },
  { id: 'hardware', label: 'Hardware & DIY', group: 'Retail', googleQuery: 'hardware stores', osmFilters: ['shop=hardware', 'shop=doityourself'] },
  { id: 'florist', label: 'Florist', group: 'Retail', googleQuery: 'florists', osmFilters: ['shop=florist'] },
  { id: 'bookstore', label: 'Bookstore', group: 'Retail', googleQuery: 'bookstores', osmFilters: ['shop=books'] },
  { id: 'pet-store', label: 'Pet Store', group: 'Retail', googleQuery: 'pet stores', osmFilters: ['shop=pet'] },
  { id: 'sporting-goods', label: 'Sporting Goods', group: 'Retail', googleQuery: 'sporting goods stores', osmFilters: ['shop=sports'] },

  // Health & Medical
  { id: 'dentist', label: 'Dentist', group: 'Health & Medical', googleQuery: 'dentists', osmFilters: ['amenity=dentist', 'healthcare=dentist'] },
  { id: 'doctor', label: 'Doctor & Clinic', group: 'Health & Medical', googleQuery: 'medical clinics and doctors', osmFilters: ['amenity=doctors', 'amenity=clinic', 'healthcare=doctor'] },
  { id: 'pharmacy', label: 'Pharmacy', group: 'Health & Medical', googleQuery: 'pharmacies', osmFilters: ['amenity=pharmacy'] },
  { id: 'veterinary', label: 'Veterinary Clinic', group: 'Health & Medical', googleQuery: 'veterinary clinics', osmFilters: ['amenity=veterinary'] },
  { id: 'optician', label: 'Optician', group: 'Health & Medical', googleQuery: 'opticians and eye care', osmFilters: ['shop=optician', 'healthcare=optometrist'] },
  { id: 'physiotherapy', label: 'Physiotherapy', group: 'Health & Medical', googleQuery: 'physiotherapy clinics', osmFilters: ['healthcare=physiotherapist'] },
  { id: 'hospital', label: 'Hospital', group: 'Health & Medical', googleQuery: 'hospitals', osmFilters: ['amenity=hospital'] },
  { id: 'laboratory', label: 'Medical Laboratory', group: 'Health & Medical', googleQuery: 'medical laboratories', osmFilters: ['healthcare=laboratory'] },

  // Beauty & Wellness
  { id: 'hair-salon', label: 'Hair Salon', group: 'Beauty & Wellness', googleQuery: 'hair salons', osmFilters: ['shop=hairdresser'] },
  { id: 'beauty-salon', label: 'Beauty Salon', group: 'Beauty & Wellness', googleQuery: 'beauty salons', osmFilters: ['shop=beauty'] },
  { id: 'spa', label: 'Spa & Massage', group: 'Beauty & Wellness', googleQuery: 'spas and massage', osmFilters: ['leisure=spa', 'shop=massage'] },
  { id: 'nail-salon', label: 'Nail Salon', group: 'Beauty & Wellness', googleQuery: 'nail salons', osmFilters: ['shop=beauty'] },
  { id: 'barber', label: 'Barber Shop', group: 'Beauty & Wellness', googleQuery: 'barber shops', osmFilters: ['shop=hairdresser'] },
  { id: 'tattoo', label: 'Tattoo Studio', group: 'Beauty & Wellness', googleQuery: 'tattoo studios', osmFilters: ['shop=tattoo'] },

  // Professional Services
  { id: 'law-firm', label: 'Law Firm', group: 'Professional Services', googleQuery: 'law firms and lawyers', osmFilters: ['office=lawyer'] },
  { id: 'accountant', label: 'Accountant', group: 'Professional Services', googleQuery: 'accountants', osmFilters: ['office=accountant'] },
  { id: 'marketing-agency', label: 'Marketing Agency', group: 'Professional Services', googleQuery: 'marketing agencies', osmFilters: ['office=advertising_agency', 'office=marketing'] },
  { id: 'it-services', label: 'IT & Software Services', group: 'Professional Services', googleQuery: 'IT services and software companies', osmFilters: ['office=it', 'office=company'] },
  { id: 'insurance', label: 'Insurance Agency', group: 'Professional Services', googleQuery: 'insurance agencies', osmFilters: ['office=insurance'] },
  { id: 'consulting', label: 'Business Consulting', group: 'Professional Services', googleQuery: 'business consultants', osmFilters: ['office=consulting'] },
  { id: 'financial-advisor', label: 'Financial Advisor', group: 'Professional Services', googleQuery: 'financial advisors', osmFilters: ['office=financial', 'office=financial_advisor'] },
  { id: 'architect', label: 'Architect', group: 'Professional Services', googleQuery: 'architects', osmFilters: ['office=architect'] },
  { id: 'printing', label: 'Printing & Signage', group: 'Professional Services', googleQuery: 'printing and signage companies', osmFilters: ['shop=copyshop', 'craft=signmaker'] },
  { id: 'travel-agency', label: 'Travel Agency', group: 'Professional Services', googleQuery: 'travel agencies', osmFilters: ['shop=travel_agency'] },

  // Home Services
  { id: 'plumber', label: 'Plumber', group: 'Home Services', googleQuery: 'plumbers', osmFilters: ['craft=plumber'] },
  { id: 'electrician', label: 'Electrician', group: 'Home Services', googleQuery: 'electricians', osmFilters: ['craft=electrician'] },
  { id: 'builder', label: 'Builder & Contractor', group: 'Home Services', googleQuery: 'building contractors', osmFilters: ['craft=builder', 'office=construction_company'] },
  { id: 'painter', label: 'Painter & Decorator', group: 'Home Services', googleQuery: 'painters and decorators', osmFilters: ['craft=painter'] },
  { id: 'carpenter', label: 'Carpenter & Joiner', group: 'Home Services', googleQuery: 'carpenters and joiners', osmFilters: ['craft=carpenter'] },
  { id: 'landscaping', label: 'Landscaping & Gardening', group: 'Home Services', googleQuery: 'landscaping and gardening services', osmFilters: ['craft=gardener', 'shop=garden_centre'] },
  { id: 'cleaning', label: 'Cleaning Service', group: 'Home Services', googleQuery: 'cleaning services', osmFilters: ['shop=laundry', 'craft=cleaning'] },
  { id: 'hvac', label: 'HVAC & Heating', group: 'Home Services', googleQuery: 'HVAC and heating engineers', osmFilters: ['craft=hvac'] },
  { id: 'roofing', label: 'Roofing', group: 'Home Services', googleQuery: 'roofing contractors', osmFilters: ['craft=roofer'] },
  { id: 'locksmith', label: 'Locksmith', group: 'Home Services', googleQuery: 'locksmiths', osmFilters: ['craft=locksmith', 'shop=locksmith'] },
  { id: 'moving', label: 'Moving & Removals', group: 'Home Services', googleQuery: 'moving and removal companies', osmFilters: ['office=moving_company'] },

  // Automotive
  { id: 'car-repair', label: 'Car Repair & Garage', group: 'Automotive', googleQuery: 'car repair garages', osmFilters: ['shop=car_repair'] },
  { id: 'car-dealer', label: 'Car Dealership', group: 'Automotive', googleQuery: 'car dealerships', osmFilters: ['shop=car'] },
  { id: 'car-wash', label: 'Car Wash', group: 'Automotive', googleQuery: 'car washes', osmFilters: ['amenity=car_wash'] },
  { id: 'tyre-shop', label: 'Tyre Shop', group: 'Automotive', googleQuery: 'tyre and tire shops', osmFilters: ['shop=tyres'] },
  { id: 'car-rental', label: 'Car Rental', group: 'Automotive', googleQuery: 'car rental companies', osmFilters: ['amenity=car_rental'] },
  { id: 'driving-school', label: 'Driving School', group: 'Automotive', googleQuery: 'driving schools', osmFilters: ['amenity=driving_school'] },
  { id: 'motorcycle', label: 'Motorcycle Shop', group: 'Automotive', googleQuery: 'motorcycle dealers', osmFilters: ['shop=motorcycle'] },

  // Hospitality & Travel
  { id: 'hotel', label: 'Hotel', group: 'Hospitality & Travel', googleQuery: 'hotels', osmFilters: ['tourism=hotel'] },
  { id: 'guest-house', label: 'Guest House & B&B', group: 'Hospitality & Travel', googleQuery: 'guest houses and bed and breakfast', osmFilters: ['tourism=guest_house', 'tourism=bed_and_breakfast'] },
  { id: 'hostel', label: 'Hostel', group: 'Hospitality & Travel', googleQuery: 'hostels', osmFilters: ['tourism=hostel'] },
  { id: 'event-venue', label: 'Event Venue', group: 'Hospitality & Travel', googleQuery: 'event venues', osmFilters: ['amenity=events_venue', 'amenity=conference_centre'] },

  // Education
  { id: 'school', label: 'School', group: 'Education', googleQuery: 'schools', osmFilters: ['amenity=school'] },
  { id: 'language-school', label: 'Language School', group: 'Education', googleQuery: 'language schools', osmFilters: ['amenity=language_school'] },
  { id: 'tutoring', label: 'Tutoring Centre', group: 'Education', googleQuery: 'tutoring centres', osmFilters: ['amenity=prep_school', 'office=educational_institution'] },
  { id: 'nursery', label: 'Nursery & Childcare', group: 'Education', googleQuery: 'nurseries and childcare', osmFilters: ['amenity=kindergarten', 'amenity=childcare'] },
  { id: 'university', label: 'University & College', group: 'Education', googleQuery: 'universities and colleges', osmFilters: ['amenity=university', 'amenity=college'] },

  // Real Estate
  { id: 'real-estate-agency', label: 'Real Estate Agency', group: 'Real Estate', googleQuery: 'real estate agencies', osmFilters: ['office=estate_agent'] },
  { id: 'property-management', label: 'Property Management', group: 'Real Estate', googleQuery: 'property management companies', osmFilters: ['office=property_management'] },

  // Fitness & Sports
  { id: 'gym', label: 'Gym & Fitness Centre', group: 'Fitness & Sports', googleQuery: 'gyms and fitness centres', osmFilters: ['leisure=fitness_centre'] },
  { id: 'yoga-studio', label: 'Yoga & Pilates Studio', group: 'Fitness & Sports', googleQuery: 'yoga and pilates studios', osmFilters: ['leisure=fitness_centre', 'sport=yoga'] },
  { id: 'martial-arts', label: 'Martial Arts School', group: 'Fitness & Sports', googleQuery: 'martial arts schools', osmFilters: ['sport=martial_arts'] },
  { id: 'swimming-pool', label: 'Swimming Pool', group: 'Fitness & Sports', googleQuery: 'swimming pools', osmFilters: ['leisure=swimming_pool', 'leisure=sports_centre'] },

  // Entertainment
  { id: 'cinema', label: 'Cinema', group: 'Entertainment', googleQuery: 'cinemas', osmFilters: ['amenity=cinema'] },
  { id: 'nightclub', label: 'Nightclub', group: 'Entertainment', googleQuery: 'nightclubs', osmFilters: ['amenity=nightclub'] },
  { id: 'photographer', label: 'Photographer', group: 'Entertainment', googleQuery: 'photographers', osmFilters: ['craft=photographer', 'shop=photo'] },
  { id: 'event-planner', label: 'Event Planner', group: 'Entertainment', googleQuery: 'event planners', osmFilters: ['office=event_management'] },
  { id: 'museum', label: 'Museum & Gallery', group: 'Entertainment', googleQuery: 'museums and art galleries', osmFilters: ['tourism=museum', 'tourism=gallery'] },
];

const CATEGORY_BY_ID = new Map(BUSINESS_CATEGORIES.map((c) => [c.id, c]));

export function getCategory(id: string): BusinessCategory | undefined {
  return CATEGORY_BY_ID.get(id);
}

export function getCategoryLabel(id: string): string {
  return CATEGORY_BY_ID.get(id)?.label ?? id;
}

export function isValidCategory(id: string): boolean {
  return CATEGORY_BY_ID.has(id);
}

export const CATEGORIES_BY_GROUP = CATEGORY_GROUPS.map((group) => ({
  group,
  categories: BUSINESS_CATEGORIES.filter((c) => c.group === group),
}));
