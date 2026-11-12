using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database.Entities;

namespace Essenthos.Core.Loading.Encyclopedia;

/// <summary>
/// The shapes a region is drawn by, from OpenBible.info's geometry: <c>geometry.jsonl</c> lists every
/// shape the gazetteer holds beyond a point, and its <c>format</c> says whose it is. <c>kml</c> is the
/// gazetteer's own drawing (a rough boundary, nested bands of how sure the sources are, an area a place
/// probably lies in), CC BY 4.0 with the rest of the dataset; <c>osm</c> is OpenStreetMap's, ODbL 1.0.
///
/// <para>
/// An answer is drawn by OpenStreetMap's outline where the gazetteer gives it one as a polygon (its
/// <c>precise</c> role): for a modern feature such as an island the gazetteer's own boundary only says
/// where a label would go (readme, <em>Suggested</em>). Otherwise by the gazetteer's own drawing of an
/// area (its <c>geometry</c> role). Paths (roads, rivers) are not areas and are not taken; nor are the
/// circles the gazetteer draws round a site, which <see cref="PlaceCandidate.RadiusMeters"/> already
/// gives. Each shape is kept at two levels of detail; the two owners' shapes go to two files, so
/// OpenStreetMap's never sit in a file under another licence (Resources/OpenBible/LICENCE.md).
/// </para>
/// </summary>
internal static class OpenBibleOutlines
{
    public const string IndexFile = "geometry.jsonl";

    public const string Folder = "geometry";

    public const string OpenStreetMapDataset = "openstreetmap";

    public const string OwnSource =
        "OpenBible.info Bible Geocoding, github.com/openbibleinfo/Bible-Geocoding-Data, CC BY 4.0";

    public const string OpenStreetMapSource =
        "© OpenStreetMap contributors, ODbL 1.0 (openstreetmap.org/copyright), " +
        "through OpenBible.info Bible Geocoding, github.com/openbibleinfo/Bible-Geocoding-Data";

    /// <summary>The coarse shape strays at most this far from the drawing, in degrees (about a kilometre).</summary>
    public const double CoarseTolerance = 0.01;

    public const int CoarseDecimals = 3;

    /// <summary>The fine shape strays at most this far, about a hundred metres.</summary>
    public const double FineTolerance = 0.001;

    public const int FineDecimals = 4;

    /// <summary>The detail note every outline file carries, so a change of the rule rewrites them.</summary>
    public static string Detail =>
        string.Create(CultureInfo.InvariantCulture,
            $"coarse within {CoarseTolerance} degrees at {CoarseDecimals} decimals, fine within {FineTolerance} degrees at {FineDecimals} decimals");

    /// <summary>A shape the index lists as an area, and the file it is in.</summary>
    internal sealed record Entry(string Id, string Kind, bool OpenStreetMap, string File)
    {
        public string Dataset => OpenStreetMap ? OpenStreetMapDataset : Datasets.OpenBibleId;

        public string Source => OpenStreetMap ? OpenStreetMapSource : OwnSource;
    }

    /// <summary>One band of a shape: its rings, the outer one first, and how sure the sources are of it where it is a band.</summary>
    internal sealed record Band(int? Confidence, IReadOnlyList<IReadOnlyList<(double Lon, double Lat)>> Rings);

    /// <summary>Every area shape the index lists, by its identifier; paths are left out.</summary>
    public static Dictionary<string, Entry> Index(string file)
    {
        var index = new Dictionary<string, Entry>(StringComparer.Ordinal);
        foreach (var line in File.ReadLines(file))
        {
            if (line.Length == 0)
            {
                continue;
            }

            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (OpenBibleLocationLoader.Text(root, "id") is not { } id
                || OpenBibleLocationLoader.Text(root, "geometry") is not { } geometry)
            {
                continue;
            }

            var openStreetMap = OpenBibleLocationLoader.Text(root, "format") == "osm";
            var (kind, fileKey) = geometry switch
            {
                "rough_boundary" => ("rough-boundary", "geojson_file"),
                "isobands" => ("confidence-bands", "isobands_geojson_file"),
                "probability" => ("probable-area", "geojson_file"),
                "polygon" => (openStreetMap ? "precise" : "rough-boundary", "geojson_file"),
                _ => (null, null),
            };
            if (kind is not null && OpenBibleLocationLoader.Text(root, fileKey!) is { } name)
            {
                index[id] = new Entry(id, kind, openStreetMap, name);
            }
        }

        return index;
    }

