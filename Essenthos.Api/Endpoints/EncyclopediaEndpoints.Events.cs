using System.Linq.Expressions;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Endpoints;

internal static partial class EncyclopediaEndpoints
{
    /// <summary>The events, a page at a time, and one event whole.</summary>
    private static void MapEvents(IEndpointRouteBuilder routes)
    {
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
    }

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
    /// This is the base reckoning's <c>last_year_before_the_common_era</c> — BibleData's, on whose
    /// count every year from creation stands whichever reckoning is the default — and every event
    /// with a date row reads its own reckoning's instead. It stands alone in two places only: the
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
            // it is the same number the base reckoning holds.
            Era = reckoning?.Era
                  ?? (e.YearFromCreation > LastYearBeforeChrist ? CommonEra : BeforeTheCommonEra),
        };
    }
}
