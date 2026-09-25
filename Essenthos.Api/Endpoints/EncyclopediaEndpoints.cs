using Essenthos.Core.Configuration;
using Essenthos.Core.Corpus;
﻿using System.Linq.Expressions;
using System.Text.RegularExpressions;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Endpoints;

/// <summary>
/// The people, places and peoples the text names, what it calls them, how they stand to one
/// another, and when the source thinks things happened to them.
///
/// A reference here is a canonical verse, not a word. That is what the data states — BibleData
/// tags verses — and stating it at the verse is honest where claiming a word would not be. The
/// word-level layer exists (STEPBible's TIPNR names a person at each occurrence with a
/// disambiguated Strong number) and is a separate load; <c>entity_name.strong_number</c> is the
/// column it will arrive through.
/// </summary>
internal static class EncyclopediaEndpoints
{
    private const int MostPerPage = 100;

    /// <summary>
    /// A verse address as one number, so that <em>how many verses</em> is a <c>DISTINCT</c> the
    /// database can do rather than a group the API has to assemble.
    ///
    /// It orders exactly as the three columns order, because no chapter reaches a thousand verses
    /// and no book a thousand chapters — Psalm 119 is 176 verses and Psalms is 150 chapters, and
    /// both stay that way.
    /// </summary>
    private const int ChapterStride = 1_000;

    private const int BookStride = 1_000_000;

    /// <summary>
    /// How many verses name an entity, how many namings they hold, and how many of those verses
    /// carry a naming the source cannot resolve.
    ///
    /// One expression, read by the entity page and by the test that measures it, because the three
    /// numbers differ — 28,226 verses under 30,105 namings — and reporting one of them under
    /// another's name is the whole defect.
    /// </summary>
    internal static readonly Expression<Func<Entity, EntityTally>> Tally =
        e => new EntityTally(
            e.Verses
                .Select(v => (v.CanonicalBook * BookStride) + (v.CanonicalChapter * ChapterStride)
                             + v.CanonicalVerse)
                .Distinct().Count(),
            e.Verses.Count,
            e.Verses
                .Where(v => v.Disputed)
                .Select(v => (v.CanonicalBook * BookStride) + (v.CanonicalChapter * ChapterStride)
                             + v.CanonicalVerse)
                .Distinct().Count());

    internal static readonly Expression<Func<Entity, EntitySummaryResponse>> Summary =
        e => new EntitySummaryResponse(
            e.Slug,
            EnumSpelling.Of(e.Kind),
            e.Name,
            e.Distinguisher,
            e.Verses
                .Select(v => (v.CanonicalBook * BookStride) + (v.CanonicalChapter * ChapterStride)
                             + v.CanonicalVerse)
                .Distinct().Count(),
            e.Verses.Count)
        {
            Subtype = e.Subtype,
        };