    /// <summary>
    /// The shape a resolution of an answer is drawn by, or null where it has none: OpenStreetMap's
    /// polygon where the gazetteer gives one, else the gazetteer's own area.
    /// </summary>
    public static string? Choose(JsonElement resolution, IReadOnlyDictionary<string, Entry> index)
    {
        if (!resolution.TryGetProperty("geojson_roles", out var roles) || roles.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return Role("precise") is { OpenStreetMap: true } precise ? precise.Id
            : Role("geometry") is { OpenStreetMap: false } own ? own.Id
            : null;

        Entry? Role(string role) =>
            roles.TryGetProperty(role, out var drawn) && drawn.ValueKind == JsonValueKind.Object
            && OpenBibleLocationLoader.Text(drawn, "id") is { } feature
            && index.TryGetValue(feature.Split('.')[0], out var entry)
                ? entry
                : null;
    }

    /// <summary>Every shape named, read from the geometry folder and made into the rows the outline table holds, by identifier.</summary>
    public static List<PlaceOutline> Outlines(string openBible, IReadOnlyDictionary<string, Entry> index, IEnumerable<string> ids) =>
    [
        .. ids.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)
            .Select(id => Outline(index[id], Read(Path.Combine(openBible, Folder, index[id].File), index[id]))),
    ];

