using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Endpoints;

internal static partial class EncyclopediaEndpoints
{
    /// <summary>The verses that name an entity, a page at a time.</summary>
    private static void MapReferences(IEndpointRouteBuilder routes)
    {
        routes.MapGet("/entities/{slug}/references", async (
            string slug,
            [FromQuery] int? skip,
            [FromQuery] int? take,
            AppDbContext db,
            CancellationToken cancellationToken) =>
        {
            slug = await MergedAddresses.Current(db, slug, cancellationToken);
            var entity = await db.Entities.Where(e => e.Slug == slug).Select(e => e.Id)
                .FirstOrDefaultAsync(cancellationToken);
            if (entity == 0)
            {
                return ApiResults.NotFound($"There is no person, place or people \"{slug}\".");
            }

            // Paged by verse, not by naming. The source writes one row per naming, so Manasseh's
            // list showed the same address twice wherever the text names him twice in it — a verse
            // repeated in a list of verses, with nothing on the page saying why.
            var references = db.EntityVerses.Shown().Where(v => v.EntityId == entity);

            var total = await Addresses(references).CountAsync(cancellationToken);
            var wanted = await NamingFirst(references)
                .Skip(Math.Max(0, skip ?? 0))
                .Take(Math.Clamp(take ?? 50, 1, MostPerPage))
                .ToListAsync(cancellationToken);

            var namings = await references
                .Where(v => wanted.Contains((v.CanonicalBook * BookStride) + (v.CanonicalChapter * ChapterStride)
                                            + v.CanonicalVerse))
                .Select(v => new
                {
                    v.CanonicalBook, v.CanonicalChapter, v.CanonicalVerse, v.Label, v.Disputed, v.Source, v.Names,
                })
                .ToListAsync(cancellationToken);

            var byAddress = namings
                .GroupBy(v => (v.CanonicalBook * BookStride) + (v.CanonicalChapter * ChapterStride)
                              + v.CanonicalVerse)
                .ToDictionary(group => group.Key, group => group.ToList());

            return Results.Ok(new EntityReferenceListResponse(
                total,
                [
                    .. wanted.Where(byAddress.ContainsKey).Select(address =>
                    {
                        var at = byAddress[address];
                        return new EntityReferenceResponse(
                            new BookRefResponse(
                                at[0].CanonicalBook,
                                BookReferences.Name(at[0].CanonicalBook),
                                BookReferences.Slug(at[0].CanonicalBook)),
                            at[0].CanonicalChapter,
                            at[0].CanonicalVerse,
                            [.. at.Select(v => new EntityNamingResponse(v.Label, v.Disputed, Datasets.Of(v.Source)))],
                            at.Any(v => v.Disputed),
                            at.Any(v => v.Names),
                            ReferenceKinds.Of(at.Select(v => (v.Source, v.Names))));
                    }),
                ]));
        });
    }
}
