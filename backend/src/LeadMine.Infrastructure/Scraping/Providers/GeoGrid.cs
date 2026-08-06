namespace LeadMine.Infrastructure.Scraping.Providers;

/// <summary>
/// Turns a city center and a requested radius into a set of overlapping search
/// points for <see cref="PlaywrightGoogleMapsProvider"/>.
/// <para>
/// Unlike the Places API, Google Maps' website has no radius parameter — the
/// closest lever is the <c>@lat,lng,zoom</c> viewport segment a search URL can
/// carry, which biases the results feed toward whatever area that viewport
/// covers. One such search only really covers a few kilometres before the feed
/// starts returning places nowhere near what the user asked for, so a bigger
/// requested radius is covered by tiling several of these viewport-biased
/// searches across the area instead, and letting the caller merge and dedupe
/// what each one finds.
/// </para>
/// </summary>
public static class GeoGrid
{
    private const double EarthRadiusMeters = 6_371_000;

    /// <summary>
    /// Below this, a single search centered on the city already covers the
    /// requested area about as well as tiling would — not worth the extra page
    /// loads.
    /// </summary>
    private const double SingleCellRadiusMeters = 5_000;

    /// <summary>
    /// Bounds on how large one tile's own radius is allowed to be. Above the
    /// upper bound, the viewport-bias trick stops being meaningfully "local";
    /// below the lower bound, Maps' own feed for one search is already at
    /// least this dense, so a finer tile buys nothing.
    /// </summary>
    private const double MinCellRadiusMeters = 1_500;
    private const double MaxCellRadiusMeters = 6_000;

    /// <summary>
    /// Neighbouring tiles overlap by this factor of a cell's radius (i.e.
    /// spacing is 1.5x the radius, not the full 2x a non-overlapping grid would
    /// use), so a business sitting near a tile boundary is not missed without
    /// every tile re-covering most of its neighbours.
    /// </summary>
    private const double OverlapFactor = 1.5;

    /// <summary>
    /// Hard ceiling on searches generated for one task.
    /// <para>
    /// Measured against the live site: one Maps search's feed exhausts at
    /// roughly 20 places, and neighbouring cells overlap by design, so cells
    /// past the first handful return mostly businesses already seen. 120 cells
    /// meant a single (city × category) task could issue 120 full searches —
    /// minutes of work for a rapidly shrinking number of new results. The
    /// caller stops earlier still once cells stop producing anything new (see
    /// <c>PlaywrightGoogleMapsProvider</c>); this is only the backstop.
    /// </para>
    /// </summary>
    private const int MaxCells = 24;

    /// <summary>One search point: where to center the Maps viewport and how far out it should be zoomed.</summary>
    public readonly record struct Cell(double Latitude, double Longitude, int Zoom);

    /// <summary>
    /// The search points needed to cover a circle of <paramref name="radiusMeters"/>
    /// around <paramref name="centerLat"/>/<paramref name="centerLng"/>, nearest
    /// first, so a caller that stops early (max results reached) still spends
    /// its searches on the area closest to what the user asked for.
    /// </summary>
    public static IReadOnlyList<Cell> BuildCoverage(double centerLat, double centerLng, int radiusMeters)
    {
        var radius = Math.Max(radiusMeters, 250);

        if (radius <= SingleCellRadiusMeters)
        {
            return [new Cell(centerLat, centerLng, ZoomFor(centerLat, radius))];
        }

        // Cell size scales with the requested radius (within the bounds above)
        // rather than staying fixed, so a bigger ask is covered by more,
        // appropriately-sized tiles instead of an ever-larger single grid of
        // tiny ones.
        var cellRadius = Math.Clamp(radius / 3.0, MinCellRadiusMeters, MaxCellRadiusMeters);
        var step = cellRadius * OverlapFactor;
        var latStep = MetersToLatDegrees(step);
        var rings = (int)Math.Ceiling(radius / step);

        var candidates = new List<(double Lat, double Lng, double Distance)>();

        for (var row = -rings; row <= rings; row++)
        {
            var lat = centerLat + row * latStep;

            // Every other row is offset half a step, a hex-ish packing that
            // covers a circle with noticeably fewer tiles than a plain square
            // grid would need for the same coverage.
            var rowOffsetMeters = row % 2 != 0 ? step / 2 : 0;
            var lngStep = MetersToLngDegrees(step, lat);
            var lngOffset = MetersToLngDegrees(rowOffsetMeters, lat);

            for (var col = -rings; col <= rings; col++)
            {
                var lng = centerLng + col * lngStep + lngOffset;
                var distance = HaversineMeters(centerLat, centerLng, lat, lng);

                // A tile whose center sits just outside the requested radius
                // still covers part of it (a tile has its own radius), so the
                // cutoff is the requested radius plus half a step, not the
                // radius itself.
                if (distance <= radius + step / 2) candidates.Add((lat, lng, distance));
            }
        }

        return candidates
            .OrderBy(c => c.Distance)
            .Take(MaxCells)
            .Select(c => new Cell(c.Lat, c.Lng, ZoomFor(c.Lat, cellRadius)))
            .ToList();
    }

    /// <summary>
    /// The zoom level whose half-viewport roughly spans <paramref name="radiusMeters"/>,
    /// so a search's results feed is biased toward approximately that radius
    /// around <paramref name="latitude"/>. Derived from the standard Web
    /// Mercator meters-per-pixel formula.
    /// </summary>
    private static int ZoomFor(double latitude, double radiusMeters)
    {
        // Half of the 1366px-wide viewport PlaywrightGoogleMapsProvider opens
        // its pages at.
        const double viewportHalfWidthPx = 640;

        var metersPerPixel = radiusMeters / viewportHalfWidthPx;
        var zoom = Math.Log2(156_543.03392 * Math.Cos(latitude * Math.PI / 180) / metersPerPixel);
        return Math.Clamp((int)Math.Round(zoom), 3, 20);
    }

    private static double MetersToLatDegrees(double meters) => meters / 111_320.0;

    private static double MetersToLngDegrees(double meters, double atLatitudeDegrees) =>
        meters / (111_320.0 * Math.Max(0.01, Math.Cos(atLatitudeDegrees * Math.PI / 180)));

    private static double HaversineMeters(double lat1, double lng1, double lat2, double lng2)
    {
        var dLat = ToRadians(lat2 - lat1);
        var dLng = ToRadians(lng2 - lng1);

        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) *
                Math.Sin(dLng / 2) * Math.Sin(dLng / 2);

        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return EarthRadiusMeters * c;
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180;
}