    /// <summary>
    /// The gazetteer scores an identification in thousandths — 500 high confidence, 1000 very high —
    /// and the API states confidence as a fraction, as it does everywhere else. The scale is not
    /// clamped, because the score is not: it runs past 1000 and below zero, and a clamp would state
    /// something the source did not.
    /// </summary>
    internal const double ScoreScale = 1000.0;

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
                    .Select(v => (v.CanonicalBook * BookStride) + (v.CanonicalChapter * ChapterStride)
                                 + v.CanonicalVerse)
                    .Distinct().Count(),
            })
            .ToListAsync(cancellationToken);

        // The chapters in a second query: EF cannot put a distinct list inside the projection above,
        // and one row per place and chapter is a few thousand small rows.
        var chapters = (await db.EntityVerses
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

    /// <summary>
    /// Every text's spellings of one entity, a text's heading first. The texts in the corpus's own
    /// order of languages and then of texts, which is the order the pane list already offers them in.
    /// </summary>
    private static async Task<List<EntityRenderingResponse>> Renderings(
        AppDbContext db,
        int entityId,
        CancellationToken cancellationToken)
    {
        var rows = await db.EntityRenderings
            .Where(r => r.EntityId == entityId)
            .Select(r => new { r.Text!.Slug, r.Text.Language, r.TextId, r.Form, r.Occurrences, r.Heading })
            .ToListAsync(cancellationToken);

        return
        [
            .. rows
                .GroupBy(r => (r.TextId, r.Slug, r.Language))
                .OrderBy(g => g.Key.Language, StringComparer.Ordinal).ThenBy(g => g.Key.TextId)
                .Select(g => new EntityRenderingResponse(
                    g.Key.Slug,
                    g.Key.Language,
                    g.Single(r => r.Heading).Form,
                    g.Sum(r => r.Occurrences),
                    [
                        .. g.OrderByDescending(r => r.Occurrences).ThenBy(r => r.Form, StringComparer.Ordinal)
                            .Select(r => new EntitySpellingResponse(r.Form, r.Occurrences)),
                    ])),
        ];
    }

    /// <summary>The addresses of a set of namings, each address once.</summary>
    internal static IQueryable<int> Addresses(IQueryable<EntityVerse> namings) =>
        namings
            .Select(v => (v.CanonicalBook * BookStride) + (v.CanonicalChapter * ChapterStride) + v.CanonicalVerse)
            .Distinct();

    /// <summary>
    /// How far each layer of the encyclopedia actually reaches, asked of the database rather than
    /// declared anywhere.
    ///
    /// A layer whose books stop short of the canon makes every count on every page of it a
    /// statement about the source and not about the text: the place layer states references for
    /// Genesis and Exodus and nothing after them, so Jerusalem reports one verse for a city the
    /// text names hundreds of times. The books are what turns that number from a wrong answer into
    /// a partial one.
    ///
    /// Four aggregates over the whole table rather than one, because the four questions are
    /// different — how many entities exist, how many are cited at all, how many verses cite them,
    /// and how many citations those verses hold — and a single query answering them together
    /// double-counts every entity named twice in a verse.
    /// </summary>
    internal static async Task<EntityCoverageResponse> Coverage(
        AppDbContext db,
        CancellationToken cancellationToken = default)
    {
        var tallies = await db.Entities
            .GroupBy(e => e.Kind)
            .Select(g => new { Kind = g.Key, Entities = g.Count(), Named = g.Count(e => e.Verses.Any()) })
            .ToListAsync(cancellationToken);

        var mentions = await db.EntityVerses
            .GroupBy(v => v.Entity!.Kind)
            .Select(g => new { Kind = g.Key, Count = g.Count() })
            .ToDictionaryAsync(row => row.Kind, row => row.Count, cancellationToken);

        var verses = await db.EntityVerses
            .Select(v => new
            {
                v.Entity!.Kind,
                Address = (v.CanonicalBook * BookStride) + (v.CanonicalChapter * ChapterStride) + v.CanonicalVerse,
            })
            .Distinct()
            .GroupBy(v => v.Kind)
            .Select(g => new { Kind = g.Key, Count = g.Count() })
            .ToDictionaryAsync(row => row.Kind, row => row.Count, cancellationToken);

        var reached = (await db.EntityVerses
                .Select(v => new { v.Entity!.Kind, v.CanonicalBook })
                .Distinct()
                .ToListAsync(cancellationToken))
            .GroupBy(b => b.Kind)
            .ToDictionary(g => g.Key, g => g.Select(b => b.CanonicalBook).Order().ToList());

        var stated = await Stated(db, cancellationToken);

        return new EntityCoverageResponse(
            BookReferences.CanonBookCount,
            [
                .. tallies
                    .Select(t =>
                    {
                        var ordinals = reached.GetValueOrDefault(t.Kind, []);
                        return new EntityLayerCoverageResponse(
                            EnumSpelling.Of(t.Kind),
                            t.Entities,
                            t.Named,
                            verses.GetValueOrDefault(t.Kind),
                            mentions.GetValueOrDefault(t.Kind),
                            new CoverageResponse(
                                ordinals.Count > 0 ? ordinals[0] : 0,
                                ordinals.Count > 0 ? ordinals[^1] : 0,
                                ordinals),
                            stated.GetValueOrDefault(t.Kind, []));
                    })
                    .OrderBy(layer => layer.Kind, StringComparer.Ordinal),
            ]);
    }

    /// <summary>
    /// Which dataset states each layer's references, and how far each one of them reaches.
    ///
    /// The place layer is two sources over one set of places, and they are not alike: one names
    /// 118 places and stops after Exodus, the other names 1,342 across the canon and joins onto
    /// the same entries wherever the two mean the same place. A layer total that adds them
    /// together is true and says nothing — this is what turns it back into two statements, each
    /// attributable to whoever made it.
    /// </summary>
    private static async Task<Dictionary<EntityKind, List<EntitySourceCoverageResponse>>> Stated(
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        var mentions = await db.EntityVerses
            .GroupBy(v => new { v.Entity!.Kind, v.Source })
            .Select(g => new { g.Key.Kind, g.Key.Source, Mentions = g.Count() })
            .ToListAsync(cancellationToken);

        var verses = (await db.EntityVerses
                .Select(v => new
                {
                    v.Entity!.Kind,
                    v.Source,
                    Address = (v.CanonicalBook * BookStride) + (v.CanonicalChapter * ChapterStride)
                              + v.CanonicalVerse,
                })
                .Distinct()
                .ToListAsync(cancellationToken))
            .GroupBy(v => (v.Kind, v.Source))
            .ToDictionary(g => g.Key, g => g.Count());

        var books = (await db.EntityVerses
                .Select(v => new { v.Entity!.Kind, v.Source, v.CanonicalBook })
                .Distinct()
                .ToListAsync(cancellationToken))
            .GroupBy(b => (b.Kind, b.Source))
            .ToDictionary(g => g.Key, g => g.Select(b => b.CanonicalBook).Order().ToList());

        return mentions
            .GroupBy(m => m.Kind)
            .ToDictionary(
                g => g.Key,
                g => g
                    .OrderByDescending(m => m.Mentions)
                    .Select(m =>
                    {
                        var ordinals = books.GetValueOrDefault((m.Kind, m.Source), []);
                        return new EntitySourceCoverageResponse(
                            Datasets.Of(m.Source),
                            m.Source,
                            verses.GetValueOrDefault((m.Kind, m.Source)),
                            m.Mentions,
                            new CoverageResponse(
                                ordinals.Count > 0 ? ordinals[0] : 0,
                                ordinals.Count > 0 ? ordinals[^1] : 0,
                                ordinals));
                    })
                    .ToList());
    }


    /// <summary>
    /// How the index is ordered. Alphabetical is the default and the only order that answers
    /// <em>where is the name I am looking for</em>; the two counts answer <em>who does this corpus
    /// have most to say about</em>, and they are different numbers — 28,226 verses under 30,105
    /// namings — so a client that offers a sort has to name which of them it used.
    ///
    /// It is a query parameter rather than something the client does to a page because the page is
    /// a hundred of four and a half thousand rows: sorting what arrived would order the first
    /// hundred alphabetical names by verse count and present that as the corpus's own ranking.
    ///
    /// Both counts descend, because the question they answer only has a useful end at the top, and
    /// both break ties on the name so that a page boundary falls in the same place twice. The name
    /// is the one shown, in the language asked for and in that language's own alphabetical order.
    /// </summary>
    private static IQueryable<LocalisedEntity> Ordered(
        AppDbContext db,
        IQueryable<LocalisedEntity> entities,
        string? sort,
        string? q,
        string? language) => sort switch
    {
        "verses" => EntityNames.Alphabetical(
            entities.OrderByDescending(l => l.Entity.Verses
                .Select(v => (v.CanonicalBook * BookStride) + (v.CanonicalChapter * ChapterStride)
                             + v.CanonicalVerse)
                .Distinct().Count()),
            language),
        "mentions" => EntityNames.Alphabetical(entities.OrderByDescending(l => l.Entity.Verses.Count), language),
        "name" => EntityNames.Alphabetical(entities, language),
        _ => q is { Length: > 0 }
            ? EntityNames.ByRelevance(db, entities, q, language)
            : EntityNames.Alphabetical(entities, language),
    };

    /// <summary>The orders <c>sort</c> accepts. Anything else is refused rather than ignored.</summary>
    private static readonly string[] Sorts = ["name", "verses", "mentions"];

    /// <summary>
    /// Whatever opens a name before its first letter — a quotation mark, an apostrophe, a digit.
    /// None of the loaded names has any today; the day one does, it is filed under its first
    /// letter rather than under a punctuation mark no index button offers.
    /// </summary>
    private const string BeforeTheFirstLetter = "^[^[:alpha:]]*";

    /// <summary>
    /// The letter a name is filed under in the A to Z index: its first letter, in capitals, and the
    /// empty string for a name with no letter in it at all. The printed name as it stands — <em>the
    /// angel of the LORD</em> files under T, where a reader scanning a list of printed names will
    /// look for it — and it is the name shown in the language asked for, so <em>Аарон</em> files under
    /// А.
    ///
    /// <para>
    /// The counts are read with this and the filter with <see cref="UnderLetter"/>, which says the
    /// same thing to Postgres as a regular expression; the provider translates no regex that
    /// returns a string, so the two are written twice and a test holds them to each other.
    /// </para>
    /// </summary>
    internal static string FiledUnder(string name)
    {
        foreach (var c in name)
        {
            if (char.IsLetter(c))
            {
                return char.ToUpperInvariant(c).ToString();
            }
        }

        return string.Empty;
    }

    /// <summary>The entities filed under one letter, whatever case it was asked in.</summary>
    internal static IQueryable<LocalisedEntity> UnderLetter(IQueryable<LocalisedEntity> entities, char letter)
    {
        var pattern = $"{BeforeTheFirstLetter}[{char.ToUpperInvariant(letter)}{char.ToLowerInvariant(letter)}]";
        return entities.Where(l => Regex.IsMatch(l.Shown, pattern));
    }

    /// <summary>
    /// How many entities each letter holds: every letter of the language's alphabet, empty ones at
    /// zero, and any other letter a name happens to open with after them — in a Ukrainian index, the
    /// Latin initials of the names the corpus holds no Ukrainian name for. The alphabet is in the
    /// response so a client can draw all of it, the empty letters greyed, without holding its own.
    ///
    /// Counted here rather than grouped in the database, because the grouping key is a string the
    /// provider cannot compute. It reads one column of five thousand short rows.
    /// </summary>
    internal static async Task<EntityLettersResponse> Letters(
        IQueryable<LocalisedEntity> entities,
        string? language = null,
        CancellationToken cancellationToken = default)
    {
        var alphabet = EntityNames.Alphabet(language);
        var counted = (await entities.Select(l => l.Shown).ToListAsync(cancellationToken))
            .GroupBy(FiledUnder)
            .ToDictionary(g => g.Key, g => g.Count());

        var letters = alphabet.Select(c => c.ToString())
            .Concat(counted.Keys
                .Where(letter => letter.Length > 0 && !alphabet.Contains(letter, StringComparison.Ordinal))
                .Order(StringComparer.Ordinal))
            .Select(letter => new EntityLetterResponse(letter, counted.GetValueOrDefault(letter)))
            .ToList();

        return new EntityLettersResponse(letters.Sum(l => l.Count), letters);
    }

    /// <summary>
    /// The entities of one kind, or a refusal naming the kinds there are. Shared by the index and
    /// its letter counts, so the two accept exactly the same words.
    /// </summary>
    private static bool OfKind(ref IQueryable<Entity> entities, string? kind, out IResult? refusal)
    {
        refusal = null;
        if (kind is not { Length: > 0 })
        {
            return true;
        }

        if (kind is not ("person" or "place" or "people" or "term" or "title" or "object" or "observance"))
        {
            refusal = Results.BadRequest(new ProblemResponse(
                $"\"{kind}\" is not a kind of entity. Try person, place, people, term, title, object or observance."));
            return false;
        }

        var wanted = EnumSpelling.ToEntityKind(kind);
        entities = entities.Where(e => e.Kind == wanted);
        return true;
    }

    /// <summary>
    /// The one letter <c>letter</c> names, or a refusal saying what it takes. A word or a digit is
    /// refused rather than read as its first character, because an index that quietly answers a
    /// different question from the one asked is harder to notice than one that says no.
    /// </summary>
    private static bool OneLetter(string letter, out char wanted, out IResult? refusal)
    {
        wanted = default;
        refusal = null;

        if (letter.Length != 1 || !char.IsLetter(letter[0]))
        {
            refusal = Results.BadRequest(new ProblemResponse(
                $"\"{letter}\" is not one letter. Ask for a single letter, such as letter=A."));
            return false;
        }

        wanted = letter[0];
        return true;
    }

    /// <summary>A passage as the wire carries it: the book once, and the span in its own numbers.</summary>
    internal static PassageResponse Passage(EntityPassage passage) =>
        new(
            passage.Role,
            new BookRefResponse(
                passage.CanonicalBook,
                BookReferences.Name(passage.CanonicalBook),
                BookReferences.Slug(passage.CanonicalBook)),
            passage.CanonicalChapter,
            passage.CanonicalVerse,
            passage.EndChapter,
            passage.EndVerse,
            passage.Note);

    public static void MapEncyclopedia(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/entities", async (
            [FromQuery] string? q,
            [FromQuery] string? kind,
            [FromQuery] string? language,
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
                .Select(l => new { l.Entity.Slug, l.LocalName })
                .ToListAsync(cancellationToken);

            // The summaries in a second read, by slug: the projection is an expression the provider
            // cannot put beside the name the first read ordered by.
            var slugs = named.Select(row => row.Slug).ToList();
            var summaries = await db.Entities
                .Where(e => slugs.Contains(e.Slug))
                .Select(Summary)
                .ToDictionaryAsync(row => row.Slug, cancellationToken);
            var page = named.Select(row => summaries[row.Slug] with { LocalName = row.LocalName }).ToList();

            var described = await Descriptors.Of(
                db, page.Select(e => e.Slug), language, cancellationToken);
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
            AppDbContext db,
            SiteSettingsFile settings,
            CancellationToken cancellationToken) =>
            FamilyEndpoints.Requested(slugs) is { } named
                ? Results.Ok(await FamilyEndpoints.Family(
                    db, named, language, cancellationToken, settings.Is(SiteSettings.GeneratedImages)))
                : Results.BadRequest(new ProblemResponse(
                    $"Name between 1 and {FamilyEndpoints.MostPeople} people, as slugs=moses,aaron.")));

        routes.MapGet("/entities/{slug}", async (
            string slug,
            [FromQuery] string? language,
            AppDbContext db,
            SiteSettingsFile settings,
            CancellationToken cancellationToken) =>
        {
            slug = await MergedAddresses.Current(db, slug, cancellationToken);
            var entity = await db.Entities
                .Where(e => e.Slug == slug)
                .Select(e => new
                {
                    e.Id,
                    e.Slug,
                    e.Kind,
                    e.Name,
                    e.Distinguisher,
                    e.Sex,
                    e.Tribe,
                    e.PlaceKind,
                    e.Subtype,
                    e.ModernEquivalent,
                    e.Notes,
                    e.OpenBibleId,
                    Origin = e.Origin == null
                        ? null
                        : new EntityOriginResponse(
                            e.Origin.Slug,
                            EnumSpelling.Of(e.Origin.Kind),
                            e.Origin.Name,
                            e.Origin.Distinguisher),
                    e.Source,
                    Location = e.Location == null
                        ? null
                        : new EntityLocationResponse(
                            e.Location.Longitude,
                            e.Location.Latitude,
                            e.Location.Kind,
                            e.Location.Score / ScoreScale),
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (entity is null)
            {
                return ApiResults.NotFound($"There is no person, place or people \"{slug}\".");
            }

            var tally = await db.Entities
                .Where(e => e.Id == entity.Id)
                .Select(Tally)
                .SingleAsync(cancellationToken);

            var names = await db.EntityNames
                .Where(n => n.EntityId == entity.Id)
                .Select(n => new
                {
                    n.Label, n.Hebrew, n.HebrewTransliterated, n.Greek, n.GreekTransliterated,
                    n.Meaning, n.HebrewStrongNumber, n.GreekStrongNumber, n.Kind,
                })
                .ToListAsync(cancellationToken);

            var related = await Relationships.Of(db, entity.Id, language, cancellationToken);
            var own = await Relationships.Forms(db, [entity.Id], language, cancellationToken);

            var events = await InOrder(db.Events.Where(e => e.EntityId == entity.Id))
                .Select(Rows)
                .ToListAsync(cancellationToken);
            var placesNamed = await PlacesNamed(db, events.Select(row => EventLocation.Of(row.Event)), cancellationToken);

            // Who says the text names this entity where it does — which is not always whoever
            // supplied the entity. A place can come from one dataset and be referenced by another,
            // and a page reporting 955 verses under the first one's credit would attribute the
            // second one's work to it.
            var stated = await db.EntityVerses
                .Where(v => v.EntityId == entity.Id)
                .GroupBy(v => v.Source)
                .Select(g => new
                {
                    Source = g.Key,
                    Mentions = g.Count(),
                    References = g
                        .Select(v => (v.CanonicalBook * BookStride) + (v.CanonicalChapter * ChapterStride)
                                     + v.CanonicalVerse)
                        .Distinct()
                        .Count(),
                })
                .OrderByDescending(row => row.Mentions)
                .ToListAsync(cancellationToken);

            // What established the record, and who else it might be. Both are empty for every
            // record a dataset supplied, which is nearly all of them — the cost is two indexed
            // reads that come back with nothing, and the gain is that a record this corpus wrote
            // for itself can never reach a reader looking like one it merely carried.
            var established = await db.EntityClaims
                .Where(c => c.EntityId == entity.Id)
                .Select(c => new { c.Method, c.Confidence, c.Source, c.Note })
                .ToListAsync(cancellationToken);

            var bearers = await db.TitleBearers
                .Where(b => b.TitleEntityId == entity.Id)
                .OrderBy(b => b.CanonicalBook).ThenBy(b => b.CanonicalChapter).ThenBy(b => b.CanonicalVerse)
                .Select(b => new
                {
                    b.Bearer!.Slug, b.Bearer.Kind, b.Bearer.Name, b.Bearer.Distinguisher,
                    b.CanonicalBook, b.CanonicalChapter, b.CanonicalVerse, b.Note,
                })
                .ToListAsync(cancellationToken);

            var titles = await db.TitleBearers
                .Where(b => b.BearerEntityId == entity.Id)
                .OrderBy(b => b.Title!.Name)
                .Select(b => new
                {
                    b.Title!.Slug, b.Title.Kind, b.Title.Name, b.Title.Distinguisher,
                    b.CanonicalBook, b.CanonicalChapter, b.CanonicalVerse, b.Note,
                })
                .ToListAsync(cancellationToken);

            var passages = await db.EntityPassages
                .Where(p => p.EntityId == entity.Id)
                .OrderBy(p => p.Ordinal)
                .ToListAsync(cancellationToken);

            var times = await db.ObservanceTimes
                .Where(o => o.EntityId == entity.Id)
                .OrderBy(o => o.Id)
                .ToListAsync(cancellationToken);

            var alternatives = await db.EntityAlternatives
                .Where(a => a.EntityId == entity.Id)
                .Select(a => new EntityAlternativeResponse(
                    a.Alternative == null ? null : a.Alternative.Slug,
                    a.Alternative == null ? null : a.Alternative.Name,
                    a.Alternative == null ? null : a.Alternative.Distinguisher,
                    a.Describes,
                    a.Reason,
                    a.Source))
                .ToListAsync(cancellationToken);

            return Results.Ok(new EntityResponse(
                entity.Slug,
                EnumSpelling.Of(entity.Kind),
                entity.Name,
                entity.Distinguisher,
                entity.Sex,
                entity.Tribe,
                entity.PlaceKind,
                entity.ModernEquivalent,
                entity.Notes,
                entity.OpenBibleId,
                entity.Origin,
                entity.Source,
                Datasets.Of(entity.Source),
                tally.References,
                tally.Mentions,
                tally.Disputed,
                [
                    .. names.Select(n => new EntityNameResponse(
                        n.Label, n.Hebrew, n.HebrewTransliterated, n.Greek, n.GreekTransliterated,
                        n.Meaning, Numbers(n.HebrewStrongNumber), Numbers(n.GreekStrongNumber), n.Kind)),
                ],
                related,
                [.. events.Select(one => Event(one, placesNamed))],
                [
                    .. stated.Select(row => new EntityReferenceSourceResponse(
                        Datasets.Of(row.Source), row.Source, row.References, row.Mentions)),
                ],
                [
                    .. established.Select(c => new EntityClaimResponse(
                        EnumSpelling.Of(c.Method), c.Confidence, c.Source, Datasets.Of(c.Source), c.Note)),
                ],
                alternatives,
                alternatives.Count > 0)
            {
                Forms = own.GetValueOrDefault(entity.Slug),
                Descriptor = await Descriptors.Of(
                    db, entity.Slug, language, cancellationToken),
                Location = entity.Location,
                LocalName = (await EntityNames.Of(db, [entity.Id], language, cancellationToken))
                    .GetValueOrDefault(entity.Id),
                Renderings = await Renderings(db, entity.Id, cancellationToken),
                Images = await ImageEndpoints.Of(
                    db, entity.Id, cancellationToken, settings.Is(SiteSettings.GeneratedImages), language),
                Bearers =
                [
                    .. bearers.Select(b => new EntityTitleResponse(
                        b.Slug, EnumSpelling.Of(b.Kind), b.Name, b.Distinguisher,
                        BookReferences.At(b.CanonicalBook, b.CanonicalChapter, b.CanonicalVerse)!, b.Note)),
                ],
                Titles =
                [
                    .. titles.Select(t => new EntityTitleResponse(
                        t.Slug, EnumSpelling.Of(t.Kind), t.Name, t.Distinguisher,
                        BookReferences.At(t.CanonicalBook, t.CanonicalChapter, t.CanonicalVerse)!, t.Note)),
                ],
                Subtype = entity.Subtype,
                Passages = [.. passages.Select(Passage)],
                Times =
                [
                    .. times.Select(o => new ObservanceTimeResponse(
                        o.Cycle, o.Month, o.Day, o.LastDay,
                        BookReferences.At(o.CanonicalBook, o.CanonicalChapter, o.CanonicalVerse)!, o.Note)),
                ],
            });
        });

        // Every place that has a point, in one payload small enough to draw them all on one map:
        // about 1,300 rows of a slug, a name, two numbers and two words. The paged index cannot do
        // this without fourteen round trips, and a map is useless until it has all of them.
        routes.MapGet("/places/map", async (
            [FromQuery] string? language,
            AppDbContext db,
            CancellationToken cancellationToken) =>
            Results.Ok(await Map(db, language, cancellationToken)));

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
            var references = db.EntityVerses.Where(v => v.EntityId == entity);
            var addresses = Addresses(references);

            var total = await addresses.CountAsync(cancellationToken);
            var wanted = await addresses
                .OrderBy(address => address)
                .Skip(Math.Max(0, skip ?? 0))
                .Take(Math.Clamp(take ?? 50, 1, MostPerPage))
                .ToListAsync(cancellationToken);

            var namings = await references
                .Where(v => wanted.Contains((v.CanonicalBook * BookStride) + (v.CanonicalChapter * ChapterStride)
                                            + v.CanonicalVerse))
                .Select(v => new
                {
                    v.CanonicalBook, v.CanonicalChapter, v.CanonicalVerse, v.Label, v.Disputed, v.Source,
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
                            at.Any(v => v.Disputed));
                    }),
                ]));
        });

        // The timeline. Ordered by the year from creation rather than the BCE year, because that is
        // the number the source computes and the one every event has.
        routes.MapGet("/events", async (
            [FromQuery] string? entity,
            [FromQuery] int? fromYear,
            [FromQuery] int? toYear,
            [FromQuery] string? realm,
            [FromQuery] int? skip,
            [FromQuery] int? take,
            AppDbContext db,
            CancellationToken cancellationToken) =>
        {
            var events = db.Events.AsQueryable();

            if (realm is { Length: > 0 })
            {
                events = events.Where(e => e.Realm == realm);
            }

            if (entity is { Length: > 0 })
            {
                events = events.Where(e => e.Entity!.Slug == entity);
            }

            if (fromYear is { } from)
            {
                events = events.Where(e => e.YearFromCreation >= from);
            }

            if (toYear is { } to)
            {
                events = events.Where(e => e.YearFromCreation <= to);
            }

            var total = await events.CountAsync(cancellationToken);
            var page = await InOrder(events)
                .Skip(Math.Max(0, skip ?? 0))
                .Take(Math.Clamp(take ?? 50, 1, MostPerPage))
                .Select(Rows)
                .ToListAsync(cancellationToken);

            var places = await PlacesNamed(db, page.Select(row => EventLocation.Of(row.Event)), cancellationToken);

            return Results.Ok(new EventListResponse(total, [.. page.Select(row => Event(row, places))]));
        });

        // Every event at once, trimmed to what a timeline draws with.
        //
        // The paged endpoint caps at a hundred, so a timeline would make six round trips for the
        // 572 events and more as the corpus grows — and it would make them again on every zoom.
        // That is the shape that stalls. Trimmed, the whole set is 58 KB, which is fetched once
        // and never fetched again, so panning and zooming touch no network at all.
        //
        // When world history arrives and this becomes megabytes, a windowed request earns its
        // complexity. Not before.
        // One event, with everything the trimmed timeline payload leaves out.
        //
        // The timeline sends 1,508 events and cannot afford a description apiece; a reader who has
        // picked one wants exactly that, plus the arithmetic and every reckoning's answer. So it is
        // fetched when asked for rather than carried for everything on the chance it is.
        routes.MapGet("/events/{slug}", async (
            string slug,
            AppDbContext db,
            CancellationToken cancellationToken) =>
        {
            var row = await db.Events.Where(e => e.Slug == slug).Select(Rows).FirstOrDefaultAsync(cancellationToken);
            if (row is null)
            {
                return Results.NotFound();
            }

            var places = await PlacesNamed(db, [EventLocation.Of(row.Event)], cancellationToken);
            return Results.Ok(Event(row, places));
        });

        // One period: what it is, what opens and closes it, and what each reckoning makes of those.
        routes.MapGet("/periods/{slug}", async (
            string slug,
            AppDbContext db,
            CancellationToken cancellationToken) =>
        {
            var period = await db.Periods
                .Where(p => p.Slug == slug)
                .Select(p => new
                {
                    p.Slug,
                    p.Name,
                    p.Kind,
                    p.Level,
                    p.Realm,
                    p.Region,
                    p.Uri,
                    p.Notes,
                    p.Source,
                    Parent = p.Parent == null ? null : new { p.Parent.Slug, p.Parent.Name },
                    Entity = p.Entity == null ? null : new { p.Entity.Slug, p.Entity.Name, p.Entity.Distinguisher },
                    Opens = p.StartEvent == null ? null : new { p.StartEvent.Slug, p.StartEvent.Name },
                    Closes = p.EndEvent == null ? null : new { p.EndEvent.Slug, p.EndEvent.Name },
                    p.StartEventId,
                    p.EndEventId,
                    p.StartYear,
                    p.EndYear,
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (period is null)
            {
                return Results.NotFound();
            }

            var years = await EventYears(
                db,
                db.EventDates.Where(d => d.EventId == period.StartEventId || d.EventId == period.EndEventId),
                cancellationToken);

            var children = await db.Periods
                .Where(p => p.Parent!.Slug == slug)
                .OrderBy(p => p.StartYear)
                .Select(p => new PeriodRefResponse(p.Slug, p.Name, p.Kind, p.StartYear, p.EndYear))
                .Take(60)
                .ToListAsync(cancellationToken);

            return Results.Ok(new PeriodResponse(
                period.Slug,
                period.Name,
                period.Kind,
                period.Level,
                period.Realm,
                period.Region,
                period.Uri,
                period.Notes,
                period.Source,
                Datasets.Of(period.Source),
                period.Parent is null ? null : new PeriodRefResponse(
                    period.Parent.Slug, period.Parent.Name, null, null, null),
                period.Entity is null ? null : new NamedEntityResponse(
                    period.Entity.Slug, period.Entity.Name, period.Entity.Distinguisher),
                period.Opens is null ? null : new EventRefResponse(period.Opens.Slug, period.Opens.Name),
                period.Closes is null ? null : new EventRefResponse(period.Closes.Slug, period.Closes.Name),
                children)
            {
                Years = Span(years, period.StartEventId, period.EndEventId, period.StartYear, period.EndYear),
            });
        });

        // The reckonings on their own, for a page that has to name or format one and has no
        // reason to hold a chronology's worth of dates to do it.
        routes.MapGet("/chronologies", async (AppDbContext db, CancellationToken cancellationToken) =>
            Results.Ok(new ChronologyListResponse(
                LastYearBeforeChrist, await Chronologies(db, cancellationToken))));

        // Every period with its span in each reckoning: the timeline without its fifteen hundred
        // events, which is all a page placing one year or naming one era needs.
        routes.MapGet("/periods", async (AppDbContext db, CancellationToken cancellationToken) =>
        {
            var anchoring = db.EventDates.Where(d => db.Periods.Any(
                p => p.StartEventId == d.EventId || p.EndEventId == d.EventId));
            var years = await EventYears(db, anchoring, cancellationToken);

            return Results.Ok(new PeriodListResponse(
                LastYearBeforeChrist,
                await Periods(db, years, cancellationToken)));
        });

        routes.MapGet("/timeline", async (
            AppDbContext db,
            SiteSettingsFile settings,
            CancellationToken cancellationToken) =>
        {
            var events = await InOrder(db.Events)
                .Select(e => new
                {
                    e.Id,
                    e.Slug,
                    e.Name,
                    e.Kind,
                    e.Realm,
                    e.Region,
                    e.RegionAtTheTime,
                    e.Uri,
                    e.SequenceInYear,
                    Location = EventLocation.Of(e.Location, e.LocationBook, e.LocationChapter, e.LocationVerse),
                    EntitySlug = e.Entity == null ? null : e.Entity.Slug,
                })
                .ToListAsync(cancellationToken);

            var years = await EventYears(db, db.EventDates, cancellationToken);
            var periods = await Periods(db, years, cancellationToken);

            var pictures = await TimelinePictures.Of(
                db,
                [.. events.Select(e => (e.EntitySlug, e.Location))],
                periods.Select(p => p.EntitySlug),
                settings.Is(SiteSettings.GeneratedImages),
                cancellationToken);

            return Results.Ok(new TimelineResponse(
                LastYearBeforeChrist,
                await Chronologies(db, cancellationToken),
                [
                    .. events.Select(e => new TimelineEventResponse(
                        e.Slug,
                        e.Name,
                        e.Kind,
                        e.Realm,
                        e.Region,
                        e.Uri,
                        e.EntitySlug,
                        e.SequenceInYear != null,
                        years.GetValueOrDefault(e.Id) ?? [])
                    {
                        Picture = pictures.ForEvent(e.EntitySlug, e.Location),
                        RegionAtTheTime = e.RegionAtTheTime,
                    }),
                ],
                [.. periods.Select(p => p with { Picture = pictures.ForPeriod(p.EntitySlug) })]));
        });
    }

    private static async Task<IList<ChronologyResponse>> Chronologies(
        AppDbContext db,
        CancellationToken cancellationToken) =>
    [
        .. (await db.Chronologies.OrderBy(c => c.Position).ToListAsync(cancellationToken))
            .Select(c => new ChronologyResponse(
                c.Slug, c.Name, c.Authority, c.Basis, c.Source, c.LastYearBeforeTheCommonEra, c.IsDefault)),
    ];

    /// <summary>
    /// Where each chronology puts each of these dates' events on its own axis, keyed by chronology
    /// slug: its year from creation, except where the reckoning states its year of the common era,
    /// which places the event instead.
    ///
    /// The two differ by a year wherever a reckoning's year opens mid-way through the Julian one.
    /// Ussher's creation is his year 1 and, in his own words, 4004 BC; the axis turns a year into an
    /// era by the chronology's zero, which makes year 1 into 4003, so it is given the position that
    /// reads back as what he printed. And a date that states only its year of the common era —
    /// Ussher's passion narrative, whose anno mundi column is ten years out — is placed by that,
    /// where it would otherwise not be placed at all.
    /// </summary>
    internal static async Task<Dictionary<int, Dictionary<string, int>>> EventYears(
        AppDbContext db,
        IQueryable<EventDate> dates,
        CancellationToken cancellationToken)
    {
        var reckoning = await db.Chronologies.ToDictionaryAsync(
            c => c.Id, c => (c.Slug, Zero: c.LastYearBeforeTheCommonEra), cancellationToken);
        var rows = await dates
            .Where(d => d.Year != null || d.StatedYear != null)
            .Select(d => new { d.EventId, d.ChronologyId, d.Year, d.StatedYear })
            .ToListAsync(cancellationToken);

        var years = new Dictionary<int, Dictionary<string, int>>();
        foreach (var date in rows)
        {
            if (!years.TryGetValue(date.EventId, out var byChronology))
            {
                byChronology = [];
                years[date.EventId] = byChronology;
            }

            var (slug, zero) = reckoning[date.ChronologyId];
            byChronology[slug] = Placed(date.Year, date.StatedYear, zero);
        }

        return years;
    }

    /// <summary>
    /// A date's position on its chronology's axis. A stated year of the common era has no year zero,
    /// so 4004 BC is the axis year that reads back as 4004 BC: <paramref name="zero"/> less 4,003.
    /// </summary>
    internal static int Placed(int? year, int? statedYear, int zero) =>
        statedYear switch
        {
            < 0 => zero + statedYear.Value + 1,
            > 0 => zero + statedYear.Value,
            _ => year!.Value,
        };

    internal static async Task<IList<TimelinePeriodResponse>> Periods(
        AppDbContext db,
        Dictionary<int, Dictionary<string, int>> years,
        CancellationToken cancellationToken)
    {
        var periods = await db.Periods
            .OrderBy(p => p.Level).ThenBy(p => p.StartYear)
            .Select(p => new
            {
                p.Slug,
                p.Name,
                p.Kind,
                p.Level,
                p.Realm,
                p.Region,
                p.Uri,
                ParentSlug = p.Parent == null ? null : p.Parent.Slug,
                EntitySlug = p.Entity == null ? null : p.Entity.Slug,
                p.Notes,
                p.StartEventId,
                p.EndEventId,
                p.StartYear,
                p.EndYear,
            })
            .ToListAsync(cancellationToken);

        return
        [
            .. periods.Select(p => new TimelinePeriodResponse(
                p.Slug,
                p.Name,
                p.Kind,
                p.Level,
                p.Realm,
                p.Region,
                p.Uri,
                p.ParentSlug,
                p.EntitySlug,
                p.Notes,
                Span(years, p.StartEventId, p.EndEventId, p.StartYear, p.EndYear))),
        ];
    }

    /// <summary>
    /// A period's years, in every chronology that can state both of them.
    ///
    /// Both ends or neither. A band whose start came from Ussher and whose end came from the base
    /// reckoning is a duration nobody computed, and drawing one would be the exact failure this
    /// whole model exists to avoid — Ussher is up to 278 years from the base, so such a band could
    /// be wrong by two centuries while looking authoritative.
    /// </summary>
    internal static Dictionary<string, int[]> Span(
        Dictionary<int, Dictionary<string, int>> years,
        int? startEventId,
        int? endEventId,
        int? startYear,
        int? endYear)
    {
        var span = new Dictionary<string, int[]>();

        if (startEventId is { } opens && endEventId is { } closes
            && years.TryGetValue(opens, out var from) && years.TryGetValue(closes, out var to))
        {
            foreach (var (chronology, year) in from)
            {
                if (to.TryGetValue(chronology, out var ends))
                {
                    span[chronology] = [year, ends];
                }
            }
        }

        // A period with no anchors carries its own years, and they belong to no chronology.
        if (span.Count == 0 && startYear is { } first && endYear is { } last)
        {
            span[""] = [first, last];
        }

        return span;
    }

    /// <summary>
    /// The Strong numbers of one name, which the column keeps comma-joined the way the lexicon's
    /// own cross-references are kept.
    /// </summary>
    /// <summary>
    /// The lexicon entries stored against a name, one per word of it.
    ///
    /// Trimmed and emptied out, because the column holds them as one comma-separated string written
    /// with spaces — <c>"H1, NONE"</c> — and splitting it without trimming sends <c>" NONE"</c> to
    /// every client, each of which then has to know that. A word the source has no entry for stays
    /// in the list as the source wrote it: the position matters, and a name is easier to read with
    /// a gap named than with a gap.
    /// </summary>
    private static IList<string> Numbers(string? stored) =>
        stored is { Length: > 0 }
            ? [.. stored.Split(',').Select(entry => entry.Trim()).Where(entry => entry.Length > 0)]
            : [];

    /// <summary>
    /// The event with the two fields that need a join, read in the query rather than off a
    /// navigation property. Projecting <c>e.Entity.Slug</c> through a method EF cannot translate
    /// left every one of the 572 events without a person on it, while the filter that uses the
    /// same navigation worked — so the timeline could select by a person and never name one.
    /// </summary>
    internal sealed record EventRow(
        Database.Entities.Event Event,
        string? EntitySlug,
        string? EntityName,
        string? DefaultChronology,
        IList<EventDateResponse> Dates);

    /// <summary>
    /// One vocabulary for the two sides of the turn, in the response and in the dates alike. Named
    /// rather than written twice, because they were written twice and disagreed: an event past the
    /// turn said <c>AD</c> at the top and <c>CE</c> in its dates, so a client switching on the
    /// field had three values to handle for two eras.
    /// </summary>
    private const string CommonEra = "CE";

    private const string BeforeTheCommonEra = "BCE";

    /// <summary>
    /// Which side of the turn a year from creation falls. The source counts forward without a
    /// sign, so its year 3,969 answers <c>8</c> meaning 8 CE, and nothing in the number says so.
    /// The turn is at 3,961, and it is arithmetic rather than a guess: below it the BCE year is
    /// 3,962 less the count, above it the CE year is the count less 3,961.
    ///
    /// <para>
    /// This is the default chronology's <c>last_year_before_the_common_era</c>, and every event
    /// with a date row reads it from there instead. It stands alone in two places only: the
    /// timeline payload, which states it once for a client that has to place a bare year, and an
    /// event no reckoning dates at all.
    /// </para>
    /// </summary>
    private const int LastYearBeforeChrist = 3961;

    /// <summary>
    /// Events in the order a timeline draws them: by the year, then by whatever order a source
    /// stated inside that year, then by the slug so that what nobody ordered is at least stable.
    ///
    /// <para>
    /// The year alone does not order them. Ussher dates dozens of paragraphs to AD 33, and writes
    /// <em>the next day</em> and <em>on the third day</em> across them, so the year is a tie and his
    /// paragraphs are the tiebreak he himself stated. The tiebreak before this was the slug, which
    /// is a name and not a position: it sorts <c>ussher-{paragraph}</c> as text, so it agrees with
    /// him only while every loaded paragraph number has the same number of digits, and it says
    /// nothing at all about the order of two rows from different sources in one year.
    /// </para>
    ///
    /// <para>
    /// **Nothing invents a position for a row that has none.** The null check is written out rather
    /// than left to the database's own idea of where nulls sort, because it is the whole claim this
    /// ordering makes: what a source ordered comes in that order, and what nobody ordered follows
    /// it. <see cref="EventResponse.Sequenced"/> says which of the two a row is, so a client is
    /// never left reading an arbitrary order as a chronology.
    /// </para>
    /// </summary>
    internal static IOrderedQueryable<Database.Entities.Event> InOrder(
        IQueryable<Database.Entities.Event> events) =>
        events
            .OrderBy(e => e.YearFromCreation)
            .ThenBy(e => e.SequenceInYear == null)
            .ThenBy(e => e.SequenceInYear)
            .ThenBy(e => e.Slug);

    /// <summary>
    /// An event with the people and the dates it needs, projected in the query.
    ///
    /// One expression, used by both places that return events, because reading a navigation
    /// property outside the projection is what left all 572 events without a person once already
    /// while the filter over the same navigation went on working.
    /// </summary>
    internal static readonly System.Linq.Expressions.Expression<Func<Database.Entities.Event, EventRow>> Rows =
        e => new EventRow(
            e,
            e.Entity == null ? null : e.Entity.Slug,
            e.Entity == null ? null : e.Entity.Name,
            e.Dates.Where(d => d.Chronology!.IsDefault).Select(d => d.Chronology!.Slug).FirstOrDefault(),
            e.Dates
                .OrderBy(d => d.Chronology!.Position)
                .Select(d => new EventDateResponse(
                    d.Chronology!.Slug,
                    d.Chronology.Name,
                    d.Year,
                    d.StatedYear != null
                        ? d.StatedYear < 0 ? -d.StatedYear.Value : d.StatedYear.Value
                        : d.Year == null
                            ? null
                            : d.Year <= d.Chronology.LastYearBeforeTheCommonEra
                                ? d.Chronology.LastYearBeforeTheCommonEra - d.Year.Value + 1
                                : d.Year.Value - d.Chronology.LastYearBeforeTheCommonEra,
                    d.StatedYear != null
                        ? d.StatedYear > 0 ? CommonEra : BeforeTheCommonEra
                        : d.Year != null && d.Year > d.Chronology.LastYearBeforeTheCommonEra
                            ? CommonEra
                            : BeforeTheCommonEra,
                    d.EarliestYear,
                    d.LatestYear,
                    d.Calculation,
                    d.Citation,
                    d.Notes)
                {
                    Stated = d.StatedYear != null,
                })
                .ToList());

    internal static EventResponse Event(EventRow row, IReadOnlyDictionary<EventLocation, string> places) =>
        Event(row.Event, row.EntitySlug, row.EntityName, row.DefaultChronology, row.Dates, places);

    /// <summary>
    /// The place each of these locations names: the one place of that name the corpus records at
    /// the verse the source names it at, or failing that the only place of that name there is.
    ///
    /// An event's location is free text — <em>Gerar</em>, <em>West of Eden</em> — and the
    /// encyclopedia holds places as records with pages of their own, so the one screen that names
    /// a place was the one screen that could not open it. Resolved here rather than by a client
    /// matching on the words. 27 of the 159 locations name a word two places answer to, and the
    /// verse is what tells them apart: the Samaria a reign of Israel begins in is the one recorded
    /// at 2 Kings 13:1, and that is the corpus's own answer rather than an inference about
    /// spelling. Where the verse does not settle it either, nothing is linked — a link that picks
    /// one of two Samarias is worse than none. The words stay as the source wrote them either way.
    /// </summary>
    internal static async Task<Dictionary<EventLocation, string>> PlacesNamed(
        AppDbContext db,
        IEnumerable<EventLocation?> locations,
        CancellationToken cancellationToken)
    {
        var asked = locations.OfType<EventLocation>().Distinct().ToList();
        if (asked.Count == 0)
        {
            return [];
        }

        var named = asked.Select(location => location.Name).Distinct().ToList();
        var places = await db.Entities
            .Where(e => e.Kind == EntityKind.Place && named.Contains(e.Name.ToLower()))
            .Select(e => new { e.Id, e.Slug, Name = e.Name.ToLower() })
            .ToListAsync(cancellationToken);
        var byName = places.ToLookup(place => place.Name);

        var shared = places.GroupBy(place => place.Name).Where(group => group.Count() > 1)
            .SelectMany(group => group.Select(place => place.Id))
            .ToList();
        var books = asked.Where(location => location.Book != null).Select(location => location.Book!.Value)
            .Distinct().ToList();
        var recorded = shared.Count == 0 || books.Count == 0
            ? []
            : (await db.EntityVerses
                .Where(v => shared.Contains(v.EntityId) && books.Contains(v.CanonicalBook))
                .Select(v => new { v.EntityId, v.CanonicalBook, v.CanonicalChapter, v.CanonicalVerse })
                .Distinct()
                .ToListAsync(cancellationToken))
            .Select(v => (v.EntityId, v.CanonicalBook, v.CanonicalChapter, v.CanonicalVerse))
            .ToHashSet();

        var resolved = new Dictionary<EventLocation, string>();
        foreach (var location in asked)
        {
            var candidates = byName[location.Name].ToList();
            var atTheVerse = location.Book is { } book && location.Chapter is { } chapter && location.Verse is { } verse
                ? candidates.Where(place => recorded.Contains((place.Id, book, chapter, verse))).ToList()
                : [];

            if (atTheVerse.Count == 1)
            {
                resolved[location] = atTheVerse[0].Slug;
            }
            else if (candidates.Count == 1)
            {
                resolved[location] = candidates[0].Slug;
            }
        }

        return resolved;
    }

    /// <summary>
    /// One event, with the top-level year read off the default reckoning's own row rather than
    /// computed a second time.
    ///
    /// BibleData ships a <c>bce_year</c> column beside the year from creation, and the two do not
    /// always agree: David's return to Jerusalem is year 2,978, which the base reckoning turns
    /// into 984 BCE, while the column says 980. Serving both put two answers to one question in
    /// one payload, and left two events dated <c>null</c> at the top that every reckoning below
    /// them could date. Taking the number from the row that already derived it means the two can
    /// no longer drift apart, because there is only one of them.
    /// </summary>
    private static EventResponse Event(
        Database.Entities.Event e,
        string? entitySlug,
        string? entityName,
        string? defaultChronology,
        IList<EventDateResponse> dates,
        IReadOnlyDictionary<EventLocation, string> places)
    {
        var reckoning = dates.FirstOrDefault(d => d.Chronology == defaultChronology);

        return new EventResponse(
            e.Slug,
            e.Name,
            e.NameSource,
            e.Description,
            e.Kind,
            entitySlug,
            entityName,
            e.YearFromCreation,
            reckoning?.BceYear,
            e.AgeAtEvent,
            e.Calculation,
            BookReferences.At(e.CanonicalBook, e.CanonicalChapter, e.CanonicalVerse),
            e.Location,
            e.Realm,
            e.Region,
            e.Uri,
            e.Notes,
            e.Source,
            Datasets.Of(e.Source),
            dates)
        {
            Sequenced = e.SequenceInYear is not null,
            LocationSlug = EventLocation.Of(e) is { } location ? places.GetValueOrDefault(location) : null,
            RegionAtTheTime = e.RegionAtTheTime,
            // Only where no reckoning states this event at all does the base zero point stand in;
            // it is the same number the default chronology holds.
            Era = reckoning?.Era
                  ?? (e.YearFromCreation > LastYearBeforeChrist ? CommonEra : BeforeTheCommonEra),
        };
    }
}

