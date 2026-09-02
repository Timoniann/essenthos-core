using System.Linq.Expressions;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Endpoints;

/// <summary>
/// The people and places the text names, what it calls them, how they stand to one another, and
/// when the source thinks things happened to them.
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
            e.Verses.Count);

    /// <summary>The addresses of a set of namings, each address once.</summary>
    internal static IQueryable<int> Addresses(IQueryable<EntityVerse> namings) =>
        namings
            .Select(v => (v.CanonicalBook * BookStride) + (v.CanonicalChapter * ChapterStride) + v.CanonicalVerse)
            .Distinct();

    public static void MapEncyclopedia(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/entities", async (
            [FromQuery] string? q,
            [FromQuery] string? kind,
            [FromQuery] int? skip,
            [FromQuery] int? take,
            AppDbContext db,
            CancellationToken cancellationToken) =>
        {
            var entities = db.Entities.AsQueryable();

            if (kind is { Length: > 0 })
            {
                if (kind is not ("person" or "place"))
                {
                    return Results.BadRequest(new ProblemResponse(
                        $"\"{kind}\" is not a kind of entity. Try person or place."));
                }

                var wanted = kind == "place" ? EntityKind.Place : EntityKind.Person;
                entities = entities.Where(e => e.Kind == wanted);
            }

            if (q is { Length: > 0 })
            {
                // The name as printed, and every other name the entity is called by — Peter is
                // Simon, Cephas and Simon Bar-Jonah, and a search that only reads the headword
                // finds him under one of the four.
                var like = LikePatterns.Containing(q);
                entities = entities.Where(e =>
                    EF.Functions.ILike(e.Name, like)
                    || EF.Functions.ILike(e.Slug, like)
                    || e.Names.Any(n => EF.Functions.ILike(n.Label, like)));
            }

            var total = await entities.CountAsync(cancellationToken);
            var page = await entities
                .OrderBy(e => e.Name).ThenBy(e => e.Slug)
                .Skip(Math.Max(0, skip ?? 0))
                .Take(Math.Clamp(take ?? 40, 1, MostPerPage))
                .Select(Summary)
                .ToListAsync(cancellationToken);

            return Results.Ok(new EntityListResponse(total, page));
        });

        routes.MapGet("/entities/{slug}", async (
            string slug,
            AppDbContext db,
            CancellationToken cancellationToken) =>
        {
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
                    e.ModernEquivalent,
                    e.Notes,
                    e.OpenBibleId,
                    e.Source,
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (entity is null)
            {
                return ApiResults.NotFound($"There is no person or place \"{slug}\".");
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

            // Both directions at once: a father is not recorded twice, so reading only one side
            // would give Isaac a father and no sons.
            var outward = await db.EntityRelationships
                .Where(r => r.FromEntityId == entity.Id)
                .Select(r => new EntityRelationshipResponse(
                    r.Type, r.Category, r.To!.Slug, r.To.Name, r.To.Distinguisher, false,
                    Reference(r.CanonicalBook, r.CanonicalChapter, r.CanonicalVerse), r.Notes))
                .ToListAsync(cancellationToken);

            var inward = await db.EntityRelationships
                .Where(r => r.ToEntityId == entity.Id)
                .Select(r => new EntityRelationshipResponse(
                    r.Type, r.Category, r.From!.Slug, r.From.Name, r.From.Distinguisher, true,
                    Reference(r.CanonicalBook, r.CanonicalChapter, r.CanonicalVerse), r.Notes))
                .ToListAsync(cancellationToken);

            var events = await db.Events
                .Where(e => e.EntityId == entity.Id)
                .OrderBy(e => e.YearFromCreation)
                .Select(Rows)
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
                [.. outward, .. inward],
                [.. events.Select(Event)]));
        });

        routes.MapGet("/entities/{slug}/references", async (
            string slug,
            [FromQuery] int? skip,
            [FromQuery] int? take,
            AppDbContext db,
            CancellationToken cancellationToken) =>
        {
            var entity = await db.Entities.Where(e => e.Slug == slug).Select(e => e.Id)
                .FirstOrDefaultAsync(cancellationToken);
            if (entity == 0)
            {
                return ApiResults.NotFound($"There is no person or place \"{slug}\".");
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
                .Select(v => new { v.CanonicalBook, v.CanonicalChapter, v.CanonicalVerse, v.Label, v.Disputed })
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
                            [.. at.Select(v => new EntityNamingResponse(v.Label, v.Disputed))],
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
            var page = await events
                .OrderBy(e => e.YearFromCreation).ThenBy(e => e.Slug)
                .Skip(Math.Max(0, skip ?? 0))
                .Take(Math.Clamp(take ?? 50, 1, MostPerPage))
                .Select(Rows)
                .ToListAsync(cancellationToken);

            return Results.Ok(new EventListResponse(total, [.. page.Select(Event)]));
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
            return row is null ? Results.NotFound() : Results.Ok(Event(row));
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
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (period is null)
            {
                return Results.NotFound();
            }

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
                children));
        });

        routes.MapGet("/timeline", async (AppDbContext db, CancellationToken cancellationToken) =>
        {
            var chronologies = await db.Chronologies
                .OrderBy(c => c.Position)
                .ToListAsync(cancellationToken);

            var events = await db.Events
                .OrderBy(e => e.YearFromCreation).ThenBy(e => e.Slug)
                .Select(e => new
                {
                    e.Id,
                    e.Slug,
                    e.Name,
                    e.Kind,
                    e.Realm,
                    e.Region,
                    e.Uri,
                    EntitySlug = e.Entity == null ? null : e.Entity.Slug,
                })
                .ToListAsync(cancellationToken);

            var dates = await db.EventDates
                .Select(d => new { d.EventId, d.ChronologyId, d.Year })
                .ToListAsync(cancellationToken);

            var reckoning = chronologies.ToDictionary(c => c.Id, c => c.Slug);
            var years = new Dictionary<int, Dictionary<string, int>>(events.Count);
            foreach (var date in dates.Where(d => d.Year is not null))
            {
                if (!years.TryGetValue(date.EventId, out var byChronology))
                {
                    byChronology = [];
                    years[date.EventId] = byChronology;
                }

                byChronology[reckoning[date.ChronologyId]] = date.Year!.Value;
            }

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

            return Results.Ok(new TimelineResponse(
                LastYearBeforeChrist,
                [
                    .. chronologies.Select(c => new ChronologyResponse(
                        c.Slug, c.Name, c.Authority, c.Basis, c.Source,
                        c.LastYearBeforeTheCommonEra, c.IsDefault)),
                ],
                [
                    .. events.Select(e => new TimelineEventResponse(
                        e.Slug,
                        e.Name,
                        e.Kind,
                        e.Realm,
                        e.Region,
                        e.Uri,
                        e.EntitySlug,
                        years.GetValueOrDefault(e.Id) ?? [])),
                ],
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
                ]));
        });
    }

    /// <summary>
    /// A period's years, in every chronology that can state both of them.
    ///
    /// Both ends or neither. A band whose start came from Ussher and whose end came from the base
    /// reckoning is a duration nobody computed, and drawing one would be the exact failure this
    /// whole model exists to avoid — Ussher is up to 236 years from the base, so such a band could
    /// be wrong by two centuries while looking authoritative.
    /// </summary>
    private static Dictionary<string, int[]> Span(
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
    private static IList<string> Numbers(string? stored) =>
        stored is { Length: > 0 } ? stored.Split(',') : [];

    private static VerseRefResponse? Reference(int? book, int? chapter, int? verse) =>
        book is { } ordinal && chapter is { } inChapter && verse is { } atVerse
            ? new VerseRefResponse(
                ordinal, BookReferences.Name(ordinal), BookReferences.Slug(ordinal), inChapter, atVerse)
            : null;

    /// <summary>
    /// The event with the two fields that need a join, read in the query rather than off a
    /// navigation property. Projecting <c>e.Entity.Slug</c> through a method EF cannot translate
    /// left every one of the 572 events without a person on it, while the filter that uses the
    /// same navigation worked — so the timeline could select by a person and never name one.
    /// </summary>
    private sealed record EventRow(
        Database.Entities.Event Event,
        string? EntitySlug,
        string? EntityName,
        IList<EventDateResponse> Dates);

    /// <summary>
    /// Which side of the era a year falls. The source counts forward from the creation without a
    /// sign, so its year 3,969 answers <c>8</c> meaning AD 8, and nothing in the number says so.
    /// The turn is at 3,961, and it is arithmetic rather than a guess: below it the BCE year is
    /// 3,962 less the count, above it the AD year is the count less 3,961.
    /// </summary>
    private const int LastYearBeforeChrist = 3961;

    /// <summary>
    /// An event with the people and the dates it needs, projected in the query.
    ///
    /// One expression, used by both places that return events, because reading a navigation
    /// property outside the projection is what left all 572 events without a person once already
    /// while the filter over the same navigation went on working.
    /// </summary>
    private static readonly System.Linq.Expressions.Expression<Func<Database.Entities.Event, EventRow>> Rows =
        e => new EventRow(
            e,
            e.Entity == null ? null : e.Entity.Slug,
            e.Entity == null ? null : e.Entity.Name,
            e.Dates
                .OrderBy(d => d.Chronology!.Position)
                .Select(d => new EventDateResponse(
                    d.Chronology!.Slug,
                    d.Chronology.Name,
                    d.Year,
                    d.Year == null
                        ? null
                        : d.Year <= d.Chronology.LastYearBeforeTheCommonEra
                            ? d.Chronology.LastYearBeforeTheCommonEra - d.Year.Value + 1
                            : d.Year.Value - d.Chronology.LastYearBeforeTheCommonEra,
                    d.Year != null && d.Year > d.Chronology.LastYearBeforeTheCommonEra ? "CE" : "BCE",
                    d.EarliestYear,
                    d.LatestYear,
                    d.Calculation,
                    d.Citation,
                    d.Notes))
                .ToList());

    private static EventResponse Event(EventRow row) =>
        Event(row.Event, row.EntitySlug, row.EntityName, row.Dates);

    private static EventResponse Event(
        Database.Entities.Event e,
        string? entitySlug,
        string? entityName,
        IList<EventDateResponse> dates) => new(
        e.Slug,
        e.Name,
        e.Description,
        e.Kind,
        entitySlug,
        entityName,
        e.YearFromCreation,
        e.BceYear,
        e.AgeAtEvent,
        e.Calculation,
        Reference(e.CanonicalBook, e.CanonicalChapter, e.CanonicalVerse),
        e.Location,
        e.Realm,
        e.Region,
        e.Uri,
        e.Notes,
        e.Source,
        Datasets.Of(e.Source),
        dates)
    {
        Era = e.YearFromCreation is { } year && year > LastYearBeforeChrist ? "AD" : "BCE",
    };
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
    int Mentions);

