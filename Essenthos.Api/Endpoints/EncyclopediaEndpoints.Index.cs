using Essenthos.Core.Configuration;
using Essenthos.Core.Context;
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
            [FromQuery] string? subtype,
            [FromQuery] string? language,
            [FromQuery] string? prose,
            [FromQuery] string? sort,
            [FromQuery] string? letter,
            [FromQuery] int? skip,
            [FromQuery] int? take,
            [FromQuery] string? canon,
            [FromQuery] string? tribe,
            [FromQuery] string? slugs,
            AppDbContext db,
            IServiceScopeFactory scopes,
            SiteSettingsFile settings,
            [FromServices] PlacePictures? places,
            CancellationToken cancellationToken) =>
        {
            // The cards of the records named, in the order named: the rows a page of the list holds, for a client that knows
            // which records it shows. What orders, narrows or pages a list has nothing to do here and is refused, not ignored.
            if (slugs is not null)
            {
                return new[] { q, kind, subtype, sort, letter, canon, tribe }.Any(p => !string.IsNullOrEmpty(p)) || skip is not null || take is not null
                    ? Results.BadRequest(new ProblemResponse(
                        "slugs names the records whose cards are asked for, and takes only language and prose beside it."))
                    : await Named(db, scopes, slugs, language, prose, settings, places, cancellationToken);
            }

            if (sort is { Length: > 0 } && !Sorts.Contains(sort))
            {
                return Results.BadRequest(new ProblemResponse(
                    $"\"{sort}\" is not an order for this index. Try {string.Join(", ", Sorts)}."));
            }

            var entities = Listed(db.Entities);

            // The persons of a people, every one of them: a member no verse names is still of the tribe.
            if (tribe is { Length: > 0 })
            {
                var people = await db.Entities.Where(e => e.Slug == tribe && e.Kind == EntityKind.People)
                    .Select(e => new { e.Id, e.OriginEntityId }).FirstOrDefaultAsync(cancellationToken);
                if (people is null)
                {
                    return ApiResults.NotFound($"There is no people \"{tribe}\".");
                }

                entities = Tribes.Members(db, db.Entities, await Tribes.Of(db, people.Id, people.OriginEntityId, cancellationToken));
            }

            if (!OfKind(ref entities, kind, subtype, out var refusal) || !InCanon(ref entities, canon, out refusal))
            {
                return refusal!;
            }

            NameMatches? found = null;
            if (q is { Length: > 0 })
            {
                // Every name the entity is called by, in every language and every text — Peter is
                // Simon, Cephas and Simon Bar-Jonah, Aaron is Аарон and Aarón, and a search that
                // only reads the headword finds each of them under one name of many.
                //
                // Asked first and on its own: the planner cannot estimate how many rows a disjunction
                // of five name lookups keeps, guesses nearly all of them, and prices the ordering
                // below high enough to compile it — which costs more than the query. Handed the ids,
                // it knows; and handed how well each answers, the ordering looks for no name again.
                found = await EntityNames.Find(db, entities, q, cancellationToken);
                var matched = found.Ids;
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
            var named = await Ordered(localised, sort, found, language)
                .Skip(Math.Max(0, skip ?? 0))
                .Take(Math.Clamp(take ?? 40, 1, MostPerPage))
                .Select(l => new { l.Entity.Id, l.Entity.Slug, l.LocalName })
                .ToListAsync(cancellationToken);

            var cards = await Cards(
                db, scopes, [.. named.Select(row => (row.Id, row.Slug, row.LocalName))], language, prose, q, settings, places, cancellationToken);
            return Results.Ok(new EntityListResponse(total, cards.Items)
            {
                Near = found?.Near ?? false,
                Records = cards.Records,
            });
        });

        // The tribes of Israel the people list can be narrowed to, each with how many belong to it.
        routes.MapGet("/entities/tribes", async (
            [FromQuery] string? language,
            [FromQuery] string? canon,
            AppDbContext db,
            CancellationToken cancellationToken) =>
        {
            var everyone = db.Entities.AsQueryable();
            if (!InCanon(ref everyone, canon, out var refusal))
            {
                return refusal!;
            }

            var tribes = await Tribes.Israel(db, cancellationToken);
            var ids = tribes.SelectMany(t => t.Membership.Targets).Distinct().ToList();
            var heads = await db.Entities.Where(e => ids.Contains(e.Id))
                .Select(e => new { e.Id, e.Slug, e.Kind, e.Name }).ToDictionaryAsync(e => e.Id, cancellationToken);
            var names = await EntityNames.Of(db, ids, language, cancellationToken);
            var items = new List<TribeResponse>();
            foreach (var (id, slug, membership) in tribes)
            {
                var count = await Tribes.Members(db, everyone, membership).CountAsync(cancellationToken);
                var father = membership.Targets.Single(target => target != id);
                items.Add(new TribeResponse(slug, EnumSpelling.Of(heads[id].Kind), heads[id].Name, names.GetValueOrDefault(id), count)
                {
                    Patriarch = new EntityTribeResponse(
                        heads[father].Slug, EnumSpelling.Of(heads[father].Kind), heads[father].Name, names.GetValueOrDefault(father)),
                });
            }

            return Results.Ok(new TribeListResponse(
                [.. items.Where(i => i.Members > 0).OrderBy(i => i.Patriarch?.LocalName ?? i.Patriarch?.Name, StringComparer.Ordinal)]));
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
            [FromQuery] string? subtype,
            [FromQuery] string? language,
            [FromQuery] string? canon,
            AppDbContext db,
            CancellationToken cancellationToken) =>
        {
            var entities = Listed(db.Entities);
            return OfKind(ref entities, kind, subtype, out var refusal) && InCanon(ref entities, canon, out refusal)
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

        // A person's whole family as one document, which a tree walks to any depth without asking again.
        routes.MapGet("/family/component", async (
            [FromQuery] string? of,
            [FromQuery] string? language,
            [FromQuery] string? prose,
            AppDbContext db,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(of))
            {
                return Results.BadRequest(new ProblemResponse("Name the person whose family is asked for, as of=david."));
            }

            if (await FamilyEndpoints.Component(db, of.Trim(), language, prose, cancellationToken) is not { } family)
            {
                return Results.NotFound(new ProblemResponse($"No person is called '{of}'."));
            }

            var tag = '"' + family.Hash + '"';
            context.Response.Headers.ETag = tag;
            return context.Request.Headers.IfNoneMatch.Any(asked => asked == tag)
                ? Results.StatusCode(StatusCodes.Status304NotModified)
                : Results.Text(family.Body, "application/json; charset=utf-8");
        }).KeptUntilRelease();
    }

    /// <summary>The cards of the records a client names, in the order it names them, and the slugs no record has.</summary>
    private static async Task<IResult> Named(
        AppDbContext db,
        IServiceScopeFactory scopes,
        string slugs,
        string? language,
        string? prose,
        SiteSettingsFile settings,
        PlacePictures? places,
        CancellationToken cancellationToken)
    {
        var asked = slugs.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (asked.Count is 0 or > MostPerPage)
        {
            return Results.BadRequest(new ProblemResponse($"Name between 1 and {MostPerPage} records, as slugs=moses,aaron."));
        }

        var rows = await EntityNames.Localised(db, db.Entities.Where(e => asked.Contains(e.Slug)), language)
            .Select(l => new { l.Entity.Id, l.Entity.Slug, l.LocalName })
            .ToDictionaryAsync(row => row.Slug, StringComparer.Ordinal, cancellationToken);
        var held = asked.Where(rows.ContainsKey).Select(slug => (rows[slug].Id, slug, rows[slug].LocalName)).ToList();
        var cards = await Cards(db, scopes, held, language, prose, null, settings, places, cancellationToken);
        var unknown = asked.Where(slug => !rows.ContainsKey(slug)).ToList();
        return Results.Ok(new EntityListResponse(held.Count, cards.Items)
        {
            Records = cards.Records,
            Unknown = unknown.Count > 0 ? unknown : null,
        });
    }

    /// <summary>
    /// What a row of the list shows of each record, in the order given: its summary and heading in the reader's languages,
    /// the line under its name, its picture, and the first verse that names a record nothing else tells apart. The line is
    /// the one the build stored for the pair of languages (<c>context_line</c>), its targets in the one map of records
    /// returned beside the rows.
    /// </summary>
    private static async Task<(IList<EntitySummaryResponse> Items, IDictionary<string, ContextRecordResponse>? Records)> Cards(
        AppDbContext db,
        IServiceScopeFactory scopes,
        IReadOnlyList<(int Id, string Slug, string? LocalName)> named,
        string? language,
        string? prose,
        string? q,
        SiteSettingsFile settings,
        PlacePictures? places,
        CancellationToken cancellationToken)
    {
        var slugs = named.Select(row => row.Slug).ToList();
        var ids = named.Select(row => row.Id).ToList();
        var words = ReaderLanguages.Prose(prose, language);
        var (lines, records) = await CardLines(db, scopes, named, language, words, cancellationToken);

        // The summaries in a second read, by slug: the projection is an expression the provider
        // cannot put beside the name the first read ordered by.
        var summaries = await db.Entities
            .Where(e => slugs.Contains(e.Slug))
            .Select(Summary)
            .ToDictionaryAsync(row => row.Slug, cancellationToken);
        var renamed = await RenamedHeadings.Of(db, ids, language, cancellationToken);
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
            .Select(row => headed[row.Id] with
            {
                LocalDistinguisher = lines[row.Slug].LocalDistinguisher,
                Line = lines[row.Slug].Descriptor,
                Records = lines[row.Slug].Records,
                Matched = foundAs.GetValueOrDefault(row.Id),
            })
            .ToList();

        var pictured = await ImageEndpoints.Leading(
            db, slugs, cancellationToken, settings.Is(SiteSettings.GeneratedImages), places, words);
        var undescribed = page
            .Zip(named)
            .Where(pair => pair.First is { Line: null, Distinguisher: null, LocalDistinguisher: null })
            .ToDictionary(pair => pair.Second.Id, pair => pair.Second.Slug);
        var first = (await FirstVerses(db, undescribed.Keys, cancellationToken))
            .ToDictionary(pair => undescribed[pair.Key], pair => pair.Value);

        return (
            [
                .. page.Select(e => e with
                {
                    Thumbnail = pictured.GetValueOrDefault(e.Slug),
                    FirstVerse = first.GetValueOrDefault(e.Slug),
                }),
            ],
            records.Count > 0 ? records : null);
    }

    /// <summary>
    /// A record's line under its name in a list, its own line in the language of words, and the records its line points at
    /// where it says them otherwise than the list's map does.
    /// </summary>
    private sealed record CardLine(
        ContextDescriptorResponse? Descriptor,
        string? LocalDistinguisher,
        IDictionary<string, ContextRecordResponse>? Records = null);

    /// <summary>
    /// The line under each record's name and its own line, as the build stored them for this pair of languages, in one
    /// statement. A record the build stored no line for (a language of names it holds none in, a record made since) is
    /// composed as the stored ones were, its descriptor read beside the rest on a connection of its own.
    ///
    /// <para>
    /// The list's map holds each target as the first row to point at it says it; a row that says a target otherwise keeps
    /// that target with it. A title is said of a people in the plural and of a person in the singular, so the Anakim are
    /// <em>Giants</em> beside a giant whose line says <em>Giant</em>.
    /// </para>
    /// </summary>
    private static async Task<(Dictionary<string, CardLine> Lines, SortedDictionary<string, ContextRecordResponse> Records)> CardLines(
        AppDbContext db,
        IServiceScopeFactory scopes,
        IReadOnlyList<(int Id, string Slug, string? LocalName)> named,
        string? language,
        string? words,
        CancellationToken cancellationToken)
    {
        var lines = new Dictionary<string, CardLine>(StringComparer.Ordinal);
        var pointed = new Dictionary<string, IReadOnlyDictionary<string, ContextRecordResponse>>(StringComparer.Ordinal);

        var slugs = named.Select(row => row.Slug).ToList();
        if (slugs.Count > 0 && language is { Length: > 0 } && words is { Length: > 0 })
        {
            var stored = await db.ContextLines
                .Where(l => l.Prose == words && l.Names == language && slugs.Contains(l.Slug))
                .Select(l => new { l.Slug, l.Body })
                .ToListAsync(cancellationToken);
            foreach (var row in stored)
            {
                var line = ContextLines.Read(row.Body);
                lines[row.Slug] = new CardLine(line.Descriptor, line.LocalDistinguisher);
                pointed[row.Slug] = line.Records ?? [];
            }
        }

        var missing = named.Where(row => !lines.ContainsKey(row.Slug)).ToList();
        if (missing.Count > 0)
        {
            var describing = Beside(scopes, other => Descriptors.Of(other, missing.Select(row => row.Slug), words, cancellationToken, language));
            var distinguishers = await EntityDistinguishers.Of(db, [.. missing.Select(row => row.Id)], words, cancellationToken);
            var described = await describing;
            foreach (var row in missing)
            {
                var slim = described.TryGetValue(row.Slug, out var descriptor)
                    ? ContextDescriptors.Slim(new Dictionary<string, EntityDescriptorResponse>(StringComparer.Ordinal) { [row.Slug] = descriptor })
                    : null;
                lines[row.Slug] = new CardLine(slim?.Lines.GetValueOrDefault(row.Slug), distinguishers.GetValueOrDefault(row.Id));
                pointed[row.Slug] = slim is null ? [] : new Dictionary<string, ContextRecordResponse>(slim.Records, StringComparer.Ordinal);
            }
        }

        var records = new SortedDictionary<string, ContextRecordResponse>(StringComparer.Ordinal);
        foreach (var row in named)
        {
            var own = new SortedDictionary<string, ContextRecordResponse>(StringComparer.Ordinal);
            foreach (var (target, record) in pointed[row.Slug])
            {
                if (!records.TryAdd(target, record) && !records[target].SaidAlike(record))
                {
                    own[target] = record;
                }
            }

            if (own.Count > 0)
            {
                lines[row.Slug] = lines[row.Slug] with { Records = own };
            }
        }

        return (lines, records);
    }

    /// <summary>A read on a connection of its own, to run beside what the request's own connection reads.</summary>
    private static async Task<T> Beside<T>(IServiceScopeFactory scopes, Func<AppDbContext, Task<T>> read)
    {
        await using var scope = scopes.CreateAsyncScope();
        return await read(scope.ServiceProvider.GetRequiredService<AppDbContext>());
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