/// <summary>The three counts of an entity's references, which are three different questions.</summary>
internal sealed record EntityTally(int References, int Mentions, int Disputed);

/// <param name="References">How many verses name this entity.</param>
/// <param name="Mentions">
/// How many times they name it, which is the larger number: the source records one row per
/// naming, and Matthew 20:30 names Jesus three times over.
/// </param>
internal record EntitySummaryResponse(
    string Slug,
    string Kind,
    string Name,
    string? Distinguisher,
    int References,
    int Mentions)
{
    /// <summary>
    /// What this corpus says the entity is, as the pieces of a line whose every name is a link, in
    /// the language it says it is in. Null where nothing has been generated for this entity yet.
    /// </summary>
    public EntityDescriptorResponse? Descriptor { get; init; }

    /// <summary>
    /// The name in the language asked for, where the corpus has one; null where it has none, or
    /// where the language asked for is the headword's own, and a client shows the English name.
    /// </summary>
    public string? LocalName { get; init; }

    /// <summary>The picture the entity's page leads with, to show small; null where it has none.</summary>
    public EntityThumbnailResponse? Thumbnail { get; init; }

    /// <summary>What sort of object or observance it is; null on every other kind.</summary>
    public string? Subtype { get; init; }
}

internal record EntityListResponse(int Total, IList<EntitySummaryResponse> Items);

