using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Loading.Encyclopedia;

internal sealed record LocationsOutcome(
    bool AlreadyLoaded,
    int Located,
    int OpenStreetMap,
    int Commercial,
    int Unidentified,
    int Unheld,
    TimeSpan Elapsed)
{
    public override string ToString() =>
        AlreadyLoaded
            ? "the places are already located"
            : $"{Located} places located; {OpenStreetMap} left unlocated because their coordinates are " +
              $"OpenStreetMap's, {Commercial} because they are read off a commercial map, " +
              $"{Unidentified} the gazetteer does not identify, and {Unheld} located places the " +
              $"encyclopedia holds no record of, in {Elapsed}";
}

/// <summary>
/// Where each place is, as one point, from OpenBible.info's Bible Geocoding.
///
/// <para>
/// <c>ancient.jsonl</c> already says, for every place, which modern location each identification
/// resolves to and at what coordinates, and scores the identifications. <c>modern.jsonl</c> says
/// where each modern location's coordinates came from, and that is the only reason it is read: the
/// repository is CC BY 4.0 except for what it takes from OpenStreetMap, which is ODbL and
/// share-alike, and a point is only safe to hold when the credit on that point says it is not
/// OpenStreetMap's.
/// </para>
///
/// <para>
/// What is left out, and why, is counted in the outcome: a point the gazetteer credits to
/// OpenStreetMap, a point read off Google Maps with no independent point beside it, and a place the
/// gazetteer cannot identify at all. Where it read a point off a commercial map and made its own
/// beside it, its own is the one taken. See Resources/OpenBible/LICENCE.md.
/// </para>
/// </summary>
internal sealed class OpenBibleLocationLoader(AppDbContext db, ILogger<OpenBibleLocationLoader> logger)
{
    private const string Source =
        "OpenBible.info Bible Geocoding, github.com/openbibleinfo/Bible-Geocoding-Data, CC BY 4.0";

    private const string AncientFile = "ancient.jsonl";

    private const string ModernFile = "modern.jsonl";

    /// <summary>How the gazetteer credits OpenStreetMap, as a source type and as a geometry credit.</summary>
    private const string OpenStreetMap = "osm";

    /// <summary>
    /// Coordinates read off a commercial map whose terms are its owner's. The gazetteer offers an
    /// independent point beside some commercial readings; where it offers none, as for these, there
    /// is nothing to take.
    /// </summary>
    private static readonly HashSet<string> CommercialMaps = ["google_maps"];

    /// <summary>
    /// The gazetteer's four words for what a point stands for, and how this corpus spells them. It
    /// writes one of them with a space.
    /// </summary>
    private static readonly Dictionary<string, string> Kinds = new(StringComparer.Ordinal)
    {
        ["point"] = "point",
        ["representative point"] = "representative-point",
        ["center"] = "center",
        ["settlement"] = "settlement",
    };

    /// <summary>
    /// Which of a modern location's drawn roles the point is, so that a credit on that role can be
    /// read. A representative point has a role of its own; the other three are drawn as the point.
    /// </summary>
    private static string Role(string kind) => kind == "representative point" ? "representative_point" : "point";

    public async Task<LocationsOutcome> Load(string folder, CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.StartNew();
        var ancient = Path.Combine(folder, AncientFile);
        var modern = Path.Combine(folder, ModernFile);
        if (!File.Exists(ancient) || !File.Exists(modern))
        {
            logger.LogWarning(
                "No place locations: {Ancient} and {Modern} are both needed. Run " +
                "scripts/fetch-openbible.ps1; without them no place has a point on a map.",
                ancient, modern);
            return new LocationsOutcome(true, 0, 0, 0, 0, 0, started.Elapsed);
        }

        if (await db.PlaceLocations.AnyAsync(cancellationToken))
        {
            return new LocationsOutcome(true, 0, 0, 0, 0, 0, started.Elapsed);
        }

        var read = Read(ancient, modern);

        var places = await db.Entities
            .Where(e => e.Kind == EntityKind.Place && e.OpenBibleId != null)
            .Select(e => new { e.Id, e.OpenBibleId })
            .ToListAsync(cancellationToken);
        var byOpenBibleId = places.ToLookup(p => p.OpenBibleId!, p => p.Id, StringComparer.Ordinal);

        var rows = new List<PlaceLocation>(read.Located.Count);
        var unheld = 0;
        foreach (var located in read.Located)
        {
            if (!byOpenBibleId.Contains(located.PlaceId))
            {
                unheld++;
                continue;
            }

            rows.AddRange(byOpenBibleId[located.PlaceId].Select(entityId => new PlaceLocation
            {
                EntityId = entityId,
                Longitude = located.Longitude,
                Latitude = located.Latitude,
                Kind = located.Kind,
                Score = located.Score,
                ModernId = located.ModernId,
                CoordinatesSource = located.CoordinatesSource,
                Source = Source,
            }));
        }

        db.PlaceLocations.AddRange(rows);
        await db.SaveChangesAsync(cancellationToken);

        var outcome = new LocationsOutcome(
            false, rows.Count, read.OpenStreetMap, read.Commercial, read.Unidentified, unheld, started.Elapsed);
        logger.LogInformation("Located the places: {Outcome}", outcome);
        return outcome;
    }

    /// <param name="PlaceId">The gazetteer's identifier for the ancient place — what an entity holds.</param>
    internal sealed record Located(
        string PlaceId,
        double Longitude,
        double Latitude,
        string Kind,
        int Score,
        string ModernId,
        string CoordinatesSource);

