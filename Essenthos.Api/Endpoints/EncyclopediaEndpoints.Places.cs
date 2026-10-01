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
            AppDbContext db,
            CancellationToken cancellationToken) =>
            Results.Ok(await Map(db, language, cancellationToken)));
    }

    /// <summary>
    /// Every located place, alphabetical, with the number of verses that name it so a map can size
    /// or filter its marks.
    /// </summary>
    internal static async Task<PlaceMapResponse> Map(
        AppDbContext db,
        string? language = null,
        CancellationToken cancellationToken = default)
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
                    .Where(v => !v.Source.StartsWith(ShownVerses.Witness))
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

        var local = await EntityNames.Of(db, [.. places.Select(p => p.EntityId)], language, cancellationToken);

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
                }),
            ]);
    }
}
