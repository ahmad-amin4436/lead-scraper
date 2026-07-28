/**
 * Location catalogue.
 *
 * `COUNTRIES` is the full ISO 3166-1 list. `REGIONS` holds curated
 * state/city data for high-traffic markets purely as a UX convenience — the
 * search form always accepts a free-text city, which the geocoding service
 * resolves to coordinates. So a country missing from `REGIONS` is still fully
 * searchable.
 */

export interface CountryOption {
  code: string;
  name: string;
}

export interface RegionOption {
  name: string;
  cities: string[];
}

export const COUNTRIES: CountryOption[] = [
  { code: 'AF', name: 'Afghanistan' }, { code: 'AL', name: 'Albania' }, { code: 'DZ', name: 'Algeria' },
  { code: 'AD', name: 'Andorra' }, { code: 'AO', name: 'Angola' }, { code: 'AG', name: 'Antigua and Barbuda' },
  { code: 'AR', name: 'Argentina' }, { code: 'AM', name: 'Armenia' }, { code: 'AU', name: 'Australia' },
  { code: 'AT', name: 'Austria' }, { code: 'AZ', name: 'Azerbaijan' }, { code: 'BS', name: 'Bahamas' },
  { code: 'BH', name: 'Bahrain' }, { code: 'BD', name: 'Bangladesh' }, { code: 'BB', name: 'Barbados' },
  { code: 'BY', name: 'Belarus' }, { code: 'BE', name: 'Belgium' }, { code: 'BZ', name: 'Belize' },
  { code: 'BJ', name: 'Benin' }, { code: 'BT', name: 'Bhutan' }, { code: 'BO', name: 'Bolivia' },
  { code: 'BA', name: 'Bosnia and Herzegovina' }, { code: 'BW', name: 'Botswana' }, { code: 'BR', name: 'Brazil' },
  { code: 'BN', name: 'Brunei' }, { code: 'BG', name: 'Bulgaria' }, { code: 'BF', name: 'Burkina Faso' },
  { code: 'BI', name: 'Burundi' }, { code: 'KH', name: 'Cambodia' }, { code: 'CM', name: 'Cameroon' },
  { code: 'CA', name: 'Canada' }, { code: 'CV', name: 'Cape Verde' }, { code: 'CF', name: 'Central African Republic' },
  { code: 'TD', name: 'Chad' }, { code: 'CL', name: 'Chile' }, { code: 'CN', name: 'China' },
  { code: 'CO', name: 'Colombia' }, { code: 'KM', name: 'Comoros' }, { code: 'CG', name: 'Congo' },
  { code: 'CD', name: 'Congo (DRC)' }, { code: 'CR', name: 'Costa Rica' }, { code: 'CI', name: "Côte d'Ivoire" },
  { code: 'HR', name: 'Croatia' }, { code: 'CU', name: 'Cuba' }, { code: 'CY', name: 'Cyprus' },
  { code: 'CZ', name: 'Czechia' }, { code: 'DK', name: 'Denmark' }, { code: 'DJ', name: 'Djibouti' },
  { code: 'DM', name: 'Dominica' }, { code: 'DO', name: 'Dominican Republic' }, { code: 'EC', name: 'Ecuador' },
  { code: 'EG', name: 'Egypt' }, { code: 'SV', name: 'El Salvador' }, { code: 'GQ', name: 'Equatorial Guinea' },
  { code: 'ER', name: 'Eritrea' }, { code: 'EE', name: 'Estonia' }, { code: 'SZ', name: 'Eswatini' },
  { code: 'ET', name: 'Ethiopia' }, { code: 'FJ', name: 'Fiji' }, { code: 'FI', name: 'Finland' },
  { code: 'FR', name: 'France' }, { code: 'GA', name: 'Gabon' }, { code: 'GM', name: 'Gambia' },
  { code: 'GE', name: 'Georgia' }, { code: 'DE', name: 'Germany' }, { code: 'GH', name: 'Ghana' },
  { code: 'GR', name: 'Greece' }, { code: 'GD', name: 'Grenada' }, { code: 'GT', name: 'Guatemala' },
  { code: 'GN', name: 'Guinea' }, { code: 'GW', name: 'Guinea-Bissau' }, { code: 'GY', name: 'Guyana' },
  { code: 'HT', name: 'Haiti' }, { code: 'HN', name: 'Honduras' }, { code: 'HK', name: 'Hong Kong' },
  { code: 'HU', name: 'Hungary' }, { code: 'IS', name: 'Iceland' }, { code: 'IN', name: 'India' },
  { code: 'ID', name: 'Indonesia' }, { code: 'IR', name: 'Iran' }, { code: 'IQ', name: 'Iraq' },
  { code: 'IE', name: 'Ireland' }, { code: 'IL', name: 'Israel' }, { code: 'IT', name: 'Italy' },
  { code: 'JM', name: 'Jamaica' }, { code: 'JP', name: 'Japan' }, { code: 'JO', name: 'Jordan' },
  { code: 'KZ', name: 'Kazakhstan' }, { code: 'KE', name: 'Kenya' }, { code: 'KI', name: 'Kiribati' },
  { code: 'KW', name: 'Kuwait' }, { code: 'KG', name: 'Kyrgyzstan' }, { code: 'LA', name: 'Laos' },
  { code: 'LV', name: 'Latvia' }, { code: 'LB', name: 'Lebanon' }, { code: 'LS', name: 'Lesotho' },
  { code: 'LR', name: 'Liberia' }, { code: 'LY', name: 'Libya' }, { code: 'LI', name: 'Liechtenstein' },
  { code: 'LT', name: 'Lithuania' }, { code: 'LU', name: 'Luxembourg' }, { code: 'MO', name: 'Macao' },
  { code: 'MG', name: 'Madagascar' }, { code: 'MW', name: 'Malawi' }, { code: 'MY', name: 'Malaysia' },
  { code: 'MV', name: 'Maldives' }, { code: 'ML', name: 'Mali' }, { code: 'MT', name: 'Malta' },
  { code: 'MR', name: 'Mauritania' }, { code: 'MU', name: 'Mauritius' }, { code: 'MX', name: 'Mexico' },
  { code: 'MD', name: 'Moldova' }, { code: 'MC', name: 'Monaco' }, { code: 'MN', name: 'Mongolia' },
  { code: 'ME', name: 'Montenegro' }, { code: 'MA', name: 'Morocco' }, { code: 'MZ', name: 'Mozambique' },
  { code: 'MM', name: 'Myanmar' }, { code: 'NA', name: 'Namibia' }, { code: 'NP', name: 'Nepal' },
  { code: 'NL', name: 'Netherlands' }, { code: 'NZ', name: 'New Zealand' }, { code: 'NI', name: 'Nicaragua' },
  { code: 'NE', name: 'Niger' }, { code: 'NG', name: 'Nigeria' }, { code: 'MK', name: 'North Macedonia' },
  { code: 'NO', name: 'Norway' }, { code: 'OM', name: 'Oman' }, { code: 'PK', name: 'Pakistan' },
  { code: 'PS', name: 'Palestine' }, { code: 'PA', name: 'Panama' }, { code: 'PG', name: 'Papua New Guinea' },
  { code: 'PY', name: 'Paraguay' }, { code: 'PE', name: 'Peru' }, { code: 'PH', name: 'Philippines' },
  { code: 'PL', name: 'Poland' }, { code: 'PT', name: 'Portugal' }, { code: 'QA', name: 'Qatar' },
  { code: 'RO', name: 'Romania' }, { code: 'RU', name: 'Russia' }, { code: 'RW', name: 'Rwanda' },
  { code: 'SA', name: 'Saudi Arabia' }, { code: 'SN', name: 'Senegal' }, { code: 'RS', name: 'Serbia' },
  { code: 'SC', name: 'Seychelles' }, { code: 'SL', name: 'Sierra Leone' }, { code: 'SG', name: 'Singapore' },
  { code: 'SK', name: 'Slovakia' }, { code: 'SI', name: 'Slovenia' }, { code: 'SB', name: 'Solomon Islands' },
  { code: 'SO', name: 'Somalia' }, { code: 'ZA', name: 'South Africa' }, { code: 'KR', name: 'South Korea' },
  { code: 'SS', name: 'South Sudan' }, { code: 'ES', name: 'Spain' }, { code: 'LK', name: 'Sri Lanka' },
  { code: 'SD', name: 'Sudan' }, { code: 'SR', name: 'Suriname' }, { code: 'SE', name: 'Sweden' },
  { code: 'CH', name: 'Switzerland' }, { code: 'SY', name: 'Syria' }, { code: 'TW', name: 'Taiwan' },
  { code: 'TJ', name: 'Tajikistan' }, { code: 'TZ', name: 'Tanzania' }, { code: 'TH', name: 'Thailand' },
  { code: 'TL', name: 'Timor-Leste' }, { code: 'TG', name: 'Togo' }, { code: 'TO', name: 'Tonga' },
  { code: 'TT', name: 'Trinidad and Tobago' }, { code: 'TN', name: 'Tunisia' }, { code: 'TR', name: 'Türkiye' },
  { code: 'TM', name: 'Turkmenistan' }, { code: 'UG', name: 'Uganda' }, { code: 'UA', name: 'Ukraine' },
  { code: 'AE', name: 'United Arab Emirates' }, { code: 'GB', name: 'United Kingdom' },
  { code: 'US', name: 'United States' }, { code: 'UY', name: 'Uruguay' }, { code: 'UZ', name: 'Uzbekistan' },
  { code: 'VU', name: 'Vanuatu' }, { code: 'VE', name: 'Venezuela' }, { code: 'VN', name: 'Vietnam' },
  { code: 'YE', name: 'Yemen' }, { code: 'ZM', name: 'Zambia' }, { code: 'ZW', name: 'Zimbabwe' },
];

