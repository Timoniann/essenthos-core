using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Endpoints;

internal static partial class EncyclopediaEndpoints
{
    /// <summary>Every located place, for the map.</summary>
    private static void MapPlaces(IEndpointRouteBuilder routes)
    {
        // Every place that has a point, in one payload small enough to draw them all on one map:
        // about 1,300 rows of a slug, a name, two numbers and two words. The paged index cannot do
        // this without fourteen round trips, and a map is useless until it has all of them.
        routes.MapGet("/places/map", async (
            [FromQuery] string? language,
            [FromQuery] string? prose,
            [FromQuery] bool? variants,
            AppDbContext db,
            CancellationToken cancellationToken) =>
            Results.Ok(await Map(db, language, cancellationToken, prose, variants ?? false)));

        // How many of the map's places a chapter names: the reader's link to the map under a chapter
        // says so, and fetching the whole map to count them was most of what a chapter downloaded.
        routes.MapGet("/places/map/{book}/{chapter:int}", async (
            string book,
            int chapter,
            AppDbContext db,
            CancellationToken cancellationToken) =>
            BookReferences.ResolveOrdinal(book) is { } ordinal
                ? Results.Ok(new ChapterPlacesResponse(await LocatedIn(db, ordinal, chapter, cancellationToken)))
                : ApiResults.NotFound(BookReferences.FormatHint(book)));
    }

    /// <summary>
    /// The places with a point that a chapter names: those whose <see cref="PlacePointResponse.Chapters"/>
    /// on the map hold it, counted without drawing the map.
    /// </summary>
    internal static Task<int> LocatedIn(AppDbContext db, int book, int chapter, CancellationToken cancellationToken) =>
        db.EntityVerses.Shown()
            .Where(v => v.CanonicalBook == book && v.CanonicalChapter == chapter)
            .Where(v => db.PlaceLocations.Any(l => l.EntityId == v.EntityId))
            .Select(v => v.EntityId)
            .Distinct()
            .CountAsync(cancellationToken);

    /// <summary>
    /// Every located place, alphabetical, with the number of verses that name it so a map can size
    /// or filter its marks.
    /// </summary>
    /// <param name="prose">The language the words of a distinguisher are said in; the names' where not given.</param>
    /// <param name="variants">Whether the other sites proposed for each place come too.</param>
    internal static async Task<PlaceMapResponse> Map(
        AppDbContext db,
        string? language = null,
        CancellationToken cancellationToken = default,
        string? prose = null,
        bool variants = false)
    {
        var places = await db.PlaceLocations
            .OrderBy(l => l.Entity!.Name).ThenBy(l => l.Entity!.Slug)
            .Select(l => new
            {
                l.EntityId,
                l.Entity!.Slug,
                l.Entity.Name,
                l.Entity.PlaceKind,
                l.Longitude,
                l.Latitude,
                l.Kind,
                l.Score,
                l.Source,
                l.CoordinatesDataset,
                References = l.Entity.Verses
                    .AsQueryable().Where(ShownVerses.IsShown)
                    .Select(v => (v.CanonicalBook * BookStride) + (v.CanonicalChapter * ChapterStride)
                                 + v.CanonicalVerse)
                    .Distinct().Count(),
            })
            .ToListAsync(cancellationToken);

        // The chapters in a second query: EF cannot put a distinct list inside the projection above,
        // and one row per place and chapter is a few thousand small rows.
        var chapters = (await db.EntityVerses.Shown()
                .Where(v => db.PlaceLocations.Any(l => l.EntityId == v.EntityId))
                .Select(v => new { v.Entity!.Slug, Chapter = (v.CanonicalBook * ChapterStride) + v.CanonicalChapter })
                .Distinct()
                .ToListAsync(cancellationToken))
            .GroupBy(row => row.Slug)
            .ToDictionary(group => group.Key, group => group.Select(row => row.Chapter).Order().ToList());

        var ids = places.Select(p => p.EntityId).ToList();
        var local = await EntityNames.Of(db, ids, language, cancellationToken);
        var bySource = await EntityNames.NamedOnlyBySource(db, ids, language, local, cancellationToken);
        var told = await Distinguished(
            db,
            [.. places.Select(p => (p.Slug, Shown: local.GetValueOrDefault(p.EntityId) ?? p.Name))],
            ReaderLanguages.Prose(prose, language),
            language,
            cancellationToken);

        var (proposed, unlocated) = variants
            ? await Variants(db, [.. ids], language, cancellationToken)
            : (null, null);

        // Every candidate's coordinates are counted, not only the points': a ring on the map is drawn
        // where its source measured it too.
        var measured = await db.PlaceCandidates
            .Where(c => c.CoordinatesDataset != null && c.Longitude != null)
            .Select(c => c.CoordinatesDataset!)
            .Distinct()
            .ToListAsync(cancellationToken);

        return new PlaceMapResponse(
            places.Count,
            [
                .. places.Select(p => p.Source).Distinct().Select(Datasets.Of).OfType<string>()
                    .Concat(places.Select(p => p.CoordinatesDataset).OfType<string>())
                    .Concat(measured)
                    .Distinct(StringComparer.Ordinal),
            ],
            [
                .. places.Select(p => new PlacePointResponse(
                    p.Slug, p.Name, p.Longitude, p.Latitude, p.Kind, p.Score / ScoreScale, p.References,
                    chapters.GetValueOrDefault(p.Slug) ?? [])
                {
                    PlaceKind = p.PlaceKind,
                    LocalName = local.GetValueOrDefault(p.EntityId),
                    NameFromSource = bySource.Contains(p.EntityId),
                    Distinguisher = told.GetValueOrDefault(p.Slug),
                }),
            ])
        {
            Variants = proposed,
            Unlocated = unlocated,
        };
    }