/// <summary>
/// A stretch of Scripture an object's or an observance's page sends a reader to, as the record lists
/// it: a role — <c>key</c> for one to read about it, <c>command</c> for one that appoints it — and the
/// span. A verse left null is the start or the end of its chapter.
/// </summary>
internal record PassageResponse(
    string Role,
    BookRefResponse Book,
    int Chapter,
    int? Verse,
    int EndChapter,
    int? EndVerse,
    string? Note);

/// <summary>
/// Where an observance falls, as the verse that appoints it says: the month counted from Abib, the
/// day of that month — or of the week, for a weekly one — and the last day where it runs longer.
/// Each is null where the text does not state it.
/// </summary>
internal record ObservanceTimeResponse(
    string Cycle,
    int? Month,
    int? Day,
    int? LastDay,
    VerseRefResponse Reference,
    string? Note);

/// <param name="Count">How many entities are filed under this letter; zero for an empty one.</param>
internal record EntityLetterResponse(string Letter, int Count);

/// <param name="Letters">
/// The language's alphabet in order — A to Z for English — every letter present, followed by any
/// other letter a name opens with.
/// </param>
internal record EntityLettersResponse(int Total, IList<EntityLetterResponse> Letters);

/// <summary>
/// How far one kind of entity's references actually reach, so that a count on a page can be read
/// as a fact about the dataset rather than as a fact about the text.
/// </summary>
/// <param name="Named">
/// How many of them any verse names. An entity the source lists and never cites is a row with no
/// references, which is a different fact from an entity the text never mentions.
/// </param>
/// <param name="Books">
/// The canonical books in which any reference of this kind falls, and nothing beyond them. Where
/// this is short of the canon, a page that says "1 verse" is reporting where the source stopped.
/// </param>
/// <param name="Sources">
/// Which datasets state this layer's references, and what each one alone reaches. The layer's own
/// totals are the union; these are the statements the union is made of.
/// </param>
internal record EntityLayerCoverageResponse(
    string Kind,
    int Entities,
    int Named,
    int References,
    int Mentions,
    CoverageResponse Books,
    IList<EntitySourceCoverageResponse> Sources);

