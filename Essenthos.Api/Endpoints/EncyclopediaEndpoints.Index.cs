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

            var entities = db.Entities.AsQueryable();

            if (!OfKind(ref entities, kind, out var refusal))
            {
                return refusal!;
            }

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
            var page = named
                .Select(row => ours.TryGetValue(row.Slug, out var own)
                    ? summaries[row.Slug] with
                    {
                        Distinguisher = own.Line,
                        LocalName = row.LocalName,
                        LocalDistinguisher = own.Line is null ? null : lines.GetValueOrDefault(row.Id),
                    }
                    : summaries[row.Slug] with
                    {
                        LocalName = row.LocalName,
                        LocalDistinguisher = lines.GetValueOrDefault(row.Id),
                    })
                .ToList();

            var described = await Descriptors.Of(
                db, page.Select(e => e.Slug), words, cancellationToken, language);
            var pictured = await ImageEndpoints.Leading(
                db, slugs, cancellationToken, settings.Is(SiteSettings.GeneratedImages));

            return Results.Ok(new EntityListResponse(
                total,
                [
                    .. page.Select(e => e with
                    {
                        Descriptor = described.GetValueOrDefault(e.Slug),
                        Thumbnail = pictured.GetValueOrDefault(e.Slug),
                    }),
                ]));
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
            var entities = db.Entities.AsQueryable();
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
}