internal record EntityListResponse(int Total, IList<EntitySummaryResponse> Items);

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
    string Source,
    string? SourceId,
    int References,
    int Mentions,
    int Disputed,
    IList<EntityNameResponse> Names,
    IList<EntityRelationshipResponse> Relationships,
    IList<EventResponse> Events);

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

/// <param name="Category">
/// <c>explicit</c> where a verse says it, <c>inferred</c> where the source worked it out. Keeping
/// the two apart is the same discipline the link table applies to words.
/// </param>
/// <param name="Inward">
/// True when this is the other entity's relationship read backwards — Isaac is recorded as the son
/// of Abraham, and Abraham's page shows the same row from his side.
/// </param>
internal record EntityRelationshipResponse(
    string Type,
    string Category,
    string Slug,
    string Name,
    string? Distinguisher,
    bool Inward,
    VerseRefResponse? Reference,
    string? Notes);

/// <summary>What the text calls the entity at one verse, and whether that word settles who it is.</summary>
internal record EntityNamingResponse(string? Label, bool Disputed);

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
/// owed the disagreement, not a winner — and they disagree in 413 of the 419 events they share.
/// </param>
/// <param name="Source">
/// Who compiled this row and under what licence. Per row rather than per corpus, because they
/// differ: the Old Testament chronology is CC BY 4.0, the New Testament CC BY-SA 4.0, and the
/// world layer CC0. A page showing one licence for all three would assert what none of them says.
/// </param>
internal record EventResponse(
    string Slug,
    string Name,
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
    /// <c>BCE</c> or <c>AD</c>. The source writes its year without a sign and keeps counting past
    /// the turn, so its <c>8</c> may mean 8 BCE or AD 8 and the number alone cannot say which.
    /// </summary>
    public string Era { get; init; } = "BCE";
}

/// <param name="Calculation">
/// The arithmetic that produced this reckoning's year, where the reckoning shows its working.
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
    string? Notes);

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
    IList<PeriodRefResponse> Inside);

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
internal record TimelineResponse(
    int LastAnnoMundiBeforeTheCommonEra,
    IList<ChronologyResponse> Chronologies,
    IList<TimelineEventResponse> Items,
    IList<TimelinePeriodResponse> Periods);

/// <summary>
/// One reckoning of when things happened, and what it rests on.
///
/// Sent alongside the dates rather than resolved into them. They disagree in 413 of 419 shared
/// events and by as much as 236 years, and that disagreement is a finding rather than a defect —
/// a corpus that picked one and hid the others would be asserting a chronology no chronologer
/// holds.
/// </summary>
internal record ChronologyResponse(
    string Slug,
    string Name,
    string? Authority,
    string? Basis,
    string? Source,
    int LastAnnoMundiBeforeTheCommonEra,
    bool IsDefault);

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
    IDictionary<string, int> Years);

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
    IDictionary<string, int[]> Years);