    /// <summary>
    /// A shape's bands as its GeoJSON gives them: a polygon is one band, a multipolygon of confidence
    /// bands one band each, their confidences spread evenly from <c>min_confidence</c> to <c>max_confidence</c>.
    /// </summary>
    public static List<Band> Read(string file, Entry entry)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(file));
        var root = document.RootElement;
        var geometry = root.TryGetProperty("geometry", out var inner) ? inner : root;
        var type = OpenBibleLocationLoader.Text(geometry, "type");
        var coordinates = geometry.GetProperty("coordinates");
        var polygons = type switch
        {
            "Polygon" => [coordinates],
            "MultiPolygon" => coordinates.EnumerateArray().ToList(),
            _ => throw new InvalidDataException($"{file}: a {type} is no area; the index calls {entry.Id} one."),
        };

        int? least = null, most = null;
        if (entry.Kind == "confidence-bands" && root.TryGetProperty("properties", out var properties))
        {
            least = properties.TryGetProperty("min_confidence", out var min) ? min.GetInt32() : null;
            most = properties.TryGetProperty("max_confidence", out var max) ? max.GetInt32() : null;
        }

        var bands = new List<Band>(polygons.Count);
        for (var i = 0; i < polygons.Count; i++)
        {
            int? confidence = least is { } low && most is { } high
                ? (int)Math.Round(polygons.Count == 1 ? high : low + ((high - low) * (double)i / (polygons.Count - 1)))
                : null;
            bands.Add(new Band(confidence, [.. polygons[i].EnumerateArray().Select(Ring)]));
        }

        return bands;

        static IReadOnlyList<(double, double)> Ring(JsonElement ring) =>
            [.. ring.EnumerateArray().Select(p => (p[0].GetDouble(), p[1].GetDouble()))];
    }

    /// <summary>The row of the outline table: the box round every ring, and the shape at both levels of detail.</summary>
    public static PlaceOutline Outline(Entry entry, IReadOnlyList<Band> bands)
    {
        var points = bands.SelectMany(b => b.Rings).SelectMany(r => r).ToList();
        if (points.Count == 0)
        {
            throw new InvalidDataException($"{entry.Id} has no points.");
        }

        return new PlaceOutline
        {
            Id = entry.Id,
            Kind = entry.Kind,
            Dataset = entry.Dataset,
            Source = entry.Source,
            West = points.Min(p => p.Lon),
            South = points.Min(p => p.Lat),
            East = points.Max(p => p.Lon),
            North = points.Max(p => p.Lat),
            Coarse = Json(bands, CoarseTolerance, CoarseDecimals),
            Fine = Json(bands, FineTolerance, FineDecimals),
        };
    }

    /// <summary>
    /// The bands as the API serves them, <c>[{"confidence":10,"rings":[[lon,lat,…],…]},…]</c>: each ring
    /// simplified within the tolerance, rounded, closed, and left out where it shrinks below a triangle
    /// (an islet at a scale where it is less than the tolerance across), except a band's outer ring, which
    /// keeps its drawing rather than vanish.
    /// </summary>
    public static string Json(IReadOnlyList<Band> bands, double tolerance, int decimals)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartArray();
            foreach (var band in bands)
            {
                json.WriteStartObject();
                if (band.Confidence is { } confidence)
                {
                    json.WriteNumber("confidence", confidence);
                }
                else
                {
                    json.WriteNull("confidence");
                }

                json.WriteStartArray("rings");
                for (var r = 0; r < band.Rings.Count; r++)
                {
                    var ring = Rounded(Simplify(band.Rings[r], tolerance), decimals);
                    if (ring.Count < 4)
                    {
                        if (r > 0)
                        {
                            continue;
                        }

                        ring = Rounded(band.Rings[r], decimals);
                    }

                    json.WriteStartArray();
                    foreach (var (lon, lat) in ring)
                    {
                        json.WriteNumberValue(lon);
                        json.WriteNumberValue(lat);
                    }

                    json.WriteEndArray();
                }

                json.WriteEndArray();
                json.WriteEndObject();
            }

            json.WriteEndArray();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>
    /// A closed ring with fewer points, none of the dropped ones further than the tolerance from what is
    /// kept (Douglas–Peucker, run from the first point to the one furthest from it and back).
    /// </summary>
    public static List<(double Lon, double Lat)> Simplify(IReadOnlyList<(double Lon, double Lat)> ring, double tolerance)
    {
        if (ring.Count < 4)
        {
            return [.. ring];
        }

        var last = ring.Count - 1;
        var far = 0;
        var farthest = -1.0;
        for (var i = 1; i < last; i++)
        {
            var d = Distance(ring[i], ring[0]);
            if (d > farthest)
            {
                farthest = d;
                far = i;
            }
        }

        var keep = new bool[ring.Count];
        keep[0] = keep[far] = keep[last] = true;
        var spans = new Stack<(int From, int To)>([(0, far), (far, last)]);
        while (spans.Count > 0)
        {
            var (from, to) = spans.Pop();
            var at = -1;
            var worst = tolerance;
            for (var i = from + 1; i < to; i++)
            {
                var d = FromSegment(ring[i], ring[from], ring[to]);
                if (d > worst)
                {
                    worst = d;
                    at = i;
                }
            }

            if (at >= 0)
            {
                keep[at] = true;
                spans.Push((from, at));
                spans.Push((at, to));
            }
        }

        return [.. ring.Where((_, i) => keep[i])];
    }

    private static List<(double Lon, double Lat)> Rounded(IReadOnlyList<(double Lon, double Lat)> ring, int decimals)
    {
        var rounded = new List<(double, double)>(ring.Count);
        foreach (var (lon, lat) in ring)
        {
            var point = (Math.Round(lon, decimals, MidpointRounding.AwayFromZero), Math.Round(lat, decimals, MidpointRounding.AwayFromZero));
            if (rounded.Count == 0 || rounded[^1] != point)
            {
                rounded.Add(point);
            }
        }

        return rounded;
    }

    private static double Distance((double Lon, double Lat) a, (double Lon, double Lat) b) =>
        Math.Sqrt(((a.Lon - b.Lon) * (a.Lon - b.Lon)) + ((a.Lat - b.Lat) * (a.Lat - b.Lat)));

    private static double FromSegment((double Lon, double Lat) p, (double Lon, double Lat) a, (double Lon, double Lat) b)
    {
        var dx = b.Lon - a.Lon;
        var dy = b.Lat - a.Lat;
        var length = (dx * dx) + (dy * dy);
        if (length == 0)
        {
            return Distance(p, a);
        }

        var t = Math.Clamp((((p.Lon - a.Lon) * dx) + ((p.Lat - a.Lat) * dy)) / length, 0, 1);
        return Distance(p, (a.Lon + (t * dx), a.Lat + (t * dy)));
    }
}