    internal sealed record Reading(
        List<Located> Located,
        int OpenStreetMap,
        int Commercial,
        int Unidentified);

    /// <summary>
    /// Every place's best point that the terms allow, and a count of each reason a place has none.
    /// </summary>
    internal static Reading Read(string ancientFile, string modernFile)
    {
        var modern = new Dictionary<string, Modern>(StringComparer.Ordinal);
        foreach (var line in File.ReadLines(modernFile))
        {
            if (line.Length == 0)
            {
                continue;
            }

            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (Text(root, "id") is { } id)
            {
                modern[id] = Modern.Of(root);
            }
        }

        var located = new List<Located>(1_400);
        int openStreetMap = 0, commercial = 0, unidentified = 0;

        foreach (var line in File.ReadLines(ancientFile))
        {
            if (line.Length == 0)
            {
                continue;
            }

            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (Text(root, "id") is not { } placeId)
            {
                continue;
            }

            if (Best(root) is not { } resolution
                || Text(resolution.Element, "lonlat") is not { } stated
                || Text(resolution.Element, "lonlat_type") is not { } type
                || !Kinds.TryGetValue(type, out var kind)
                || Text(resolution.Element, "modern_basis_id") is not { } basis
                || !modern.TryGetValue(basis, out var at))
            {
                unidentified++;
                continue;
            }

            if (at.CreditedToOpenStreetMap(Role(type)))
            {
                openStreetMap++;
                continue;
            }

            if (at.Independent is null && CommercialMaps.Contains(at.CoordinatesSource))
            {
                commercial++;
                continue;
            }

            if (Point(at.Independent ?? stated) is not { } point)
            {
                unidentified++;
                continue;
            }

            located.Add(new Located(
                placeId, point.Longitude, point.Latitude, kind, resolution.Score, basis, at.CoordinatesSource));
        }

        return new Reading(located, openStreetMap, commercial, unidentified);
    }

    /// <summary>A resolution, the identification it belongs to, and that identification's score.</summary>
    internal readonly record struct Resolution(JsonElement Element, int Score, JsonElement Identification);

    /// <summary>
    /// The resolution the gazetteer scores highest among a place's modern associations, which are
    /// already the identifications weighted by how sure anyone is of the site. A tie goes to the
    /// lower modern identifier, so a reload answers the same.
    /// </summary>
    internal static Resolution? Best(JsonElement root)
    {
        if (!root.TryGetProperty("modern_associations", out var associations)
            || associations.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("identifications", out var identifications)
            || identifications.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var best = associations.EnumerateObject()
            .Where(a => a.Value.TryGetProperty("score", out var s) && s.ValueKind == JsonValueKind.Number)
            .OrderByDescending(a => a.Value.GetProperty("score").GetInt32())
            .ThenBy(a => a.Name, StringComparer.Ordinal)
            .FirstOrDefault();

        if (best.Value.ValueKind != JsonValueKind.Object
            || !best.Value.TryGetProperty("identification_ids", out var ids)
            || ids.ValueKind != JsonValueKind.Array
            || ids.GetArrayLength() == 0)
        {
            return null;
        }

        var pair = ids[0];
        var identification = pair[0].GetInt32();
        var resolution = pair[1].GetInt32();
        if (identification >= identifications.GetArrayLength()
            || !identifications[identification].TryGetProperty("resolutions", out var resolutions)
            || resolution >= resolutions.GetArrayLength())
        {
            return null;
        }

        return new Resolution(
            resolutions[resolution], best.Value.GetProperty("score").GetInt32(), identifications[identification]);
    }

    /// <param name="Independent">
    /// The point the gazetteer made itself beside one it read off a commercial source, where it made
    /// one.
    /// </param>
    private sealed record Modern(
        string CoordinatesSource,
        bool SourceIsOpenStreetMap,
        HashSet<string> OpenStreetMapRoles,
        string? Independent)
    {
        public bool CreditedToOpenStreetMap(string role) =>
            SourceIsOpenStreetMap || OpenStreetMapRoles.Contains(role);

        public static Modern Of(JsonElement root)
        {
            var source = "unstated";
            var fromOpenStreetMap = false;
            if (root.TryGetProperty("coordinates_source", out var credit) && credit.ValueKind == JsonValueKind.Object)
            {
                source = Text(credit, "type") ?? source;
                fromOpenStreetMap = source == OpenStreetMap || Text(credit, "geometry_credit") == OpenStreetMap;
            }

            var roles = new HashSet<string>(StringComparer.Ordinal);
            if (root.TryGetProperty("geojson_roles", out var drawn) && drawn.ValueKind == JsonValueKind.Object)
            {
                foreach (var role in drawn.EnumerateObject())
                {
                    if (role.Value.ValueKind == JsonValueKind.Object
                        && Text(role.Value, "geometry_credit") == OpenStreetMap)
                    {
                        roles.Add(role.Name);
                    }
                }
            }

            return new Modern(source, fromOpenStreetMap, roles, Text(root, "custom_lonlat"));
        }
    }

    /// <summary>The gazetteer writes a point as one string, longitude first: <c>35.2,31.7</c>.</summary>
    internal static (double Longitude, double Latitude)? Point(string lonlat)
    {
        var parts = lonlat.Split(',');
        return parts.Length == 2
               && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var longitude)
               && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var latitude)
               && longitude is >= -180 and <= 180
               && latitude is >= -90 and <= 90
            ? (longitude, latitude)
            : null;
    }

    internal static string? Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
        && value.GetString() is { Length: > 0 } text
            ? text
            : null;
}