/// <param name="Dataset">
/// Which of the declared datasets this is, where one claims the source string — the id a client
/// uses to reach its name, its licence and its credit. Null where nothing declares it, which is
/// how an undeclared source is noticed instead of being silently credited to nobody.
/// </param>
internal record EntitySourceCoverageResponse(
    string? Dataset,
    string Source,
    int References,
    int Mentions,
    CoverageResponse Books);

/// <param name="Canon">
/// How many books a complete layer would reach, so a client can say "2 of 66" without holding a
/// number of its own.
/// </param>
internal record EntityCoverageResponse(int Canon, IList<EntityLayerCoverageResponse> Layers);

/// <param name="References">
/// How many verses name this entity — the number a reader asking "how often is Nebuchadnezzar in
/// the text" is asking for.
/// </param>
/// <param name="Mentions">
/// How many namings those verses hold. The two differ by six per cent across the corpus and by a
/// third on Nebuchadnezzar, so they are both here and both labelled rather than one of them
/// standing in for the other.
/// </param>
/// <param name="Disputed">
/// Verses the source itself cannot resolve. BibleData holds the God of Israel and Jesus as one
/// entity; 1,417 New Testament namings use a word the New Testament gives both, and which of the
/// two is meant is a reading of the text rather than a fact about the dataset.
/// </param>
/// <param name="Claims">
/// What established this record, where anything beyond its dataset did. Empty for every record a
/// dataset supplied, whose <paramref name="Source"/> is the whole answer; one entry, with a method
/// and whoever decided, for a record this corpus wrote because a verse names somebody no dataset
/// holds.
/// </param>
/// <param name="Alternatives">
/// Who else this might be, where the evidence does not decide — and empty, which is the usual case,
/// where it does. A record with alternatives is not a weaker record: it is one that has said out
/// loud what a record without them is quietly assuming.
/// </param>
/// <param name="Unsettled">
/// Whether the identification is open. Derived from <paramref name="Alternatives"/> rather than
/// stored beside it, so the flag and the list cannot come apart.
/// </param>
internal record EntityResponse(
    string Slug,
    string Kind,
    string Name,
    string? Distinguisher,
    string? Sex,
    string? Tribe,
    string? PlaceKind,
    string? ModernEquivalent,
    string? Notes,
    string? OpenBibleId,
    EntityOriginResponse? Origin,
    string Source,
    string? SourceId,
    int References,
    int Mentions,
    int Disputed,
    IList<EntityNameResponse> Names,
    IList<EntityRelationshipResponse> Relationships,
    IList<EventResponse> Events,
    IList<EntityReferenceSourceResponse> ReferenceSources,
    IList<EntityClaimResponse> Claims,
    IList<EntityAlternativeResponse> Alternatives,
    bool Unsettled)
{
    /// <summary>
    /// This entity's own name in every case a pass produced for the language asked for.
    ///
    /// A relationship row is a sentence with two names in it and either end can be the one a case
    /// falls on: <em>Лот, син Гарана</em> puts the counterpart in the genitive, and the same row
    /// read from Haran's page puts Lot there. So the page needs its own forms as well as its
    /// counterparts', and neither can stand in for the other.
    /// </summary>
    public Dictionary<string, string>? Forms { get; init; }

    /// <summary>
    /// What this corpus says the entity is: the pieces of a line, each name among them carrying the
    /// entity it names complete enough to be linked, and the claims the line was made of with the
    /// verse each was read from. <see cref="EntityDescriptorResponse.Language"/> says which language
    /// it came out in, which is not always the one asked for.
    ///
    /// <para>
    /// It is what <paramref name="Distinguisher"/> was being shown for, and it replaces it — but
    /// that field stays on the wire while the client is changed, because a field that vanishes
    /// mid-flight breaks whoever is reading it. Null where nothing has been generated for this
    /// entity.
    /// </para>
    /// </summary>
    public EntityDescriptorResponse? Descriptor { get; init; }

    /// <summary>
    /// Where the place is, as one point. Null on every person and people, and on a place its
    /// gazetteer cannot locate or locates only with coordinates this corpus does not hold.
    /// </summary>
    public EntityLocationResponse? Location { get; init; }

    /// <summary>The name in the language asked for, as on the index; null where there is none.</summary>
    public string? LocalName { get; init; }

    /// <summary>
    /// How each text prints the name, counted from the words that name this entity there: one entry
    /// per text, its heading spelling first and every other spelling it prints after. Empty where no
    /// word of any text is named as this entity.
    /// </summary>
    public IList<EntityRenderingResponse> Renderings { get; init; } = [];

    /// <summary>
    /// Who the text gives this title to, each at the verse that names both. Empty on everything but
    /// a title, and on a title the text gives nobody by name.
    /// </summary>
    public IList<EntityTitleResponse> Bearers { get; init; } = [];

    /// <summary>The titles the text gives this person, each at the verse that names both.</summary>
    public IList<EntityTitleResponse> Titles { get; init; } = [];

    /// <summary>
    /// Its pictures, the one the page leads with first, each with who made it and under what licence.
    /// Empty where it has none — which is most people, and always God.
    /// </summary>
    public IList<EntityImageResponse> Images { get; init; } = [];

    /// <summary>What sort of object or observance it is — furnishing, structure, feast; null on every other kind.</summary>
    public string? Subtype { get; init; }

    /// <summary>The passages to read about it and those that command it, in the record's order.</summary>
    public IList<PassageResponse> Passages { get; init; } = [];

    /// <summary>Where an observance falls in the year, one entry per position a verse states.</summary>
    public IList<ObservanceTimeResponse> Times { get; init; } = [];
}

