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
        // The timeline: the narrowed events in the reader's order of reckonings, a page at a time, ordered and cut in SQL
        // by the order the build stored (EventWindow).
        routes.MapGet("/events", async (
            [FromQuery] string? entity,
            [FromQuery] int? fromYear,
            [FromQuery] int? toYear,
            [FromQuery] string? realm,
            [FromQuery] string? kind,
            [FromQuery] string? notKind,
            [FromQuery] string? reckonings,
            [FromQuery] string? q,
            [FromQuery] string? worldRegions,
            [FromQuery] string? around,
            [FromQuery] int? skip,
            [FromQuery] int? take,
            [FromQuery] string? language,
            [FromQuery] string? prose,
            AppDbContext db,
            CancellationToken cancellationToken) =>
        {
            var filter = new EventFilter(entity, fromYear, toYear, realm, kind, notKind) { WorldRegions = worldRegions };
            var preference = Preference(reckonings);
            var found = await Searched(db, filter, q, language, prose, cancellationToken);
            var window = await EventWindow.Read(
                db, filter, found, preference, Math.Max(0, skip ?? 0), Math.Clamp(take ?? 50, 1, MostPerPage),
                preference.Count > 0 ? around : null, cancellationToken);
            var items = await ListItems(db, window.Rows, preference.Count > 0, language, prose, cancellationToken);
            return Results.Ok(preference.Count == 0
                ? new EventListResponse(window.Total, items)
                : new EventListResponse(window.Total, items) { Skip = window.Start, Centuries = window.Centuries });
        });

        // Where one event stands in a list narrowed and ordered as the list was, its neighbours either side, and the
        // events around it in that list, so an event's page steps through and shows the list the reader came from in one
        // request.
        routes.MapGet("/events/{slug}/step", async (
            string slug,
            [FromQuery] string? entity,
            [FromQuery] int? fromYear,
            [FromQuery] int? toYear,
            [FromQuery] string? realm,
            [FromQuery] string? kind,
            [FromQuery] string? notKind,
            [FromQuery] string? reckonings,
            [FromQuery] string? q,
            [FromQuery] string? worldRegions,
            [FromQuery] int? near,
            [FromQuery] string? language,
            [FromQuery] string? prose,
            AppDbContext db,
            CancellationToken cancellationToken) =>
        {
            var filter = new EventFilter(entity, fromYear, toYear, realm, kind, notKind) { WorldRegions = worldRegions };
            var preference = Preference(reckonings);
            var found = await Searched(db, filter, q, language, prose, cancellationToken);
            var window = await EventWindow.Read(
                db, filter, found, preference, 0, Math.Clamp(near ?? NearShown, 3, MostNear), slug, cancellationToken);
            if (window.Target is not { } at)
            {
                return window.Known ? Results.Ok(new EventStepResponse(null, window.Total, null, null)) : Results.NotFound();
            }

            var items = await ListItems(db, window.Rows, preference.Count > 0, language, prose, cancellationToken);
            EventNeighbourResponse? Beside(int position) =>
                position - window.Start is var i && i >= 0 && i < items.Count
                    ? new EventNeighbourResponse(items[i].Slug, items[i].Name, items[i].LocalName)
                    : null;
            return Results.Ok(new EventStepResponse(at + 1, window.Total, Beside(at - 1), Beside(at + 1))
            {
                Near = items,
                NearSkip = window.Start,
            });
        });

        // One event, with everything the trimmed timeline payload leaves out.
        //
        // The timeline sends 1,508 events and cannot afford a description apiece; a reader who has
        // picked one wants exactly that, plus the arithmetic and every reckoning's answer. So it is
        // fetched when asked for rather than carried for everything on the chance it is.
        routes.MapGet("/events/{slug}", async (
            string slug,
            [FromQuery] string? language,
            [FromQuery] string? prose,
            AppDbContext db,
            SiteSettingsFile settings,
            [FromServices] PeriodLists? periods,
            CancellationToken cancellationToken) =>
            await EventDetail(db, settings, slug, language, prose, cancellationToken, periods) is { } found
                ? Results.Ok(found)
                : Results.NotFound());
    }

    /// <summary>One event as <c>/v1/events/{slug}</c> answers it; null where there is none.</summary>
    internal static async Task<EventResponse?> EventDetail(
        AppDbContext db,
        SiteSettingsFile settings,
        string slug,
        string? language,
        string? prose,
        CancellationToken cancellationToken,
        PeriodLists? lists = null)
    {
        var row = await db.Events.Where(e => e.Slug == slug).Select(Rows).FirstOrDefaultAsync(cancellationToken);
        if (row is null)
        {
            return null;
        }

        var places = await PlacesNamed(db, [EventLocation.Of(row.Event)], cancellationToken);
        var one = Event((await Aged(db, [row], cancellationToken))[0], places);

        // A passage that runs on into the next chapter needs the end of the chapter it starts in.
        var crossing = one.Passages.Where(p => p.LastChapter > p.Chapter).ToList();
        if (crossing.Count > 0)
        {
            var books = crossing.Select(p => p.Book).Distinct().ToList();
            var chapters = crossing.Select(p => p.Chapter).Distinct().ToList();
            var ends = await db.VerseReferences
                .Where(r => r.IsPrimary && books.Contains(r.CanonicalBook) && chapters.Contains(r.CanonicalChapter))
                .GroupBy(r => new { r.CanonicalBook, r.CanonicalChapter })
                .Select(g => new { g.Key.CanonicalBook, g.Key.CanonicalChapter, Last = g.Max(r => r.CanonicalVerse) })
                .ToListAsync(cancellationToken);
            one = one with
            {
                Quoted = QuotedVerses.Verses(
                    one.Passages,
                    (book, chapter) => ends.FirstOrDefault(e => e.CanonicalBook == book && e.CanonicalChapter == chapter)?.Last),
            };
        }

        var read = (await InTheReadersLanguage(db, [one], language, prose, cancellationToken))[0];
        var periods = read.YearFromCreation is { } year
            ? [.. (lists is null ? await PeriodList(db, language, cancellationToken) : await lists.Get(language, cancellationToken))
                .Items.Where(p => p.Years.Values.Any(
                span => span.Length == 2 && Math.Min(span[0], span[1]) <= year && year <= Math.Max(span[0], span[1])))]
            : new List<TimelinePeriodResponse>();
        return read with
        {
            Credits = DatasetCredits.Of([read.SourceId], settings),
            Periods = periods,
        };
    }

    /// <summary>How many events around one the step answers when the page does not say.</summary>
    private const int NearShown = 9;

    private const int MostNear = 25;

    /// <summary>
    /// The ids of the narrowed events whose title, person or place holds the reader's words, in English or in the
    /// reader's language: the title's machine translation, the person's name as the reader's text gives it. Case,
    /// accents and the apostrophe's spellings do not matter, and every word typed must be found. In memory, over the
    /// narrowed list's two thousand titles at most; null where nothing was typed, which leaves the list untouched.
    /// </summary>
    internal static async Task<List<int>?> Searched(
        AppDbContext db,
        EventFilter filter,
        string? query,
        string? language,
        string? prose,
        CancellationToken cancellationToken)
    {
        var words = Folded(query ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            return null;
        }

        var rows = await EventWindow.Titles(db, filter, cancellationToken);
        var titles = await TextTranslations.Of(
            db, rows.SelectMany(r => new[] { r.Name, r.Location }), ReaderLanguages.Prose(prose, language), cancellationToken);
        var people = await EntityNames.Of(db, [.. rows.Select(r => r.EntityId).OfType<int>().Distinct()], language, cancellationToken);

        return
        [
            .. rows
                .Where(r =>
                {
                    var said = Folded(string.Join(' ', new[]
                    {
                        r.Name,
                        TextTranslations.In(titles, r.Name),
                        r.Location,
                        TextTranslations.In(titles, r.Location),
                        r.Person,
                        r.EntityId is { } person ? people.GetValueOrDefault(person) : null,
                    }.OfType<string>()));
                    return words.All(word => said.Contains(word, StringComparison.Ordinal));
                })
                .Select(r => r.Id),
        ];
    }

    /// <summary>Lower case, without accents, every apostrophe one apostrophe and every space a plain one.</summary>
    internal static string Folded(string text)
    {
        var decomposed = text.ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD);
        var folded = new System.Text.StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            folded.Append(c is '’' or 'ʼ' or '‘' or '`' ? '\'' : char.IsWhiteSpace(c) ? ' ' : c);
        }

        return folded.ToString().Normalize(System.Text.NormalizationForm.FormC);
    }

    /// <summary>What a list of events is narrowed to: each part absent where the reader did not narrow by it.</summary>
    internal sealed record EventFilter(
        string? Entity,
        int? FromYear,
        int? ToYear,
        string? Realm,
        string? Kinds,
        string? NotKinds)
    {
        /// <summary>
        /// The countries a world event must be in to be listed, as Wikidata names them (today's country,
        /// <see cref="Database.Entities.Event.Region"/>), comma separated; Scripture's events are not
        /// narrowed by it. A world event with no country is left out while this is asked for.
        /// </summary>
        public string? WorldRegions { get; init; }
    }

    internal static List<string> Listed(string? commaSeparated) =>
        commaSeparated is null
            ? []
            : [.. commaSeparated.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct()];

    /// <summary>The reckonings a reader asked for, the preferred first; empty where none was asked for.</summary>
    internal static List<string> Preference(string? reckonings) => Listed(reckonings);

    /// <summary>
    /// The slugs of these events in the base order: by the year from creation, the events of one year in the
    /// order the text tells them (the stored <see cref="Database.Entities.Event.InYearRank"/>), and those with no
    /// year last.
    /// </summary>
    internal static Task<List<string>> InOrder(
        IQueryable<Database.Entities.Event> events,
        CancellationToken cancellationToken) =>
        events
            .OrderBy(e => e.YearFromCreation == null)
            .ThenBy(e => e.YearFromCreation)
            .ThenBy(e => e.InYearRank)
            .ThenBy(e => e.Slug)
            .Select(e => e.Slug)
            .ToListAsync(cancellationToken);

    /// <summary>These items in the order of <paramref name="slugs"/>.</summary>
    internal static List<T> Ordered<T>(IEnumerable<T> items, IReadOnlyList<string> slugs, Func<T, string> slug)
    {
        var at = new Dictionary<string, int>(slugs.Count, StringComparer.Ordinal);
        for (var i = 0; i < slugs.Count; i++)
        {
            at[slugs[i]] = i;
        }

        return [.. items.OrderBy(item => at.GetValueOrDefault(slug(item), int.MaxValue))];
    }

    /// <summary>Text compared with each run of digits read as a number.</summary>
    internal sealed class NumberAwareOrder : IComparer<string>
    {
        public static readonly NumberAwareOrder Instance = new();

        public int Compare(string? x, string? y)
        {
            if (x is null || y is null)
            {
                return string.CompareOrdinal(x, y);
            }

            int i = 0, j = 0;
            while (i < x.Length && j < y.Length)
            {
                if (char.IsAsciiDigit(x[i]) && char.IsAsciiDigit(y[j]))
                {
                    var a = i;
                    var b = j;
                    while (i < x.Length && char.IsAsciiDigit(x[i]))
                    {
                        i++;
                    }

                    while (j < y.Length && char.IsAsciiDigit(y[j]))
                    {
                        j++;
                    }

                    var left = x[a..i].TrimStart('0');
                    var right = y[b..j].TrimStart('0');
                    var byNumber = left.Length != right.Length
                        ? left.Length.CompareTo(right.Length)
                        : string.CompareOrdinal(left, right);
                    if (byNumber != 0)
                    {
                        return byNumber;
                    }
                }
                else
                {
                    var byChar = x[i].CompareTo(y[j]);
                    if (byChar != 0)
                    {
                        return byChar;
                    }

                    i++;
                    j++;
                }
            }

            return (x.Length - i).CompareTo(y.Length - j);
        }
    }

    /// <summary>
    /// The events of a window as lines of a list, in its order: each with the reckoning it is shown by where the list was
    /// ordered by a reader's preference, and its title, place and region in the reader's language.
    /// </summary>
    private static async Task<IList<EventListItemResponse>> ListItems(
        AppDbContext db,
        IReadOnlyList<EventWindow.Placed> placed,
        bool preferred,
        string? language,
        string? prose,
        CancellationToken cancellationToken)
    {
        if (placed.Count == 0)
        {
            return [];
        }

        var ids = placed.Select(p => p.Id).ToList();
        var at = ids.Select((id, index) => (id, index)).ToDictionary(p => p.id, p => p.index);
        var rows = (await db.Events.Where(e => ids.Contains(e.Id)).Select(Rows).ToListAsync(cancellationToken))
            .OrderBy(row => at[row.Event.Id])
            .ToList();
        var places = await PlacesNamed(db, rows.Select(row => EventLocation.Of(row.Event)), cancellationToken);

        // Only what a line names is put into the reader's language.
        List<EventResponse> lines =
        [
            .. rows.Select(row => Event(row, places)).Select(e => e with
            {
                Description = null,
                Notes = null,
                Dates = [.. e.Dates.Select(d => d with { Calculation = null, Notes = null, Working = null })],
            }),
        ];
        var answered = await InTheReadersLanguage(db, lines, language, prose, cancellationToken);
        return [.. answered.Select((one, index) => EventListItemResponse.Of(one, preferred ? placed[index].ShownBy : null))];
    }

    private const string CommonEra = EventRows.CommonEra;

    private const string BeforeTheCommonEra = EventRows.BeforeTheCommonEra;

    /// <inheritdoc cref="EventRows.Rows"/>
    internal static readonly System.Linq.Expressions.Expression<Func<Database.Entities.Event, EventRow>> Rows = EventRows.Rows;

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
    internal const int LastYearBeforeChrist = 3961;

    /// <summary>The reckoning whose years the source's own age at the event belongs to.</summary>
    private const string BibleDataReckoning = "bibledata";

    private const string Birth = "Birth";

    /// <summary>
    /// The rows with each date's age at the event, counted in that date's own reckoning.
    ///
    /// <para>
    /// The source gives one age per event and it is BibleData's: Abram leaves Ur at 70 in its year
    /// 2080 and Haran at 75 in 2085. Ussher puts both departures in his year 2083, so his years
    /// beside BibleData's ages had Abram 70 and 75 in one year. Each reckoning's age is therefore its
    /// own year of the event less its own year of the person's birth, and BibleData keeps the age its
    /// row states. One query for the births of every person on the page. A birth itself carries no
    /// age: the source's is always 0, which tells a reader nothing.
    /// </para>
    /// </summary>
    internal static async Task<List<EventRow>> Aged(
        AppDbContext db,
        IReadOnlyList<EventRow> rows,
        CancellationToken cancellationToken)
    {
        var people = rows
            .Where(row => row.Event.AgeAtEvent is not null)
            .Select(row => row.Event.EntityId)
            .OfType<int>()
            .Distinct()
            .ToList();
        if (people.Count == 0)
        {
            return [.. rows];
        }

        var births = (await db.EventDates
                .Where(d => d.Event!.Kind == Birth && d.Event.EntityId != null && people.Contains(d.Event.EntityId.Value))
                .Select(d => new
                {
                    Person = d.Event!.EntityId!.Value,
                    d.Chronology!.Slug,
                    d.Year,
                    d.StatedYear,
                    d.Chronology.LastYearBeforeTheCommonEra,
                })
                .ToListAsync(cancellationToken))
            .GroupBy(b => (b.Person, b.Slug))
            .Where(g => g.Count() == 1)
            .ToDictionary(g => g.Key, g => g.Single());

        return
        [
            .. rows.Select(row => row.Event.Kind == Birth
                ? row with { Dates = [.. row.Dates.Select(date => date with { AgeAtEvent = null })] }
                : row.Event.AgeAtEvent is null || row.Event.EntityId is not { } person
                ? row
                : row with
                {
                    Dates =
                    [
                        .. row.Dates.Select(date => date with
                        {
                            AgeAtEvent = date.Chronology == BibleDataReckoning
                                ? row.Event.AgeAtEvent
                                : births.TryGetValue((person, date.Chronology), out var birth)
                                    ? Age(Astronomical(date.Year, date.BceYear, date.Era, birth.LastYearBeforeTheCommonEra),
                                        Astronomical(birth.Year, birth.StatedYear, birth.LastYearBeforeTheCommonEra))
                                    : null,
                        }),
                    ],
                }),
        ];
    }

    /// <summary>The years between, or null where either is missing or the event would come before the birth.</summary>
    private static int? Age(int? at, int? born) =>
        at is { } year && born is { } birth && year >= birth ? year - birth : null;

    /// <summary>
    /// A date as one count of years: the reckoning's year from creation where it states one, and
    /// otherwise its year of the common era moved onto that count, so a paragraph Ussher dates only
    /// BC is still counted against a birth he dates in anno mundi.
    /// </summary>
    private static int? Astronomical(int? year, int? bceYear, string era, int lastBeforeTheCommonEra) =>
        year ?? (bceYear is not { } stated ? null
            : era == CommonEra ? lastBeforeTheCommonEra + stated
            : lastBeforeTheCommonEra - stated + 1);

    private static int? Astronomical(int? year, int? statedYear, int lastBeforeTheCommonEra) =>
        year ?? (statedYear is not { } stated ? null
            : stated > 0 ? lastBeforeTheCommonEra + stated
            : lastBeforeTheCommonEra + stated + 1);

    /// <summary>
    /// The events with what the source wrote in English rendered in the reader's language where a
    /// machine translation of it is held — title, description, notes, region, each reckoning's
    /// arithmetic — and the place named as the reader's text names it. The source's words stay beside
    /// them. One read for the translations and one for the places, whatever the number of events.
    /// </summary>
    internal static async Task<IList<EventResponse>> InTheReadersLanguage(
        AppDbContext db,
        IReadOnlyList<EventResponse> events,
        string? language,
        string? prose,
        CancellationToken cancellationToken)
    {
        var words = ReaderLanguages.Prose(prose, language);
        var articles = await WikipediaLinks.OfEvents(
            db, [.. events.Where(e => e.Realm == Realms.World).Select(e => e.Slug)], words, cancellationToken);
        events = [.. events.Select(e => articles.TryGetValue(e.Slug, out var article) ? e with { Wikipedia = article } : e)];
        if (events.Count == 0 || (EntityNames.Local(words) is null && EntityNames.Local(language) is null))
        {
            return [.. events.Select(Unstarred)];
        }

        var rendered = await TextTranslations.Of(
            db,
            events.SelectMany(e => new[] { e.Name, e.Description, e.Notes, e.Location, e.Region, e.RegionAtTheTime }
                .Concat(e.Dates.SelectMany(d => new[] { d.Calculation, d.Notes, d.Working?.Event, d.Working?.From, d.Working?.Abram }))),
            words,
            cancellationToken);

        var slugs = events.Select(e => e.LocationSlug).OfType<string>().Distinct().ToList();
        var places = slugs.Count == 0
            ? []
            : await db.Entities.Where(e => slugs.Contains(e.Slug)).Select(e => new { e.Id, e.Slug })
                .ToListAsync(cancellationToken);
        var named = await EntityNames.Of(db, [.. places.Select(p => p.Id)], language, cancellationToken);
        var placeNames = places.Where(p => named.ContainsKey(p.Id)).ToDictionary(p => p.Slug, p => named[p.Id]);

        return
        [
            .. events.Select(e => e with
            {
                LocalName = TextTranslations.In(rendered, e.Name),
                LocalDescription = TextTranslations.In(rendered, e.Description),
                LocalNotes = TextTranslations.In(rendered, e.Notes),
                LocalLocation = e.LocationSlug is { } slug && placeNames.TryGetValue(slug, out var place)
                    ? place
                    : TextTranslations.In(rendered, e.Location),
                LocalRegion = TextTranslations.In(rendered, e.Region),
                LocalRegionAtTheTime = TextTranslations.In(rendered, e.RegionAtTheTime),
                Dates =
                [
                    .. e.Dates.Select(d => d with
                    {
                        LocalCalculation = TextTranslations.In(rendered, d.Calculation),
                        LocalNotes = TextTranslations.In(rendered, d.Notes),
                        Working = d.Working is not { } working ? null : working with
                        {
                            Event = TextTranslations.In(rendered, working.Event) ?? working.Event,
                            From = TextTranslations.In(rendered, working.From) ?? working.From,
                            Abram = TextTranslations.In(rendered, working.Abram) ?? working.Abram,
                        },
                    }),
                ],
            }).Select(Unstarred),
        ];
    }

    /// <summary>The titles without the source's asterisk, once their translations are found by the starred English.</summary>
    private static EventResponse Unstarred(EventResponse e) =>
        e with { Name = EventMarks.Unstarred(e.Name), LocalName = EventMarks.UnstarredOrNull(e.LocalName) };

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
        dates =
        [
            .. dates.Select(d => (d.Parts is null ? d : d with { Working = Arithmetic.Read(d.Parts), Parts = null }) with
            {
                Notes = QuotedVerses.WithoutQuotations(e.Slug, e.Source, d.Notes),
            }),
        ];
        var reckoning = dates.FirstOrDefault(d => d.Chronology == defaultChronology);
        var quoting = QuotedVerses.Read(e.Slug, e.Source, e.Description);

        return new EventResponse(
            e.Slug,
            e.Name,
            e.NameSource,
            quoting.Remainder,
            e.Kind,
            entitySlug,
            entityName,
            e.YearFromCreation,
            reckoning?.BceYear,
            e.Kind == Birth ? null : e.AgeAtEvent,
            e.Calculation,
            BookReferences.At(e.CanonicalBook, e.CanonicalChapter, e.CanonicalVerse),
            e.Location,
            e.Realm,
            e.Region,
            e.Uri,
            QuotedVerses.WithoutQuotations(e.Slug, e.Source, e.Notes),
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
            Passages = quoting.Passages,
            Quoted = QuotedVerses.Verses(quoting.Passages),
            Mark = EventMarks.Of(e.Notes),
        };
    }
}