    /// <summary>
    /// Every site proposed for a place besides its likeliest one, where the terms let its point be
    /// shown, and the places that are on the map only through such sites.
    /// </summary>
    /// <param name="located">The places that have a point of their own.</param>
    private static async Task<(IList<PlaceVariantResponse>?, IList<PlaceUnlocatedResponse>?)> Variants(
        AppDbContext db, HashSet<int> located, string? language, CancellationToken cancellationToken)
    {
        var rows = await db.PlaceCandidates
            .Where(c => c.Rank > 0 && c.Longitude != null && c.Latitude != null && c.Kind != null)
            .OrderBy(c => c.Entity!.Slug).ThenBy(c => c.Rank)
            .Select(c => new
            {
                c.EntityId,
                c.Entity!.Slug,
                c.Site,
                Lon = c.Longitude!.Value,
                Lat = c.Latitude!.Value,
                Kind = c.Kind!,
                c.Score,
                c.RadiusMeters,
            })
            .ToListAsync(cancellationToken);

        var others = rows.Select(r => r.EntityId).Where(id => !located.Contains(id)).Distinct().ToList();
        var places = await db.Entities
            .Where(e => others.Contains(e.Id))
            .OrderBy(e => e.Name).ThenBy(e => e.Slug)
            .Select(e => new
            {
                e.Id,
                e.Slug,
                e.Name,
                e.PlaceKind,
                References = e.Verses
                    .AsQueryable().Where(ShownVerses.IsShown)
                    .Select(v => (v.CanonicalBook * BookStride) + (v.CanonicalChapter * ChapterStride)
                                 + v.CanonicalVerse)
                    .Distinct().Count(),
            })
            .ToListAsync(cancellationToken);
        var chapters = (await db.EntityVerses.Shown()
                .Where(v => others.Contains(v.EntityId))
                .Select(v => new { v.EntityId, Chapter = (v.CanonicalBook * ChapterStride) + v.CanonicalChapter })
                .Distinct()
                .ToListAsync(cancellationToken))
            .GroupBy(row => row.EntityId)
            .ToDictionary(group => group.Key, group => group.Select(row => row.Chapter).Order().ToList());
        var local = await EntityNames.Of(db, others, language, cancellationToken);
        var bySource = await EntityNames.NamedOnlyBySource(db, others, language, local, cancellationToken);

        return (
            [
                .. rows.Select(r => new PlaceVariantResponse(r.Slug, r.Site, r.Lon, r.Lat, r.Kind, r.Score / ScoreScale)
                {
                    RadiusKm = r.RadiusMeters / 1000.0,
                }),
            ],
            [
                .. places.Select(p => new PlaceUnlocatedResponse(
                    p.Slug, p.Name, p.References, chapters.GetValueOrDefault(p.Id) ?? [])
                {
                    PlaceKind = p.PlaceKind,
                    LocalName = local.GetValueOrDefault(p.Id),
                    NameFromSource = bySource.Contains(p.Id),
                }),
            ]);
    }

