using Essenthos.Core.Configuration;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Endpoints;

internal static partial class EncyclopediaEndpoints
{
    /// <summary>The index of entities, its letters, how far each layer reaches, and the family a tree is drawn from.</summary>
    private static void MapEntityIndex(IEndpointRouteBuilder routes)
    {
        routes.MapGet("/entities", async (
            [FromQuery] string? q,
            [FromQuery] string? kind,
            [FromQuery] string? language,
            [FromQuery] string? prose,
            [FromQuery] string? sort,
            [FromQuery] string? letter,
            [FromQuery] int? skip,
            [FromQuery] int? take,
            AppDbContext db,
            SiteSettingsFile settings,
            CancellationToken cancellationToken) =>
        {
            if (sort is { Length: > 0 } && !Sorts.Contains(sort))
            {
                return Results.BadRequest(new ProblemResponse(
                    $"\"{sort}\" is not an order for this index. Try {string.Join(", ", Sorts)}."));
            }

            var entities = Listed(db.Entities);

            if (!OfKind(ref entities, kind, out var refusal))
            {
                return refusal!;
            }

            var near = false;
            if (q is { Length: > 0 })
            {
                // Every name the entity is called by, in every language and every text — Peter is
                // Simon, Cephas and Simon Bar-Jonah, Aaron is Аарон and Aarón, and a search that
                // only reads the headword finds each of them under one name of many.
                //
                // Asked first and on its own: the planner cannot estimate how many rows a disjunction
                // of five name lookups keeps, guesses nearly all of them, and prices the ordering
                // below high enough to compile it — which costs more than the query. Handed the ids,
                // it knows.
                var matched = await EntityNames.Matching(db, entities, q).Select(e => e.Id).ToListAsync(cancellationToken);
                if (matched.Count == 0)
                {
                    matched = await EntityNames.Near(db, entities, q, cancellationToken);
                    near = matched.Count > 0;
                }

                entities = db.Entities.Where(e => matched.Contains(e.Id));
            }

            var localised = EntityNames.Localised(db, entities, language);

            if (letter is { Length: > 0 })
            {
                if (!OneLetter(letter, out var initial, out refusal))
                {
                    return refusal!;
                }

                localised = UnderLetter(localised, initial);
            }

            var total = await localised.CountAsync(cancellationToken);
            var named = await Ordered(db, localised, sort, q, language)
                .Skip(Math.Max(0, skip ?? 0))
                .Take(Math.Clamp(take ?? 40, 1, MostPerPage))
                .Select(l => new { l.Entity.Id, l.Entity.Slug, l.LocalName })
                .ToListAsync(cancellationToken);

            // The summaries in a second read, by slug: the projection is an expression the provider
            // cannot put beside the name the first read ordered by.
            var slugs = named.Select(row => row.Slug).ToList();
            var summaries = await db.Entities
                .Where(e => slugs.Contains(e.Slug))
                .Select(Summary)
                .ToDictionaryAsync(row => row.Slug, cancellationToken);
            var words = ReaderLanguages.Prose(prose, language);
            var lines = await EntityDistinguishers.Of(db, [.. named.Select(row => row.Id)], words, cancellationToken);
            var ours = await OursOnlyRecords.Among(db, slugs, cancellationToken);
            var renamed = await RenamedHeadings.Of(db, [.. named.Select(row => row.Id)], language, cancellationToken);
            var headed = named.ToDictionary(
                row => row.Id,
                row => renamed.TryGetValue(row.Id, out var heads)
                    ? summaries[row.Slug] with { Name = heads.Name, LocalName = heads.LocalName, AlsoCalled = heads.AlsoCalled }
                    : summaries[row.Slug] with { LocalName = row.LocalName });
            var foundAs = q is { Length: > 0 }
                ? await EntityNames.FoundAs(
                    db, headed.ToDictionary(row => row.Key, row => row.Value.LocalName ?? row.Value.Name), q, cancellationToken, renamed)
                : [];
            var page = named
                .Select(row => ours.TryGetValue(row.Slug, out var own)
                    ? headed[row.Id] with
                    {
                        Distinguisher = own.Line,
                        LocalDistinguisher = own.Line is null ? null : lines.GetValueOrDefault(row.Id),
                        Matched = foundAs.GetValueOrDefault(row.Id),
                    }
                    : headed[row.Id] with
                    {
                        LocalDistinguisher = lines.GetValueOrDefault(row.Id),
                        Matched = foundAs.GetValueOrDefault(row.Id),
                    })
                .ToList();

            var described = await Descriptors.Of(
                db, page.Select(e => e.Slug), words, cancellationToken, language);
            var pictured = await ImageEndpoints.Leading(
                db, slugs, cancellationToken, settings.Is(SiteSettings.GeneratedImages));
            var undescribed = named
                .Where(row => !described.ContainsKey(row.Slug)
                              && page.First(e => e.Slug == row.Slug) is { Distinguisher: null, LocalDistinguisher: null })
                .ToDictionary(row => row.Id, row => row.Slug);
            var first = (await FirstVerses(db, undescribed.Keys, cancellationToken))
                .ToDictionary(pair => undescribed[pair.Key], pair => pair.Value);

            return Results.Ok(new EntityListResponse(
                total,
                [
                    .. page.Select(e => e with
                    {
                        Descriptor = described.GetValueOrDefault(e.Slug),
                        Thumbnail = pictured.GetValueOrDefault(e.Slug),
                        FirstVerse = first.GetValueOrDefault(e.Slug),
                    }),
                ])
            {
                Near = near,
            });
        });

        // How far each layer of the encyclopedia actually reaches, measured rather than declared.
        //
        // The place layer states references for Genesis and Exodus and for nothing after them, so
        // Jerusalem's page reports one verse for a city the text names hundreds of times. A page
        // that shows the number alone reads as a claim about the text; a page that can also say
        // which books the layer covers reads as what it is, which is a claim about the dataset.
        //
        // Its own endpoint rather than a field on every entity: it is one aggregate over the whole
        // table, it is the same answer for every entity of a kind, and it costs about 20ms — which
        // is nothing once a session and a great deal on each of a hundred pages.
        routes.MapGet("/entities/coverage", async (AppDbContext db, CancellationToken cancellationToken) =>
            Results.Ok(await Coverage(db, cancellationToken)));

        // Which letters of the A to Z index hold anything, and how much, so a client can grey out
        // the empty ones before a reader presses them. Takes the index's own kind filter.
        routes.MapGet("/entities/letters", async (
            [FromQuery] string? kind,
            [FromQuery] string? language,
            AppDbContext db,
            CancellationToken cancellationToken) =>
        {
            var entities = Listed(db.Entities);
            return OfKind(ref entities, kind, out var refusal)
                ? Results.Ok(await Letters(EntityNames.Localised(db, entities, language), language, cancellationToken))
                : refusal!;
        });

        // Many people at once, with only what a family tree is drawn from. A tree grows a
        // generation at a time, and one request per generation beats one entity page per person.
        routes.MapGet("/entities/family", async (
            [FromQuery] string? slugs,
            [FromQuery] string? language,
            [FromQuery] string? prose,
            AppDbContext db,
            SiteSettingsFile settings,
            CancellationToken cancellationToken) =>
            FamilyEndpoints.Requested(slugs) is { } named
                ? Results.Ok(await FamilyEndpoints.Family(
                    db, named, language, cancellationToken, settings.Is(SiteSettings.GeneratedImages), prose))
                : Results.BadRequest(new ProblemResponse(
                    $"Name between 1 and {FamilyEndpoints.MostPeople} people, as slugs=moses,aaron.")));
    }

    /// <summary>
    /// The first verse that names each of these entities, in canonical order: what tells two records of
    /// one name apart in a list where neither has a line of its own.
    /// </summary>
    internal static async Task<Dictionary<int, VerseRefResponse>> FirstVerses(
        AppDbContext db, IEnumerable<int> entities, CancellationToken cancellationToken)
    {
        var ids = entities.ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        var firsts = await db.EntityVerses
            .Where(v => ids.Contains(v.EntityId))
            .Where(ShownVerses.IsShown)
            .GroupBy(v => v.EntityId)
            .Select(g => new
            {
                g.Key,
                First = g.Min(v => (v.CanonicalBook * BookStride) + (v.CanonicalChapter * ChapterStride) + v.CanonicalVerse),
            })
            .ToListAsync(cancellationToken);

        return firsts.ToDictionary(
            row => row.Key,
            row =>
            {
                var book = row.First / BookStride;
                return new VerseRefResponse(
                    book, BookReferences.Name(book), BookReferences.Slug(book),
                    row.First % BookStride / ChapterStride, row.First % ChapterStride);
            });
    }
}
