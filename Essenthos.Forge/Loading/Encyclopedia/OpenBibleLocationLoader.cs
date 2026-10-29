using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Loading.Encyclopedia;

internal sealed record LocationsOutcome(
    bool AlreadyLoaded,
    int Located,
    int Candidates,
    int OpenStreetMap,
    int Commercial,
    int NoSite,
    int Unidentified,
    int Unheld,
    TimeSpan Elapsed)
{
    public override string ToString() =>
        AlreadyLoaded
            ? "the places are already located"
            : $"{Located} places located and {Candidates} proposed sites held; {OpenStreetMap} places left " +
              $"unlocated because their likeliest site's coordinates are OpenStreetMap's, {Commercial} " +
              $"because it is read off a commercial map, {NoSite} because the gazetteer's likeliest answer " +
              $"is that they are no located place, {Unidentified} the gazetteer does not identify, and " +
              $"{Unheld} located places the encyclopedia holds no record of, in {Elapsed}";
}

/// <summary>
/// Where each place is, from OpenBible.info's Bible Geocoding: every answer the gazetteer records,
/// ranked by its score, and the likeliest of them as the place's one point.
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
/// A site whose point the terms do not allow is still held as an answer, by its name and score, with
/// no point. An answer that is no site — the gazetteer's <em>not a place</em>, <em>unknown place</em>
/// — is held too, and where it outranks every site the place gets no point: falling back to the
/// best site would put the place where its own source thinks it probably is not. Where the gazetteer
/// read a point off a commercial map and made its own beside it, its own is the one taken. See
/// Resources/OpenBible/LICENCE.md.
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
    /// How a place stands to the site it is identified by, in the gazetteer's modifiers: inside it
    /// (<c>&lt;</c>), the region round it (<c>&gt;</c>), near it, along a river, on a mountain.
    /// </summary>
    private static readonly Dictionary<string, string> Relations = new(StringComparer.Ordinal)
    {
        ["near"] = "near",
        ["along"] = "along",
        ["on"] = "on",
        ["<"] = "in",
        [">"] = "around",
    };

    /// <summary>
    /// The gazetteer's answers that are no site. <c>recursive</c> is left out: it says only that
    /// every path from the place leads back to it, which tells a reader nothing.
    /// </summary>
    private static readonly Dictionary<string, string> Specials = new(StringComparer.Ordinal)
    {
        ["not_a_place"] = "not-a-place",
        ["not_a_proper_name"] = "not-a-proper-name",
        ["unknown_place"] = "unknown-place",
        ["nonspecific_place"] = "nonspecific-place",
        ["multiple_locations"] = "multiple-locations",
    };

    /// <summary>The gazetteer's tags for on whose authority an identification is made.</summary>
    private const string AuthorityTag = "authority_";

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
            return new LocationsOutcome(true, 0, 0, 0, 0, 0, 0, 0, started.Elapsed);
        }

        var supplement = LocationSupplement.Read();
        var read = supplement.Apply(Read(ancient, modern));
        var regions = supplement.Regions.ToDictionary(r => r.Slug, StringComparer.Ordinal);
        var regionSlugs = regions.Keys.ToList();
        var laterTowns = SharedSites.All.Select(s => s.Later).ToList();

        var places = await db.Entities
            .Where(e => e.Kind == EntityKind.Place
                        && (e.OpenBibleId != null || regionSlugs.Contains(e.Slug) || laterTowns.Contains(e.Slug)))
            .Select(e => new { e.Id, e.Slug, e.OpenBibleId })
            .ToListAsync(cancellationToken);
        var bySlug = places.ToDictionary(p => p.Slug, StringComparer.Ordinal);
        // A town built on an earlier one's site stands at that site's point and takes its answers.
        var builtOn = SharedSites.All
            .Where(s => bySlug.ContainsKey(s.Later) && bySlug.GetValueOrDefault(s.Earlier)?.OpenBibleId is not null)
            .Select(s => (OpenBibleId: bySlug[s.Earlier].OpenBibleId!, bySlug[s.Later].Id));
        var byOpenBibleId = places.Where(p => p.OpenBibleId != null)
            .Select(p => (OpenBibleId: p.OpenBibleId!, p.Id))
            .Concat(builtOn)
            .ToLookup(p => p.OpenBibleId, p => p.Id, StringComparer.Ordinal);

        var locations = new List<PlaceLocation>(read.Located.Count);
        var unheld = read.Located.Count(located => !byOpenBibleId.Contains(located.PlaceId));
        foreach (var located in read.Located)
        {
            locations.AddRange(byOpenBibleId[located.PlaceId].Select(entityId => new PlaceLocation
            {
                EntityId = entityId,
                Longitude = located.Longitude,
                Latitude = located.Latitude,
                Kind = located.Kind,
                Score = located.Score,
                ModernId = located.ModernId,
                CoordinatesSource = located.CoordinatesSource,
                CoordinatesDataset = located.CoordinatesDataset,
                Source = Credited(located.CoordinatesCredit),
            }));
        }

        var candidates = new List<PlaceCandidate>();
        foreach (var (placeId, answers) in read.Candidates)
        {
            foreach (var entityId in byOpenBibleId[placeId])
            {
                candidates.AddRange(answers.Select(answer => new PlaceCandidate
                {
                    EntityId = entityId,
                    Rank = answer.Rank,
                    Score = answer.Score,
                    ModernId = answer.ModernId,
                    Site = answer.Site,
                    Longitude = answer.Longitude,
                    Latitude = answer.Latitude,
                    Kind = answer.Kind,
                    RadiusMeters = answer.RadiusMeters,
                    SiteType = answer.SiteType,
                    Relation = answer.Relation,
                    ThroughOpenBibleId = answer.Through,
                    Special = answer.Special,
                    Withheld = answer.Withheld,
                    Grounds = [.. answer.Grounds],
                    CoordinatesSource = answer.CoordinatesSource,
                    CoordinatesDataset = answer.CoordinatesDataset,
                    Source = Credited(answer.CoordinatesCredit),
                }));
            }
        }

        foreach (var place in places.Where(p => regions.ContainsKey(p.Slug)))
        {
            var region = regions[place.Slug];
            if (locations.Any(l => l.EntityId == place.Id) || candidates.Any(c => c.EntityId == place.Id)
                || !LocationSupplement.ValidPoint(region.Lon, region.Lat) || string.IsNullOrWhiteSpace(region.Credit))
                throw new InvalidDataException($"The representative region conflicts with a held answer: {place.Slug}.");
            // The source places the region and scores nothing, so the point carries no score at all.
            locations.Add(new PlaceLocation
            {
                EntityId = place.Id, Longitude = region.Lon, Latitude = region.Lat, Kind = "representative-point",
                Score = null, ModernId = region.ModernId, CoordinatesSource = region.Dataset,
                CoordinatesDataset = region.Dataset, Source = region.Credit,
            });
            candidates.Add(new PlaceCandidate
            {
                EntityId = place.Id, Rank = 0, Score = null, ModernId = region.ModernId, Site = region.Site,
                Longitude = region.Lon, Latitude = region.Lat, Kind = "representative-point", SiteType = "region",
                CoordinatesSource = region.Dataset, CoordinatesDataset = region.Dataset, Source = region.Credit,
            });
        }

        if (await Holds(locations, candidates, cancellationToken))
        {
            return new LocationsOutcome(true, 0, 0, 0, 0, 0, 0, 0, started.Elapsed);
        }

        // The points and the answers are replaced together or not at all: a place must never be
        // left with a point its answers no longer give, nor without the point it had.
        await using (var own = db.Database.CurrentTransaction is null
                         ? await db.Database.BeginTransactionAsync(cancellationToken)
                         : null)
        {
            await db.PlaceCandidates.ExecuteDeleteAsync(cancellationToken);
            await db.PlaceLocations.ExecuteDeleteAsync(cancellationToken);
            foreach (var gone in db.ChangeTracker.Entries()
                         .Where(e => e.Entity is PlaceLocation or PlaceCandidate).ToList())
            {
                gone.State = EntityState.Detached;
            }

            db.PlaceLocations.AddRange(locations);
            db.PlaceCandidates.AddRange(candidates);
            await db.SaveChangesAsync(cancellationToken);
            if (own is not null)
            {
                await own.CommitAsync(cancellationToken);
            }
        }

        var outcome = new LocationsOutcome(
            false, locations.Count, candidates.Count, read.OpenStreetMap, read.Commercial, read.NoSite,
            read.Unidentified, unheld, started.Elapsed);
        logger.LogInformation("Located the places: {Outcome}", outcome);
        return outcome;
    }

    /// <summary>Whether the tables already hold exactly these rows, so a load writes nothing.</summary>
    private async Task<bool> Holds(
        List<PlaceLocation> locations, List<PlaceCandidate> candidates, CancellationToken cancellationToken)
    {
        var heldLocations = await db.PlaceLocations.AsNoTracking().ToListAsync(cancellationToken);
        var heldCandidates = await db.PlaceCandidates.AsNoTracking().ToListAsync(cancellationToken);
        return heldLocations.Count == locations.Count
               && heldCandidates.Count == candidates.Count
               && heldLocations.Select(Row).ToHashSet().SetEquals(locations.Select(Row))
               && heldCandidates.Select(Row).ToHashSet().SetEquals(candidates.Select(Row));
    }

    /// <summary>The gazetteer's credit, and where another source gave the coordinates, that source's too.</summary>
    private static string Credited(string? coordinates) => coordinates is null ? Source : $"{Source}; coordinates: {coordinates}";

    private static string Row(PlaceLocation l) =>
        string.Join('\u001f',
            l.EntityId, Exact(l.Longitude), Exact(l.Latitude), l.Kind, l.Score, l.ModernId, l.CoordinatesSource,
            l.CoordinatesDataset, l.Source);

    private static string Row(PlaceCandidate c) =>
        string.Join('\u001f',
            c.EntityId, c.Rank, c.Score, c.ModernId, c.Site, Exact(c.Longitude), Exact(c.Latitude), c.Kind,
            c.RadiusMeters, c.SiteType, c.Relation, c.ThroughOpenBibleId, c.Special, c.Withheld,
            string.Join(',', c.Grounds), c.CoordinatesSource, c.CoordinatesDataset, c.Source);

    private static string Exact(double? value) => value?.ToString("R", CultureInfo.InvariantCulture) ?? "";

    /// <param name="PlaceId">The gazetteer's identifier for the ancient place — what an entity holds.</param>
    /// <param name="Score">Null where the source of the point scores nothing.</param>
    internal sealed record Located(
        string PlaceId,
        double Longitude,
        double Latitude,
        string Kind,
        int? Score,
        string ModernId,
        string CoordinatesSource)
    {
        /// <summary>The declared dataset whose coordinates these are, where they are not the gazetteer's.</summary>
        public string? CoordinatesDataset { get; init; }

        /// <summary>Where in that dataset they were read, for the row's own credit.</summary>
        public string? CoordinatesCredit { get; init; }
    }

    /// <summary>One answer to where a place was, as <see cref="PlaceCandidate"/> holds it.</summary>
    internal sealed record Candidate(
        int Rank,
        int Score,
        string? ModernId,
        string? Site,
        double? Longitude,
        double? Latitude,
        string? Kind,
        int? RadiusMeters,
        string? SiteType,
        string? Relation,
        string? Through,
        string? Special,
        string? Withheld,
        IReadOnlyList<string> Grounds,
        string? CoordinatesSource)
    {
        /// <summary>The declared dataset whose coordinates these are, where they are not the gazetteer's.</summary>
        public string? CoordinatesDataset { get; init; }

        /// <summary>Where in that dataset they were read, for the row's own credit.</summary>
        public string? CoordinatesCredit { get; init; }
    }

    /// <param name="Candidates">Every place's answers, the likeliest first, by the gazetteer's id.</param>
    /// <param name="NoSite">Places whose likeliest answer is that they are no located place.</param>
    internal sealed record Reading(
        List<Located> Located,
        Dictionary<string, List<Candidate>> Candidates,
        int OpenStreetMap,
        int Commercial,
        int NoSite,
        int Unidentified);

    /// <summary>
    /// Every place's answers, and its likeliest one as its point where the terms allow it, with a
    /// count of each reason a place has none.
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
        var candidates = new Dictionary<string, List<Candidate>>(StringComparer.Ordinal);
        int openStreetMap = 0, commercial = 0, noSite = 0, unidentified = 0;

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

            var answers = Answers(root, modern);
            if (answers.Count == 0)
            {
                unidentified++;
                continue;
            }

            candidates[placeId] = answers;
            var first = answers[0];
            if (first.Special is not null)
            {
                noSite++;
            }
            else if (first.Withheld == "openstreetmap")
            {
                openStreetMap++;
            }
            else if (first.Withheld == "commercial")
            {
                commercial++;
            }
            else if (first is { Longitude: { } longitude, Latitude: { } latitude, Kind: { } kind })
            {
                located.Add(new Located(
                    placeId, longitude, latitude, kind, first.Score, first.ModernId!, first.CoordinatesSource!));
            }
            else
            {
                unidentified++;
            }
        }

        return new Reading(located, candidates, openStreetMap, commercial, noSite, unidentified);
    }

    /// <summary>
    /// A place's answers ranked by the gazetteer's score: each modern location its associations
    /// score, read through the identification and resolution the association names first, and each
    /// kind of answer that is no site at the best score the gazetteer gives it. A site goes before an
    /// answer that is no site at the same score, and a tie between sites to the lower modern
    /// identifier, so a reload answers the same.
    /// </summary>
    private static List<Candidate> Answers(JsonElement root, Dictionary<string, Modern> modern)
    {
        var found = new List<Candidate>();
        if (!root.TryGetProperty("identifications", out var identifications)
            || identifications.ValueKind != JsonValueKind.Array)
        {
            return found;
        }

        if (root.TryGetProperty("modern_associations", out var associations)
            && associations.ValueKind == JsonValueKind.Object)
        {
            foreach (var association in associations.EnumerateObject())
            {
                if (Site(association, identifications, modern) is { } site)
                {
                    found.Add(site);
                }
            }
        }

        var specials = new Dictionary<string, Candidate>(StringComparer.Ordinal);
        foreach (var identification in identifications.EnumerateArray())
        {
            if (Text(identification, "id_source") != "special"
                || Text(identification, "special") is not { } said
                || !Specials.TryGetValue(said, out var special))
            {
                continue;
            }

            var score = TimeTotal(identification);
            if (!specials.TryGetValue(special, out var held) || score > held.Score)
            {
                specials[special] = new Candidate(
                    0, score, null, null, null, null, null, null, null, null, null, special, null,
                    Grounds(identification), null);
            }
        }

        found.AddRange(specials.Values);
        return
        [
            .. found
                .OrderByDescending(c => c.Score)
                .ThenBy(c => c.Special is null ? 0 : 1)
                .ThenBy(c => c.ModernId, StringComparer.Ordinal)
                .ThenBy(c => c.Special, StringComparer.Ordinal)
                .Select((c, rank) => c with { Rank = rank }),
        ];
    }

    /// <summary>One modern location a place is identified with, or null where the gazetteer gives it no score.</summary>
    private static Candidate? Site(
        JsonProperty association, JsonElement identifications, Dictionary<string, Modern> modern)
    {
        var value = association.Value;
        if (value.ValueKind != JsonValueKind.Object
            || !value.TryGetProperty("score", out var scored)
            || scored.ValueKind != JsonValueKind.Number
            || !value.TryGetProperty("identification_ids", out var ids)
            || ids.ValueKind != JsonValueKind.Array
            || ids.GetArrayLength() == 0)
        {
            return null;
        }

        var score = scored.GetInt32();
        var name = Text(value, "name");
        var pair = ids[0];
        var at = pair[0].GetInt32();
        var index = pair[1].GetInt32();
        if (at >= identifications.GetArrayLength()
            || !identifications[at].TryGetProperty("resolutions", out var resolutions)
            || index >= resolutions.GetArrayLength())
        {
            return Unread();
        }

        var identification = identifications[at];
        var resolution = resolutions[index];
        if (Text(resolution, "lonlat") is not { } stated
            || Text(resolution, "lonlat_type") is not { } type
            || !Kinds.TryGetValue(type, out var kind)
            || Text(resolution, "modern_basis_id") is not { } basis
            || !modern.TryGetValue(basis, out var credit))
        {
            return Unread();
        }

        var withheld = credit.CreditedToOpenStreetMap(Role(type))
            ? "openstreetmap"
            : credit.Independent is null && CommercialMaps.Contains(credit.CoordinatesSource)
                ? "commercial"
                : null;
        var point = withheld is null ? Point(credit.Independent ?? stated) : null;

        return new Candidate(
            0,
            score,
            basis,
            name,
            point?.Longitude,
            point?.Latitude,
            point is null ? null : kind,
            Number(resolution, "geometry_radius_meters") ?? Number(identification, "geometry_radius_meters"),
            Text(resolution, "type"),
            Text(identification, "modifier") is { } modifier ? Relations.GetValueOrDefault(modifier) : null,
            Text(identification, "id_source") == "ancient" ? Text(identification, "id") : null,
            null,
            withheld,
            Grounds(identification),
            point is null ? null : credit.CoordinatesSource);

        // A site the gazetteer scores but whose point cannot be read is still its answer, without a point.
        Candidate Unread() => new(
            0, score, association.Name, name, null, null, null, null, null, null, null, null, null, [], null);
    }

    /// <summary>
    /// On whose authority an identification is made, the gazetteer's <c>authority_</c> tags without
    /// the prefix, the most cited first.
    /// </summary>
    private static List<string> Grounds(JsonElement identification) =>
        identification.TryGetProperty("votes", out var votes)
        && votes.ValueKind == JsonValueKind.Object
        && votes.TryGetProperty("tags", out var tags)
        && tags.ValueKind == JsonValueKind.Object
            ?
            [
                .. tags.EnumerateObject()
                    .Where(tag => tag.Name.StartsWith(AuthorityTag, StringComparison.Ordinal)
                                  && tag.Value.ValueKind == JsonValueKind.Number && tag.Value.GetInt32() > 0)
                    .OrderByDescending(tag => tag.Value.GetInt32())
                    .ThenBy(tag => tag.Name, StringComparer.Ordinal)
                    .Select(tag => tag.Name[AuthorityTag.Length..]),
            ]
            : [];

    /// <summary>The identification's score for current scholarship, the one its sites are ranked by.</summary>
    private static int TimeTotal(JsonElement identification) =>
        identification.TryGetProperty("score", out var score)
        && score.ValueKind == JsonValueKind.Object
        && Number(score, "time_total") is { } total
            ? total
            : 0;

    private static int? Number(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt32(out var number)
            ? number
            : null;

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