    /// <summary>
    /// Every answer a place's gazetteer records to where it was, the likeliest first, with the
    /// ancient place an answer takes it to be named in the language asked for. Null where it records
    /// none.
    /// </summary>
    internal static async Task<IList<PlaceCandidateResponse>?> Candidates(
        AppDbContext db, int entityId, string? language, CancellationToken cancellationToken)
    {
        var rows = await db.PlaceCandidates
            .Where(c => c.EntityId == entityId)
            .OrderBy(c => c.Rank)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        if (rows.Count == 0)
        {
            return null;
        }

        // The ancient place an answer passes through is found by the gazetteer's identifier, as the
        // place itself was; of several records split from one entry, the first stands for them.
        var throughIds = rows.Select(r => r.ThroughOpenBibleId).OfType<string>().Distinct().ToList();
        var through = throughIds.Count == 0
            ? []
            : (await db.Entities
                .Where(e => e.Kind == EntityKind.Place && e.OpenBibleId != null
                            && throughIds.Contains(e.OpenBibleId) && e.Id != entityId)
                .Select(e => new { e.Id, e.Slug, e.Kind, e.Name, OpenBibleId = e.OpenBibleId! })
                .ToListAsync(cancellationToken))
            .GroupBy(e => e.OpenBibleId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.MinBy(e => e.Id)!, StringComparer.Ordinal);
        var names = through.Count == 0
            ? []
            : await EntityNames.Of(db, [.. through.Values.Select(e => e.Id)], language, cancellationToken);

        return
        [
            .. rows.Select(r => new PlaceCandidateResponse(
                r.Site,
                r.Longitude,
                r.Latitude,
                r.Kind,
                r.RadiusMeters / 1000.0,
                r.SiteType,
                r.Relation,
                r.Special,
                r.Withheld is not null,
                r.Score / ScoreScale,
                r.Rank == 0,
                r.Grounds)
            {
                CoordinatesSource = r.CoordinatesSource,
                CoordinatesBy = r.CoordinatesDataset,
                Through = r.ThroughOpenBibleId is { } id && through.TryGetValue(id, out var place)
                    ? new EntityTribeResponse(
                        place.Slug, EnumSpelling.Of(place.Kind), place.Name, names.GetValueOrDefault(place.Id))
                    : null,
            }),
        ];
    }

    /// <summary>
    /// A few words telling each place from another of the same name, for the places that share one:
    /// Antioch <em>in Pisidia</em>, <em>місто в Пісідії</em>. The first clause of our own line in the
    /// reader's language; in English, failing that, the source's distinguisher or what the place is
    /// called today. Nothing where the language has neither, rather than English words on another
    /// language's map.
    /// </summary>
    private static async Task<Dictionary<string, string>> Distinguished(
        AppDbContext db,
        IReadOnlyList<(string Slug, string Shown)> places,
        string? words,
        string? names,
        CancellationToken cancellationToken)
    {
        var shared = places
            .GroupBy(p => p.Shown, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .SelectMany(group => group.Select(p => p.Slug))
            .ToList();
        if (shared.Count == 0)
        {
            return [];
        }

        var lines = await Descriptors.Of(db, shared, words, cancellationToken, names);
        var english = EntityNames.Local(words) is null;
        var sourced = english
            ? await db.Entities
                .Where(e => shared.Contains(e.Slug))
                .Select(e => new { e.Slug, Said = e.Distinguisher ?? e.ModernEquivalent })
                .ToDictionaryAsync(e => e.Slug, e => PlaceToday.WithoutCatalogueNumbers(e.Said), cancellationToken)
            : [];

        var told = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var slug in shared)
        {
            var clause = lines.GetValueOrDefault(slug) is { } line ? FirstClause(line) : null;
            if ((clause ?? sourced.GetValueOrDefault(slug)) is { Length: > 0 } said)
            {
                told[slug] = said;
            }
        }

        return told;
    }

    /// <summary>A line's words up to its first separator: <em>місто в Пісідії</em> of a longer line.</summary>
    private static string? FirstClause(EntityDescriptorResponse line)
    {
        var first = line.Parts
            .TakeWhile(part => part.Text != DescriptorPhrasings.Separator)
            .Select(part => part.Text);
        var said = string.Concat(first).Trim();
        return said.Length > 0 ? said : null;
    }
}
