namespace LeadMine.Infrastructure.Scraping;

/// <summary>
/// Business categories, with the query shape each provider needs.
/// <para>
/// One entry carries both a Google Places text fragment and OpenStreetMap tag
/// filters, so the same user-facing choice works against either source. Ids
/// match the front end's catalogue; an unknown id falls back to using the id
/// itself as the search term rather than failing the run.
/// </para>
/// </summary>
public sealed record BusinessCategory(string Id, string Label, string GoogleQuery, string[] OsmFilters);

public static class CategoryCatalog
{
    private static readonly BusinessCategory[] Items =
    [
        // Food & drink
        new("restaurant", "Restaurant", "restaurants", ["amenity=restaurant"]),
        new("cafe", "Cafe & Coffee Shop", "cafes and coffee shops", ["amenity=cafe"]),
        new("bakery", "Bakery", "bakeries", ["shop=bakery"]),
        new("bar", "Bar & Pub", "bars and pubs", ["amenity=bar", "amenity=pub"]),
        new("fast-food", "Fast Food", "fast food restaurants", ["amenity=fast_food"]),
        new("catering", "Catering Service", "catering services", ["craft=caterer", "shop=caterer"]),
        new("grocery", "Grocery & Supermarket", "supermarkets and grocery stores", ["shop=supermarket", "shop=grocery", "shop=convenience"]),
        new("butcher", "Butcher", "butcher shops", ["shop=butcher"]),

        // Retail
        new("clothing", "Clothing Store", "clothing stores", ["shop=clothes", "shop=boutique"]),
        new("shoe-store", "Shoe Store", "shoe stores", ["shop=shoes"]),
        new("jewelry", "Jewelry Store", "jewelry stores", ["shop=jewelry"]),
        new("electronics", "Electronics Store", "electronics stores", ["shop=electronics", "shop=computer", "shop=mobile_phone"]),
        new("furniture", "Furniture Store", "furniture stores", ["shop=furniture", "shop=interior_decoration"]),
        new("hardware", "Hardware & DIY", "hardware stores", ["shop=hardware", "shop=doityourself"]),
        new("florist", "Florist", "florists", ["shop=florist"]),
        new("bookstore", "Bookstore", "bookstores", ["shop=books"]),
        new("pet-store", "Pet Store", "pet stores", ["shop=pet"]),
        new("sporting-goods", "Sporting Goods", "sporting goods stores", ["shop=sports"]),

        // Health
        new("dentist", "Dentist", "dentists", ["amenity=dentist", "healthcare=dentist"]),
        new("doctor", "Doctor & Clinic", "medical clinics and doctors", ["amenity=doctors", "amenity=clinic", "healthcare=doctor"]),
        new("pharmacy", "Pharmacy", "pharmacies", ["amenity=pharmacy"]),
        new("veterinary", "Veterinary Clinic", "veterinary clinics", ["amenity=veterinary"]),
        new("optician", "Optician", "opticians and eye care", ["shop=optician", "healthcare=optometrist"]),
        new("physiotherapy", "Physiotherapy", "physiotherapy clinics", ["healthcare=physiotherapist"]),
        new("hospital", "Hospital", "hospitals", ["amenity=hospital"]),
        new("laboratory", "Medical Laboratory", "medical laboratories", ["healthcare=laboratory"]),

        // Beauty & wellness
        new("hair-salon", "Hair Salon", "hair salons", ["shop=hairdresser"]),
        new("beauty-salon", "Beauty Salon", "beauty salons", ["shop=beauty"]),
        new("spa", "Spa & Massage", "spas and massage", ["leisure=spa", "shop=massage"]),
        new("nail-salon", "Nail Salon", "nail salons", ["shop=beauty"]),
        new("barber", "Barber Shop", "barber shops", ["shop=hairdresser"]),
        new("tattoo", "Tattoo Studio", "tattoo studios", ["shop=tattoo"]),

        // Professional services
        new("law-firm", "Law Firm", "law firms and lawyers", ["office=lawyer"]),
        new("accountant", "Accountant", "accountants", ["office=accountant"]),
        new("marketing-agency", "Marketing Agency", "marketing agencies", ["office=advertising_agency", "office=marketing"]),
        new("it-services", "IT & Software Services", "IT services and software companies", ["office=it", "office=company"]),
        new("insurance", "Insurance Agency", "insurance agencies", ["office=insurance"]),
        new("consulting", "Business Consulting", "business consultants", ["office=consulting"]),
        new("financial-advisor", "Financial Advisor", "financial advisors", ["office=financial", "office=financial_advisor"]),
        new("architect", "Architect", "architects", ["office=architect"]),
        new("printing", "Printing & Signage", "printing and signage companies", ["shop=copyshop", "craft=signmaker"]),
        new("travel-agency", "Travel Agency", "travel agencies", ["shop=travel_agency"]),

        // Home services
        new("plumber", "Plumber", "plumbers", ["craft=plumber"]),
        new("electrician", "Electrician", "electricians", ["craft=electrician"]),
        new("builder", "Builder & Contractor", "building contractors", ["craft=builder", "office=construction_company"]),
        new("painter", "Painter & Decorator", "painters and decorators", ["craft=painter"]),
        new("carpenter", "Carpenter & Joiner", "carpenters and joiners", ["craft=carpenter"]),
        new("landscaping", "Landscaping & Gardening", "landscaping and gardening services", ["craft=gardener", "shop=garden_centre"]),
        new("cleaning", "Cleaning Service", "cleaning services", ["shop=laundry", "craft=cleaning"]),
        new("hvac", "HVAC & Heating", "HVAC and heating engineers", ["craft=hvac"]),
        new("roofing", "Roofing", "roofing contractors", ["craft=roofer"]),
        new("locksmith", "Locksmith", "locksmiths", ["craft=locksmith", "shop=locksmith"]),
        new("moving", "Moving & Removals", "moving and removal companies", ["office=moving_company"]),

        // Automotive
        new("car-repair", "Car Repair & Garage", "car repair garages", ["shop=car_repair"]),
        new("car-dealer", "Car Dealership", "car dealerships", ["shop=car"]),
        new("car-wash", "Car Wash", "car washes", ["amenity=car_wash"]),
        new("tyre-shop", "Tyre Shop", "tyre and tire shops", ["shop=tyres"]),
        new("car-rental", "Car Rental", "car rental companies", ["amenity=car_rental"]),
        new("driving-school", "Driving School", "driving schools", ["amenity=driving_school"]),
        new("motorcycle", "Motorcycle Shop", "motorcycle dealers", ["shop=motorcycle"]),

        // Hospitality
        new("hotel", "Hotel", "hotels", ["tourism=hotel"]),
        new("guest-house", "Guest House & B&B", "guest houses and bed and breakfast", ["tourism=guest_house", "tourism=bed_and_breakfast"]),
        new("hostel", "Hostel", "hostels", ["tourism=hostel"]),
        new("event-venue", "Event Venue", "event venues", ["amenity=events_venue", "amenity=conference_centre"]),

        // Education
        new("school", "School", "schools", ["amenity=school"]),
        new("language-school", "Language School", "language schools", ["amenity=language_school"]),
        new("tutoring", "Tutoring Centre", "tutoring centres", ["amenity=prep_school", "office=educational_institution"]),
        new("nursery", "Nursery & Childcare", "nurseries and childcare", ["amenity=kindergarten", "amenity=childcare"]),
        new("university", "University & College", "universities and colleges", ["amenity=university", "amenity=college"]),

        // Real estate
        new("real-estate-agency", "Real Estate Agency", "real estate agencies", ["office=estate_agent"]),
        new("property-management", "Property Management", "property management companies", ["office=property_management"]),

        // Fitness
        new("gym", "Gym & Fitness Centre", "gyms and fitness centres", ["leisure=fitness_centre"]),
        new("yoga-studio", "Yoga & Pilates Studio", "yoga and pilates studios", ["leisure=fitness_centre", "sport=yoga"]),
        new("martial-arts", "Martial Arts School", "martial arts schools", ["sport=martial_arts"]),
        new("swimming-pool", "Swimming Pool", "swimming pools", ["leisure=swimming_pool", "leisure=sports_centre"]),

        // Entertainment
        new("cinema", "Cinema", "cinemas", ["amenity=cinema"]),
        new("nightclub", "Nightclub", "nightclubs", ["amenity=nightclub"]),
        new("photographer", "Photographer", "photographers", ["craft=photographer", "shop=photo"]),
        new("event-planner", "Event Planner", "event planners", ["office=event_management"]),
        new("museum", "Museum & Gallery", "museums and art galleries", ["tourism=museum", "tourism=gallery"]),
    ];

    private static readonly Dictionary<string, BusinessCategory> ById =
        Items.ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<BusinessCategory> All => Items;

    /// <summary>
    /// Looks up a category. An unrecognised id becomes a plain text query rather
    /// than an error, so a newly added front-end category still searches.
    /// </summary>
    public static BusinessCategory Resolve(string id) =>
        ById.TryGetValue(id, out var found)
            ? found
            : new BusinessCategory(id, id, id, []);
}
