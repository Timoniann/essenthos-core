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
            AppDbContext db,
            CancellationToken cancellationToken) =>
            Results.Ok(await Map(db, language, cancellationToken, prose)));
    }

    /// <summary>
    /// Every located place, alphabetical, with the number of verses that name it so a map can size
    /// or filter its marks.
    /// </summary>
    /// <param name="prose">The language the words of a distinguisher are said in; the names' where not given.</param>
    internal static async Task<PlaceMapResponse> Map(
        AppDbContext db,
        string? language = null,
        CancellationToken cancellationToken = default,
        string? prose = null)
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

        return new PlaceMapResponse(
            places.Count,
            [.. places.Select(p => p.Source).Distinct().Select(Datasets.Of).OfType<string>()],
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
            ]);
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
                .ToDictionaryAsync(e => e.Slug, e => e.Said, cancellationToken)
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