export const REGIONS: Record<string, RegionOption[]> = {
  US: [
    { name: 'California', cities: ['Los Angeles', 'San Diego', 'San Francisco', 'San Jose', 'Sacramento', 'Fresno', 'Oakland'] },
    { name: 'Texas', cities: ['Houston', 'Dallas', 'Austin', 'San Antonio', 'Fort Worth', 'El Paso'] },
    { name: 'New York', cities: ['New York City', 'Buffalo', 'Rochester', 'Albany', 'Syracuse'] },
    { name: 'Florida', cities: ['Miami', 'Orlando', 'Tampa', 'Jacksonville', 'Fort Lauderdale', 'St. Petersburg'] },
    { name: 'Illinois', cities: ['Chicago', 'Aurora', 'Naperville', 'Springfield', 'Rockford'] },
    { name: 'Pennsylvania', cities: ['Philadelphia', 'Pittsburgh', 'Allentown', 'Erie'] },
    { name: 'Ohio', cities: ['Columbus', 'Cleveland', 'Cincinnati', 'Toledo'] },
    { name: 'Georgia', cities: ['Atlanta', 'Savannah', 'Augusta', 'Columbus'] },
    { name: 'North Carolina', cities: ['Charlotte', 'Raleigh', 'Greensboro', 'Durham'] },
    { name: 'Michigan', cities: ['Detroit', 'Grand Rapids', 'Ann Arbor', 'Lansing'] },
    { name: 'Arizona', cities: ['Phoenix', 'Tucson', 'Mesa', 'Scottsdale'] },
    { name: 'Washington', cities: ['Seattle', 'Spokane', 'Tacoma', 'Bellevue'] },
    { name: 'Massachusetts', cities: ['Boston', 'Worcester', 'Springfield', 'Cambridge'] },
    { name: 'Colorado', cities: ['Denver', 'Colorado Springs', 'Aurora', 'Boulder'] },
    { name: 'Nevada', cities: ['Las Vegas', 'Reno', 'Henderson'] },
  ],
  GB: [
    { name: 'England', cities: ['London', 'Manchester', 'Birmingham', 'Liverpool', 'Leeds', 'Bristol', 'Sheffield', 'Newcastle', 'Nottingham', 'Brighton'] },
    { name: 'Scotland', cities: ['Edinburgh', 'Glasgow', 'Aberdeen', 'Dundee', 'Inverness'] },
    { name: 'Wales', cities: ['Cardiff', 'Swansea', 'Newport', 'Wrexham'] },
    { name: 'Northern Ireland', cities: ['Belfast', 'Londonderry', 'Lisburn'] },
  ],
  CA: [
    { name: 'Ontario', cities: ['Toronto', 'Ottawa', 'Mississauga', 'Hamilton', 'London', 'Kitchener'] },
    { name: 'Quebec', cities: ['Montreal', 'Quebec City', 'Laval', 'Gatineau'] },
    { name: 'British Columbia', cities: ['Vancouver', 'Victoria', 'Surrey', 'Burnaby', 'Kelowna'] },
    { name: 'Alberta', cities: ['Calgary', 'Edmonton', 'Red Deer', 'Lethbridge'] },
    { name: 'Manitoba', cities: ['Winnipeg', 'Brandon'] },
    { name: 'Nova Scotia', cities: ['Halifax', 'Sydney'] },
  ],
  AU: [
    { name: 'New South Wales', cities: ['Sydney', 'Newcastle', 'Wollongong', 'Central Coast'] },
    { name: 'Victoria', cities: ['Melbourne', 'Geelong', 'Ballarat', 'Bendigo'] },
    { name: 'Queensland', cities: ['Brisbane', 'Gold Coast', 'Cairns', 'Townsville', 'Sunshine Coast'] },
    { name: 'Western Australia', cities: ['Perth', 'Fremantle', 'Bunbury'] },
    { name: 'South Australia', cities: ['Adelaide', 'Mount Gambier'] },
    { name: 'Tasmania', cities: ['Hobart', 'Launceston'] },
    { name: 'Australian Capital Territory', cities: ['Canberra'] },
  ],
  AE: [
    { name: 'Dubai', cities: ['Dubai', 'Deira', 'Jumeirah', 'Business Bay'] },
    { name: 'Abu Dhabi', cities: ['Abu Dhabi', 'Al Ain', 'Ruwais'] },
    { name: 'Sharjah', cities: ['Sharjah', 'Khor Fakkan'] },
    { name: 'Ajman', cities: ['Ajman'] },
    { name: 'Ras Al Khaimah', cities: ['Ras Al Khaimah'] },
    { name: 'Fujairah', cities: ['Fujairah'] },
    { name: 'Umm Al Quwain', cities: ['Umm Al Quwain'] },
  ],
  PK: [
    { name: 'Punjab', cities: ['Lahore', 'Faisalabad', 'Rawalpindi', 'Multan', 'Gujranwala', 'Sialkot'] },
    { name: 'Sindh', cities: ['Karachi', 'Hyderabad', 'Sukkur', 'Larkana'] },
    { name: 'Khyber Pakhtunkhwa', cities: ['Peshawar', 'Abbottabad', 'Mardan', 'Swat'] },
    { name: 'Balochistan', cities: ['Quetta', 'Gwadar', 'Turbat'] },
    { name: 'Islamabad Capital Territory', cities: ['Islamabad'] },
    { name: 'Azad Kashmir', cities: ['Muzaffarabad', 'Mirpur'] },
  ],
  IN: [
    { name: 'Maharashtra', cities: ['Mumbai', 'Pune', 'Nagpur', 'Nashik', 'Thane'] },
    { name: 'Delhi', cities: ['New Delhi', 'Delhi'] },
    { name: 'Karnataka', cities: ['Bangalore', 'Mysore', 'Mangalore', 'Hubli'] },
    { name: 'Tamil Nadu', cities: ['Chennai', 'Coimbatore', 'Madurai', 'Tiruchirappalli'] },
    { name: 'Telangana', cities: ['Hyderabad', 'Warangal'] },
    { name: 'Gujarat', cities: ['Ahmedabad', 'Surat', 'Vadodara', 'Rajkot'] },
    { name: 'West Bengal', cities: ['Kolkata', 'Howrah', 'Durgapur'] },
    { name: 'Uttar Pradesh', cities: ['Lucknow', 'Kanpur', 'Noida', 'Varanasi', 'Agra'] },
    { name: 'Rajasthan', cities: ['Jaipur', 'Jodhpur', 'Udaipur'] },
  ],
  DE: [
    { name: 'Bavaria', cities: ['Munich', 'Nuremberg', 'Augsburg', 'Regensburg'] },
    { name: 'North Rhine-Westphalia', cities: ['Cologne', 'Düsseldorf', 'Dortmund', 'Essen', 'Bonn'] },
    { name: 'Berlin', cities: ['Berlin'] },
    { name: 'Hamburg', cities: ['Hamburg'] },
    { name: 'Baden-Württemberg', cities: ['Stuttgart', 'Karlsruhe', 'Mannheim', 'Freiburg'] },
    { name: 'Hesse', cities: ['Frankfurt', 'Wiesbaden', 'Kassel', 'Darmstadt'] },
    { name: 'Saxony', cities: ['Dresden', 'Leipzig', 'Chemnitz'] },
  ],
  FR: [
    { name: 'Île-de-France', cities: ['Paris', 'Versailles', 'Boulogne-Billancourt'] },
    { name: "Provence-Alpes-Côte d'Azur", cities: ['Marseille', 'Nice', 'Toulon', 'Aix-en-Provence'] },
    { name: 'Auvergne-Rhône-Alpes', cities: ['Lyon', 'Grenoble', 'Saint-Étienne', 'Annecy'] },
    { name: 'Occitanie', cities: ['Toulouse', 'Montpellier', 'Nîmes', 'Perpignan'] },
    { name: 'Nouvelle-Aquitaine', cities: ['Bordeaux', 'Limoges', 'Poitiers'] },
    { name: 'Hauts-de-France', cities: ['Lille', 'Amiens', 'Roubaix'] },
  ],
  ES: [
    { name: 'Madrid', cities: ['Madrid', 'Móstoles', 'Alcalá de Henares'] },
    { name: 'Catalonia', cities: ['Barcelona', 'Girona', 'Tarragona', 'Lleida'] },
    { name: 'Andalusia', cities: ['Seville', 'Málaga', 'Granada', 'Córdoba', 'Marbella'] },
    { name: 'Valencia', cities: ['Valencia', 'Alicante', 'Castellón'] },
    { name: 'Basque Country', cities: ['Bilbao', 'San Sebastián', 'Vitoria-Gasteiz'] },
  ],
  IT: [
    { name: 'Lazio', cities: ['Rome', 'Latina'] },
    { name: 'Lombardy', cities: ['Milan', 'Bergamo', 'Brescia', 'Monza'] },
    { name: 'Campania', cities: ['Naples', 'Salerno'] },
    { name: 'Tuscany', cities: ['Florence', 'Pisa', 'Siena'] },
    { name: 'Veneto', cities: ['Venice', 'Verona', 'Padua'] },
    { name: 'Piedmont', cities: ['Turin', 'Novara'] },
  ],
  NL: [
    { name: 'North Holland', cities: ['Amsterdam', 'Haarlem', 'Alkmaar'] },
    { name: 'South Holland', cities: ['Rotterdam', 'The Hague', 'Leiden', 'Delft'] },
    { name: 'Utrecht', cities: ['Utrecht', 'Amersfoort'] },
    { name: 'North Brabant', cities: ['Eindhoven', 'Tilburg', 'Breda'] },
  ],
  SA: [
    { name: 'Riyadh', cities: ['Riyadh', 'Al Kharj'] },
    { name: 'Makkah', cities: ['Jeddah', 'Mecca', 'Taif'] },
    { name: 'Eastern Province', cities: ['Dammam', 'Khobar', 'Dhahran', 'Jubail'] },
    { name: 'Madinah', cities: ['Medina', 'Yanbu'] },
  ],
  ZA: [
    { name: 'Gauteng', cities: ['Johannesburg', 'Pretoria', 'Soweto', 'Midrand'] },
    { name: 'Western Cape', cities: ['Cape Town', 'Stellenbosch', 'George'] },
    { name: 'KwaZulu-Natal', cities: ['Durban', 'Pietermaritzburg'] },
    { name: 'Eastern Cape', cities: ['Gqeberha', 'East London'] },
  ],
  SG: [{ name: 'Singapore', cities: ['Singapore', 'Jurong', 'Tampines', 'Woodlands'] }],
  MY: [
    { name: 'Kuala Lumpur', cities: ['Kuala Lumpur'] },
    { name: 'Selangor', cities: ['Shah Alam', 'Petaling Jaya', 'Subang Jaya'] },
    { name: 'Penang', cities: ['George Town', 'Butterworth'] },
    { name: 'Johor', cities: ['Johor Bahru', 'Batu Pahat'] },
  ],
  BR: [
    { name: 'São Paulo', cities: ['São Paulo', 'Campinas', 'Santos', 'Guarulhos'] },
    { name: 'Rio de Janeiro', cities: ['Rio de Janeiro', 'Niterói'] },
    { name: 'Minas Gerais', cities: ['Belo Horizonte', 'Uberlândia'] },
    { name: 'Bahia', cities: ['Salvador', 'Feira de Santana'] },
  ],
  MX: [
    { name: 'Mexico City', cities: ['Mexico City'] },
    { name: 'Jalisco', cities: ['Guadalajara', 'Zapopan', 'Puerto Vallarta'] },
    { name: 'Nuevo León', cities: ['Monterrey', 'San Pedro Garza García'] },
    { name: 'Quintana Roo', cities: ['Cancún', 'Playa del Carmen'] },
  ],
  IE: [
    { name: 'Leinster', cities: ['Dublin', 'Drogheda', 'Dundalk'] },
    { name: 'Munster', cities: ['Cork', 'Limerick', 'Waterford'] },
    { name: 'Connacht', cities: ['Galway', 'Sligo'] },
  ],
  NZ: [
    { name: 'Auckland', cities: ['Auckland', 'Manukau', 'North Shore'] },
    { name: 'Wellington', cities: ['Wellington', 'Lower Hutt'] },
    { name: 'Canterbury', cities: ['Christchurch', 'Timaru'] },
    { name: 'Otago', cities: ['Dunedin', 'Queenstown'] },
  ],
};

const COUNTRY_BY_CODE = new Map(COUNTRIES.map((c) => [c.code, c]));

export function getCountryName(code: string): string {
  return COUNTRY_BY_CODE.get(code)?.name ?? code;
}

export function isValidCountry(code: string): boolean {
  return COUNTRY_BY_CODE.has(code);
}

export function getRegions(countryCode: string): RegionOption[] {
  return REGIONS[countryCode] ?? [];
}

export function getCities(countryCode: string, regionName?: string): string[] {
  const regions = getRegions(countryCode);
  if (!regionName) return [...new Set(regions.flatMap((r) => r.cities))].sort();
  return regions.find((r) => r.name === regionName)?.cities ?? [];
}

export const RADIUS_OPTIONS = [
  { value: 1000, label: '1 km' },
  { value: 2000, label: '2 km' },
  { value: 5000, label: '5 km' },
  { value: 10000, label: '10 km' },
  { value: 25000, label: '25 km' },
  { value: 50000, label: '50 km' },
] as const;

export const MAX_RESULT_OPTIONS = [20, 40, 60, 100, 150, 200] as const;
