using Essenthos.Core.Database.Entities;

namespace Essenthos.Core.Loading.Encyclopedia;

/// <summary>
/// Where a region stands on a map of points (PRB-1438). The gazetteer gives many a region the point of
/// a town in it as its representative point: Judea and Beyond the River stand on Jerusalem, Babylonia
/// and Shinar on Babylon, the Negev on Beersheba, Egypt on On. A map of points then draws the land as
/// one spot under the town's dot, where no zoom can part them. Where the gazetteer also draws the
/// region's extent, the point is taken from that drawing instead: the spot inside it farthest from its
/// edges, in the band about as sure as not. It is still a representative point and says so; the town
/// stays the region's proposed site, as the gazetteer gives it. A region with no drawn extent keeps the
/// town's point, since nothing better is known; so does a place that really stands in the town.
/// </summary>
internal static class RegionPoints
{
    /// <summary>The kind a region's point is given, and keeps.</summary>
    private const string Representative = "representative-point";

    /// <summary>The steps of the grid the spot is sought on, twice: over the shape, then round the best.</summary>
    private const int Grid = 48;

    /// <summary>
    /// Moves each region that stands on a town's point to the middle of its drawn extent; returns the
    /// regions moved, by entity, with the point each had and has.
    /// </summary>
    public static List<(int EntityId, double FromLon, double FromLat, double Lon, double Lat)> Move(
        List<PlaceLocation> locations,
        IReadOnlyList<PlaceCandidate> candidates,
        Func<string, IReadOnlyList<OpenBibleOutlines.Band>?> bandsOf)
    {
        // The towns: places the gazetteer stands at a site's own point, by that site.
        var towns = locations.Where(l => l.Kind != Representative).Select(l => (l.ModernId, l.Longitude, l.Latitude)).ToHashSet();
        var moved = new List<(int, double, double, double, double)>();
        foreach (var location in locations.Where(l => l.Kind == Representative))
        {
            if (!towns.Contains((location.ModernId, location.Longitude, location.Latitude)))
            {
                continue;
            }

            // The answer the point is: the likeliest, a region, with a drawn extent.
            var answer = candidates
                .Where(c => c.EntityId == location.EntityId && c.ModernId == location.ModernId)
                .OrderBy(c => c.Rank)
                .FirstOrDefault();
            if (answer is not { SiteType: "region", OutlineId: { } outline } || bandsOf(outline) is not { Count: > 0 } bands)
            {
                continue;
            }

            if (Middle(bands) is not var (lon, lat))
            {
                continue;
            }

            moved.Add((location.EntityId, location.Longitude, location.Latitude, lon, lat));
            location.Longitude = lon;
            location.Latitude = lat;
        }

        return moved;
    }

    /// <summary>
    /// The spot inside the band about as sure as not (the only band of a plain boundary) farthest from
    /// its edges, rounded to five decimals; null where the band has no inside to speak of. Distances
    /// east and west are shrunk by the latitude, as on the ground.
    /// </summary>
    public static (double Lon, double Lat)? Middle(IReadOnlyList<OpenBibleOutlines.Band> bands)
    {
        var band = bands[0];
        foreach (var other in bands)
        {
            if (other.Confidence is { } confidence && Math.Abs(confidence - 50) < Math.Abs((band.Confidence ?? 50) - 50))
            {
                band = other;
            }
        }

        var rings = band.Rings.Where(r => r.Count >= 3).ToList();
        if (rings.Count == 0)
        {
            return null;
        }

        var points = rings.SelectMany(r => r).ToList();
        var (west, east) = (points.Min(p => p.Lon), points.Max(p => p.Lon));
        var (south, north) = (points.Min(p => p.Lat), points.Max(p => p.Lat));
        if (east <= west || north <= south)
        {
            return null;
        }

        var squeeze = Math.Cos((south + north) / 2 * Math.PI / 180);
        double Off(double lon, double lat) => Math.Abs((lon - ((west + east) / 2)) * squeeze) + Math.Abs(lat - ((south + north) / 2));
        (double Lon, double Lat, double Depth)? best = null;
        void Search(double left, double bottom, double width, double height)
        {
            for (var i = 0; i <= Grid; i++)
            {
                for (var j = 0; j <= Grid; j++)
                {
                    var lon = left + (width * i / Grid);
                    var lat = bottom + (height * j / Grid);
                    if (!Inside(rings, lon, lat))
                    {
                        continue;
                    }

                    var depth = EdgeDistance(rings, lon, lat, squeeze);
                    // Among spots as deep as one another, the one nearest the middle of the box.
                    if (best is null || depth > best.Value.Depth + 1e-9
                        || (depth > best.Value.Depth - 1e-9 && Off(lon, lat) < Off(best.Value.Lon, best.Value.Lat)))
                    {
                        best = (lon, lat, depth);
                    }
                }
            }
        }

        Search(west, south, east - west, north - south);
        if (best is not { } coarse)
        {
            return null;
        }

        var (stepLon, stepLat) = ((east - west) / Grid, (north - south) / Grid);
        Search(coarse.Lon - stepLon, coarse.Lat - stepLat, 2 * stepLon, 2 * stepLat);
        return (Math.Round(best!.Value.Lon, 5), Math.Round(best.Value.Lat, 5));
    }

    /// <summary>Whether a point is inside the rings, the even-odd way: a hole is outside.</summary>
    private static bool Inside(List<IReadOnlyList<(double Lon, double Lat)>> rings, double lon, double lat)
    {
        var inside = false;
        foreach (var ring in rings)
        {
            for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
            {
                var (xi, yi) = ring[i];
                var (xj, yj) = ring[j];
                if ((yi > lat) != (yj > lat) && lon < ((xj - xi) * (lat - yi) / (yj - yi)) + xi)
                {
                    inside = !inside;
                }
            }
        }

        return inside;
    }

    /// <summary>The distance from a point to the nearest edge of any ring, east-west shrunk by the latitude.</summary>
    private static double EdgeDistance(List<IReadOnlyList<(double Lon, double Lat)>> rings, double lon, double lat, double squeeze)
    {
        var nearest = double.MaxValue;
        foreach (var ring in rings)
        {
            for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
            {
                var (ax, ay) = ((ring[j].Lon - lon) * squeeze, ring[j].Lat - lat);
                var (bx, by) = ((ring[i].Lon - lon) * squeeze, ring[i].Lat - lat);
                var (dx, dy) = (bx - ax, by - ay);
                var length = (dx * dx) + (dy * dy);
                var t = length == 0 ? 0 : Math.Clamp(-((ax * dx) + (ay * dy)) / length, 0, 1);
                var (px, py) = (ax + (t * dx), ay + (t * dy));
                nearest = Math.Min(nearest, Math.Sqrt((px * px) + (py * py)));
            }
        }

        return nearest;
    }
}