/// <param name="Corpus">The text, by the id every other response names it by.</param>
/// <param name="Language">The text's language, as the corpus codes it.</param>
/// <param name="Name">The spelling the text is headed with.</param>
/// <param name="Occurrences">How many words of the text name the entity, under every spelling.</param>
/// <param name="Spellings">Every spelling the text prints, the commonest first.</param>
internal record EntityRenderingResponse(
    string Corpus,
    string Language,
    string Name,
    int Occurrences,
    IList<EntitySpellingResponse> Spellings);

internal record EntitySpellingResponse(string Form, int Occurrences);

/// <summary>
/// The other end of a title held: the person on a title's page, the title on a person's.
/// </summary>
/// <param name="Reference">The verse that names the person and the title together.</param>
/// <param name="Note">What that verse says, so the claim can be checked against it.</param>
internal record EntityTitleResponse(
    string Slug,
    string Kind,
    string Name,
    string? Distinguisher,
    VerseRefResponse Reference,
    string? Note);

/// <param name="Kind">
/// What the point stands for: <c>point</c> the place itself, <c>representative-point</c> a spot
/// inside a region or along a path, <c>center</c> the middle of the circle the place is somewhere
/// in, <c>settlement</c> the town somewhere inside which it stood.
/// </param>
/// <param name="Confidence">
/// The gazetteer's score for the identification, over a thousand: 0.5 and above is high confidence
/// and 1 very high. Not clamped, because the source's score is not — it runs past 1 and below 0.
/// </param>
internal record EntityLocationResponse(double Lon, double Lat, string Kind, double Confidence);

/// <param name="References">How many verses name the place, as on its page.</param>
/// <param name="Chapters">
/// Every chapter that names it, each as its book's canonical ordinal times a thousand plus the
/// chapter — 44013 is Acts 13 — in order, so a map can show the places of a book, a chapter or a
/// stretch of the story without asking again.
/// </param>
internal record PlacePointResponse(
    string Slug,
    string Name,
    double Lon,
    double Lat,
    string Kind,
    double Confidence,
    int References,
    IList<int> Chapters)
{
    /// <summary>
    /// What kind of place it is, as the entry states it: <c>settlement</c>, <c>mountain</c>,
    /// <c>river</c>, several comma-separated where the text calls it more than one. Null where
    /// the entry does not say.
    /// </summary>
    public string? PlaceKind { get; init; }

    /// <summary>The name in the language asked for, as on the index; null where there is none.</summary>
    public string? LocalName { get; init; }
}

/// <param name="Datasets">Whose points these are, as declared dataset ids, for the credit a map owes.</param>
internal record PlaceMapResponse(int Total, IList<string> Datasets, IList<PlacePointResponse> Items);

/// <param name="Confidence">
/// How sure, and null exactly where a person or a source stated it rather than a process concluding
/// it — the same rule the word annotations and the links follow.
/// </param>
internal record EntityClaimResponse(
    string Method,
    double? Confidence,
    string Source,
    string? Dataset,
    string? Note);

/// <summary>
/// Whom or where a people is named after, as a page a reader can open.
///
/// Null on every person and every place, and on the two thirds of peoples whose ancestor the
/// encyclopedia does not hold — where it is null the record's claim still says whom, in the words
/// of whoever said it, and only the link is missing.
/// </summary>
internal record EntityOriginResponse(string Slug, string Kind, string Name, string? Distinguisher);

/// <param name="Slug">
/// The alternative's own page, where the encyclopedia holds one. Null where it does not, in which
/// case <paramref name="Describes"/> is all there is to say.
/// </param>
internal record EntityAlternativeResponse(
    string? Slug,
    string? Name,
    string? Distinguisher,
    string? Describes,
    string Reason,
    string Source);

/// <summary>
/// Who states that the text names this entity, and how much of the count is theirs.
/// </summary>
/// <remarks>
/// <see cref="EntityResponse.Source"/> is who supplied the entity, which is a different question:
/// 110 of the places came from one dataset and are referenced almost entirely by another, so a
/// page crediting the reference list to the entity's source would name the wrong party on nearly
/// every place in the corpus.
/// </remarks>
internal record EntityReferenceSourceResponse(
    string? Dataset,
    string Source,
    int References,
    int Mentions);

/// <param name="HebrewStrongNumbers">
/// The lexicon entries this name is, one per word of it. A proper name has one; a title has as
/// many as it has words — <em>King of Judah</em> is H4428 and H3063 — which is why it is a list
/// and not a number.
/// </param>
internal record EntityNameResponse(
    string Label,
    string? Hebrew,
    string? HebrewTransliterated,
    string? Greek,
    string? GreekTransliterated,
    string? Meaning,
    IList<string> HebrewStrongNumbers,
    IList<string> GreekStrongNumbers,
    string? Kind);


/// <summary>What the text calls the entity at one verse, and whether that word settles who it is.</summary>
/// <param name="Dataset">
/// Which declared dataset states this naming. A place's references can come from two sources at
/// once, and the reader is owed which of them said the text names it here.
/// </param>
internal record EntityNamingResponse(string? Label, bool Disputed, string? Dataset);

/// <param name="Namings">
/// Every name the entity is given in this verse. Matthew 20:30 calls Jesus by name and by *Son of
/// David*, and both are here rather than the verse appearing twice.
/// </param>
internal record EntityReferenceResponse(
    BookRefResponse Book,
    int Chapter,
    int Verse,
    IList<EntityNamingResponse> Namings,
    bool Disputed);

internal record EntityReferenceListResponse(int Total, IList<EntityReferenceResponse> Items);

/// <param name="Calculation">
/// The arithmetic that produced the year, in a sentence, so it can be checked rather than
/// believed. This is why this dataset was chosen over the others.
/// </param>
/// <param name="Dates">
/// Every reckoning's answer, beside each other rather than one instead of the rest. A reader is
/// owed the disagreement, not a winner — and they disagree in 406 of the 419 events they share.
/// </param>
/// <param name="Source">
/// Who compiled this row and under what licence. Per row rather than per corpus, because they
/// differ: the scripture chronology is CC BY 4.0 and the world layer CC0. A page showing one
/// licence for both would assert what neither of them says.
/// </param>
/// <param name="BceYear">
/// <see cref="YearFromCreation"/> as a reader writes it, in the default reckoning, with
/// <see cref="EventResponse.Era"/> saying on which side of the turn — so past it this is a common
/// era year, exactly as it is in <c>Dates</c>. It is that reckoning's own entry in <c>Dates</c>
/// and not BibleData's <c>bce_year</c> column, which disagrees with the arithmetic on one event
/// and is empty on 266 the arithmetic dates without trouble.
/// </param>
/// <param name="Location">
/// Where it happened, as the source wrote it. <see cref="EventResponse.LocationSlug"/> is the
/// place of that name where the corpus holds exactly one.
/// </param>
/// <param name="NameSource">
/// Whose words <paramref name="Name"/> is — <c>source</c> where the dataset titled its own row,
/// <c>quoted</c> where it wrote no title and the opening of what it did write stands in, and
/// <c>generated</c> where the title was written for this corpus, in which case
/// <paramref name="Notes"/> says by what and when. Ussher's Annals are 7,000 untitled paragraphs,
/// so the distinction is the difference between quoting a chronologer and putting words in his
/// mouth.
/// </param>
/// <param name="Region">
/// Where in the world, for the world layer: today's country, or the last known, as Wikidata states
/// it — not the polity of the time, which is <see cref="EventResponse.RegionAtTheTime"/>. The
/// Battle of Himera is in Italy here, and that is a place to find it by rather than a claim about
/// who held Sicily in 480 BCE.
/// </param>
internal record EventResponse(
    string Slug,
    string Name,
    string NameSource,
    string? Description,
    string? Kind,
    string? EntitySlug,
    string? EntityName,
    int? YearFromCreation,
    int? BceYear,
    int? AgeAtEvent,
    string? Calculation,
    VerseRefResponse? Reference,
    string? Location,
    string Realm,
    string? Region,
    string? Uri,
    string? Notes,
    string Source,
    string? SourceId,
    IList<EventDateResponse> Dates)
{
    /// <summary>
    /// The place <see cref="Location"/> names: the one place of that name recorded at the verse the
    /// source names it at, or else the only place of that name; null where neither settles it. The
    /// words are the source's and stand whatever this says.
    /// </summary>
    public string? LocationSlug { get; init; }

    /// <summary>
    /// The country of the time, where the source states one whose own years contain the event's —
    /// beside <c>Region</c>, which is today's country or the last known and never the polity of the
    /// time. Null for everything outside the world layer and for most of it.
    /// </summary>
    public string? RegionAtTheTime { get; init; }

    /// <summary>
    /// Whether a source states where this falls inside its year, or whether the year is the whole
    /// of what anybody said.
    ///
    /// False for most of the corpus, and that is the honest answer rather than a missing one: a
    /// year holds ninety-five events and only some of them were ever put in an order.
    /// Where this is true the position in a list of events is a claim; where it is false the
    /// position is only stable, and a reader should be told so rather than left to infer a
    /// chronology from it.
    /// </summary>
    public bool Sequenced { get; init; }

    /// <summary>
    /// <c>BCE</c> or <c>CE</c>, saying which side of the turn <see cref="BceYear"/> falls on: the
    /// year from creation carries no sign and keeps counting past the turn, so a bare <c>8</c>
    /// could mean either side of it. The same two words as <see cref="EventDateResponse.Era"/>,
    /// and the same answer, because both are the default reckoning's.
    /// </summary>
    public string Era { get; init; } = "BCE";
}

/// <param name="Calculation">
/// The arithmetic that produced this reckoning's year, where the reckoning shows its working.
/// </param>
/// <param name="BceYear">
/// The year as a reader writes it, with <paramref name="Era"/> saying which side of the turn: the
/// reckoning's own figure where it states one, and otherwise worked out from
/// <paramref name="Year"/> and the reckoning's zero. <see cref="EventDateResponse.Stated"/> says
/// which. Null where the reckoning gives neither.
/// </param>
/// <param name="Year">
/// Years from the reckoning's own creation. Null where the reckoning states only its year of the
/// common era, which Ussher's Annals do for the paragraphs whose anno mundi column contradicts
/// their other two.
/// </param>
internal record EventDateResponse(
    string Chronology,
    string Name,
    int? Year,
    int? BceYear,
    string Era,
    int? EarliestYear,
    int? LatestYear,
    string? Calculation,
    string? Citation,
    string? Notes)
{
    /// <summary>
    /// Whether <see cref="BceYear"/> is the figure the reckoning's source printed, rather than one
    /// worked out from the year from creation. They differ by a year wherever the reckoning's year
    /// opens in the autumn — Ussher's creation is 4004 BC in his words and 4003 by the subtraction —
    /// and a reader is owed which one is in front of them.
    /// </summary>
    public bool Stated { get; init; }
}

/// <summary>
/// Where the source says an event happened: its words, lower-cased to be matched on, and the verse
/// at which it names them, where it names one.
/// </summary>
internal readonly record struct EventLocation(string Name, int? Book, int? Chapter, int? Verse)
{
    public static EventLocation? Of(string? location, int? book, int? chapter, int? verse) =>
        string.IsNullOrWhiteSpace(location)
            ? null
            : new EventLocation(location.ToLowerInvariant(), book, chapter, verse);

    public static EventLocation? Of(Database.Entities.Event e) =>
        Of(e.Location, e.LocationBook, e.LocationChapter, e.LocationVerse);
}

internal record EventListResponse(int Total, IList<EventResponse> Items);

internal record EventRefResponse(string Slug, string Name);

/// <summary>A person or a place, named just enough to link to.</summary>
internal record NamedEntityResponse(string Slug, string Name, string? Distinguisher);

internal record PeriodRefResponse(string Slug, string Name, string? Kind, int? StartYear, int? EndYear);

/// <param name="Opens">
/// The two events that bound it. A period is anchored to them rather than to years, which is why
/// switching reckoning moves the band as well as the marks inside it.
/// </param>
internal record PeriodResponse(
    string Slug,
    string Name,
    string? Kind,
    int Level,
    string Realm,
    string? Region,
    string? Uri,
    string? Notes,
    string Source,
    string? SourceId,
    PeriodRefResponse? Parent,
    NamedEntityResponse? Entity,
    EventRefResponse? Opens,
    EventRefResponse? Closes,
    IList<PeriodRefResponse> Inside)
{
    /// <summary>
    /// Start and end in every chronology that can state both, exactly as the timeline sends them:
    /// a chronology that cannot is absent, and a period anchored to no events is keyed by the empty
    /// string, because its years are its own and belong to no reckoning.
    /// </summary>
    public IDictionary<string, int[]> Years { get; init; } = new Dictionary<string, int[]>();
}

/// <param name="LastAnnoMundiBeforeTheCommonEra">The default reckoning's, as the timeline sends it.</param>
internal record PeriodListResponse(
    int LastAnnoMundiBeforeTheCommonEra,
    IList<TimelinePeriodResponse> Items);

/// <param name="LastAnnoMundiBeforeTheCommonEra">The default reckoning's, as the timeline sends it.</param>
internal record ChronologyListResponse(
    int LastAnnoMundiBeforeTheCommonEra,
    IList<ChronologyResponse> Chronologies);

/// <param name="LastAnnoMundiBeforeTheCommonEra">
/// The year from creation that is 1 BCE, so a client can turn every year on this axis into an
/// astronomical one by subtracting it — and can do so without a <c>Date</c>, which is the point.
/// The dataset counts forward from the creation without a sign, and 3,961 is where its era turns.
/// </param>
/// <param name="LastAnnoMundiBeforeTheCommonEra">
/// The year from creation that is 1 BCE in the default reckoning, so a client can turn a year on
/// this axis into an astronomical one by subtracting it — and can do so without a <c>Date</c>,
/// which is the point. Each chronology carries its own; this is the default's.
/// </param>
/// <summary>
/// The pictures the timeline shows small beside its events and periods, read from the encyclopedia
/// rather than found anew: an event's is the one its person's page leads with, or failing that its
/// place's — the place its own page links to, by the same rule. Nothing is looked up that the
/// encyclopedia does not already show, and most events have neither.
/// </summary>
internal sealed class TimelinePictures
{
    private readonly Dictionary<string, EntityThumbnailResponse> _leading;
    private readonly Dictionary<EventLocation, string> _places;

    private TimelinePictures(
        Dictionary<string, EntityThumbnailResponse> leading,
        Dictionary<EventLocation, string> places)
    {
        _leading = leading;
        _places = places;
    }

    public static async Task<TimelinePictures> Of(
        AppDbContext db,
        IReadOnlyCollection<(string? Entity, EventLocation? Location)> events,
        IEnumerable<string?> periodEntities,
        bool generated,
        CancellationToken cancellationToken)
    {
        var places = await EncyclopediaEndpoints.PlacesNamed(db, events.Select(e => e.Location), cancellationToken);
        var slugs = events.Select(e => e.Entity)
            .Concat(places.Values)
            .Concat(periodEntities)
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return new TimelinePictures(await ImageEndpoints.Leading(db, slugs, cancellationToken, generated), places);
    }

    public EntityThumbnailResponse? ForEvent(string? entity, EventLocation? location) =>
        Leading(entity) ?? Leading(location is { } at ? _places.GetValueOrDefault(at) : null);

    public EntityThumbnailResponse? ForPeriod(string? entity) => Leading(entity);

    private EntityThumbnailResponse? Leading(string? slug) => slug is null ? null : _leading.GetValueOrDefault(slug);
}

internal record TimelineResponse(
    int LastAnnoMundiBeforeTheCommonEra,
    IList<ChronologyResponse> Chronologies,
    IList<TimelineEventResponse> Items,
    IList<TimelinePeriodResponse> Periods);

/// <summary>
/// One reckoning of when things happened, and what it rests on.
///
/// Sent alongside the dates rather than resolved into them. The base reckoning and Ussher disagree
/// in 406 of the 419 events they share and by as much as 278 years; Ussher and Shulman agree on
/// none of their 267 and stand 477 years apart at the Tower of Babel. That disagreement is a
/// finding rather than a defect — a corpus that picked one and hid the others would be asserting a
/// chronology no chronologer holds.
/// </summary>
internal record ChronologyResponse(
    string Slug,
    string Name,
    string? Authority,
    string? Basis,
    string? Source,
    int LastAnnoMundiBeforeTheCommonEra,
    bool IsDefault);

/// <param name="Sequenced">
/// Whether this event's place among the others of its year is something a source stated, or only
/// where the list happened to put it.
///
/// The items arrive in the order they are to be drawn, and for most of them that order inside a
/// year is arbitrary — the year is all anybody said. Ussher dates ninety-five paragraphs to AD 33
/// and narrates them in order, so those are a sequence and the crucifixion precedes the
/// resurrection because he says so. Without this a client cannot tell one from the other, and would
/// have to read an arbitrary order as a chronology.
/// </param>
/// <param name="Years">
/// The year each chronology gives this event, keyed by chronology slug. A chronology that says
/// nothing about it is absent rather than null — silence and zero are different facts.
/// </param>
internal record TimelineEventResponse(
    string Slug,
    string Name,
    string? Kind,
    string Realm,
    string? Region,
    string? Uri,
    string? EntitySlug,
    bool Sequenced,
    IDictionary<string, int> Years)
{
    /// <summary>
    /// The picture its person's page leads with, or its place's where the person has none; null for
    /// most, which the encyclopedia has no picture for. Sent only by the timeline.
    /// </summary>
    public EntityThumbnailResponse? Picture { get; init; }

    /// <summary>
    /// The country of the time, where the source states one; <c>Region</c> is today's country or the
    /// last known. See <see cref="EventResponse.RegionAtTheTime"/>.
    /// </summary>
    public string? RegionAtTheTime { get; init; }
}

/// <param name="Level">
/// Which row to draw it on — 0 the eras, 1 the spans of rule and captivity, 2 the lives and
/// ministries — and not its depth in the tree. Every period hangs off an era whatever its level,
/// so nothing sits under a level-1 period: of the 178 that have a parent, 87 are level 1 and 91
/// level 2, and all 178 point at a level-0 era.
/// </param>
/// <param name="ParentSlug">
/// The era this period <em>opens in</em>, which is not always an era that contains it. Drawing a
/// child bar inside its parent's bar without reading <see cref="Years"/> puts it outside in 42 of
/// the 178: a lifespan crosses the Flood because the man outlived it, and the 430 years from the
/// promise to the covenant end two eras after they begin. Those 42 say how far they overrun in
/// <see cref="Notes"/>. The years are the containment; this is only where the band starts.
/// </param>
/// <param name="Years">
/// Start and end, per chronology, as a two-element array. Only a chronology that can state both
/// ends appears. The empty key is a period that carries its own years and belongs to no reckoning.
/// </param>
internal record TimelinePeriodResponse(
    string Slug,
    string Name,
    string? Kind,
    int Level,
    string Realm,
    string? Region,
    string? Uri,
    string? ParentSlug,
    string? EntitySlug,
    string? Notes,
    IDictionary<string, int[]> Years)
{
    /// <summary>The picture its person's page leads with; null where there is none, and on the period list.</summary>
    public EntityThumbnailResponse? Picture { get; init; }
}
