using Essenthos.Core.Configuration;
using Essenthos.Core.Corpus;
using System.Linq.Expressions;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Endpoints;

/// <summary>
/// What one chapter speaks of: the people, places, peoples, titles and terms it names, the events
/// the sources set in it, and the periods it falls in — drawn from records the encyclopedia already
/// holds, for a panel beside the reader.
///
/// A record is here because a word of some text in the chapter names it, or because the record's
/// own list of verses says it is named here. Both, not either: the word layer reaches every text
/// the corpus aligns, so a man the English calls <em>he</em> and the Hebrew names is still found,
/// and the verse lists reach the places the word layer has not resolved. Both are addressed in the
/// shared canonical frame, which is the numbering the reader's rows already use.
/// </summary>
internal static class ContextEndpoints
{
    /// <summary>
    /// The one kind of period a chapter is never placed in. Eras, reigns, judgeships and the spans
    /// below them say where a chapter stands; a lifetime does not, because every chapter falls inside
    /// the lives of dozens of people it never mentions.
    /// </summary>
    private const string LifeKind = "life";

    public static void MapContext(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/context/{book}/{chapter:int}", async (
            string book,
            int chapter,
            [FromQuery] string? language,
            AppDbContext db,
            ICanonIndex canon,
            ContextWeightsCache weights,
            SiteSettingsFile settings,
            CancellationToken cancellationToken) =>
        {
            var (ordinal, refusal) = await Chapter(canon, book, chapter, cancellationToken);
            return refusal ?? Results.Ok(await Context(
                db, ordinal, chapter, language, await weights.Get(cancellationToken), cancellationToken,
                settings.Is(SiteSettings.GeneratedImages)));
        });

        // The families among the chapter's people, apart from the rest because a genealogy's trees
        // are the one part of the panel that can run to hundreds of people.
        routes.MapGet("/context/{book}/{chapter:int}/family", async (
            string book,
            int chapter,
            [FromQuery] string? language,
            AppDbContext db,
            ICanonIndex canon,
            CancellationToken cancellationToken) =>
        {
            var (ordinal, refusal) = await Chapter(canon, book, chapter, cancellationToken);
            return refusal ?? Results.Ok(await ChapterFamily.Of(db, ordinal, chapter, language, cancellationToken));
        });
    }

    /// <summary>The book's ordinal where the address names a chapter the shared numbering has, or why not.</summary>
    private static async Task<(int Ordinal, IResult? Refusal)> Chapter(
        ICanonIndex canon,
        string book,
        int chapter,
        CancellationToken cancellationToken)
    {
        var ordinal = BookReferences.ResolveOrdinal(book);
        if (ordinal is null)
        {
            return (0, ApiResults.NotFound(BookReferences.FormatHint(book)));
        }

        var chapterCount = await canon.ChapterCount(ordinal.Value, cancellationToken);
        return chapter < 1 || chapter > chapterCount
            ? (0, ApiResults.NotFound(
                $"{BookReferences.Name(ordinal.Value)} has {chapterCount} chapters in the shared numbering, " +
                $"so there is no chapter {chapter}."))
            : (ordinal.Value, null);
    }

    /// <summary>The context with the weights counted afresh, for a caller that holds none.</summary>
    internal static async Task<ChapterContextResponse> Context(
        AppDbContext db,
        int book,
        int chapter,
        string? language,
        CancellationToken cancellationToken) =>
        await Context(db, book, chapter, language, await ContextWeights.Count(db, cancellationToken), cancellationToken);

    internal static async Task<ChapterContextResponse> Context(
        AppDbContext db,
        int book,
        int chapter,
        string? language,
        ContextWeights weights,
        CancellationToken cancellationToken,
        bool generated = true)
    {
        var (verses, how, spoken) = await NamedAndHow(db, book, chapter, cancellationToken);
        ChapterSalience.SettleWordsForGod(verses, how, spoken);
        var slugs = verses.Keys.ToList();
        var records = await db.Entities
            .Where(e => slugs.Contains(e.Slug))
            .Select(e => new
            {
                e.Id,
                e.Slug,
                e.Kind,
                e.Name,
                e.Distinguisher,
                e.PlaceKind,
                e.Subtype,
                e.Source,
                Location = e.Location == null
                    ? null
                    : new EntityLocationResponse(
                        e.Location.Longitude,
                        e.Location.Latitude,
                        e.Location.Kind,
                        e.Location.Score / EncyclopediaEndpoints.ScoreScale),
            })
            .ToListAsync(cancellationToken);

        var local = await EntityNames.Of(db, [.. records.Select(r => r.Id)], language, cancellationToken);
        var described = await Descriptors.Of(db, slugs, language, cancellationToken);
        var meanings = await Meanings(db, [.. records.Select(r => r.Id)], cancellationToken);
        var namesUsed = await ChapterSalience.NamesUsed(db, book, chapter, cancellationToken);
        // A face for the people only: a place's or a thing's picture is its page's, and a row of the
        // words for God carries the one picture God's record leads with, which is light and not a face.
        var pictured = await ImageEndpoints.Leading(
            db,
            [.. records.Where(r => r.Kind == EntityKind.Person).Select(r => r.Slug)],
            cancellationToken,
            generated);

        var entities = records
            .Select(r => new ContextEntityResponse(
                r.Slug,
                EnumSpelling.Of(r.Kind),
                r.Name,
                r.Distinguisher,
                [.. verses[r.Slug]])
            {
                LocalName = local.GetValueOrDefault(r.Id),
                Descriptor = described.GetValueOrDefault(r.Slug),
                Meaning = meanings.GetValueOrDefault(r.Id),
                PlaceKind = r.PlaceKind,
                Subtype = r.Subtype,
                Location = r.Location,
                How = [.. how.GetValueOrDefault(r.Slug) ?? []],
                SourceId = Datasets.Of(r.Source),
                Group = ChapterSalience.WordsForGod.Contains(r.Slug) ? ChapterSalience.GodGroup : null,
                ChapterName = ChapterSalience.WordsForGod.Contains(r.Slug)
                    ? null
                    : ChapterSalience.NameUsed(r.Name, namesUsed.GetValueOrDefault(r.Slug)),
                Thumbnail = pictured.GetValueOrDefault(r.Slug),
            })
            .OrderByDescending(e => ChapterSalience.Of(e.Slug, verses, how, spoken))
            .ThenBy(e => weights.EntityChapters.GetValueOrDefault(e.Slug, int.MaxValue))
            .ThenBy(e => e.LocalName ?? e.Name, StringComparer.CurrentCulture)
            .ThenBy(e => e.Slug, StringComparer.Ordinal)
            .ToList();

        var events = await db.Events
            .Where(e => e.CanonicalBook == book && e.CanonicalChapter == chapter)
            .OrderBy(e => e.CanonicalVerse)
            .ThenBy(e => e.YearFromCreation)
            .ThenBy(e => e.SequenceInYear == null)
            .ThenBy(e => e.SequenceInYear)
            .ThenBy(e => e.Slug)
            .Select(EncyclopediaEndpoints.Rows)
            .ToListAsync(cancellationToken);

        var (periods, dated) = await Periods(db, book, chapter, events, cancellationToken);
        var measures = await Measures(db, book, chapter, cancellationToken);
        var commandments = await CommandmentEndpoints.InChapter(db, book, chapter, cancellationToken);
        var topics = await ChapterTopics.InChapter(db, book, chapter, weights, entities, cancellationToken);

        return new ChapterContextResponse(
            new BookRefResponse(book, BookReferences.Name(book), BookReferences.Slug(book)),
            chapter,
            entities,
            [.. events.Select(row => EncyclopediaEndpoints.Event(row, NoPlaces))],
            periods,
            dated,
            measures,
            commandments,
            topics);
    }

    /// <summary>
    /// Every record the chapter names, with the verses naming it: the words of any text that name
    /// it, the record's own list of verses, and the passages of objects and observances running
    /// through the chapter.
    /// </summary>
    internal static async Task<Dictionary<string, SortedSet<int>>> Named(
        AppDbContext db,
        int book,
        int chapter,
        CancellationToken cancellationToken) =>
        (await NamedAndHow(db, book, chapter, cancellationToken)).Verses;

    /// <summary>
    /// The three routes a record reaches the chapter by, which are three different claims: a word
    /// of some text names it (<c>words</c>), the record's source lists a verse of the chapter
    /// (<c>listed</c>), or a passage about it runs through the chapter (<c>passage</c>). A record
    /// can arrive by more than one, and the reader is told which.
    ///
    /// <para>
    /// <c>Spoken</c> holds the verses where the words themselves name each record: the annotated
    /// words, and the verses this corpus lists for a record because of the words it numbers or
    /// annotates. The rest of a record's verses are a source's list alone.
    /// </para>
    /// </summary>
    internal static async Task<(Dictionary<string, SortedSet<int>> Verses, Dictionary<string, SortedSet<string>> How,
            Dictionary<string, SortedSet<int>> Spoken)>
        NamedAndHow(
            AppDbContext db,
            int book,
            int chapter,
            CancellationToken cancellationToken)
    {
        var verses = await Annotations.InChapter(db, book, chapter, cancellationToken);
        var how = verses.Keys.ToDictionary(slug => slug, _ => new SortedSet<string>(StringComparer.Ordinal) { "words" });
        var spoken = verses.ToDictionary(pair => pair.Key, pair => new SortedSet<int>(pair.Value), StringComparer.Ordinal);

        // Disputed rows are left out: they are the references the source itself would not assign
        // to the record, and listing them here would assign them.
        var stated = await db.EntityVerses
            .Where(v => v.CanonicalBook == book && v.CanonicalChapter == chapter && !v.Disputed)
            .Select(v => new { v.Entity!.Slug, v.CanonicalVerse, v.Source })
            .Distinct()
            .ToListAsync(cancellationToken);
        foreach (var row in stated)
        {
            if (!verses.TryGetValue(row.Slug, out var at))
            {
                verses[row.Slug] = at = [];
            }

            at.Add(row.CanonicalVerse);
            Route(how, row.Slug, "listed");
            if (ChapterSalience.FromTheWords(row.Source))
            {
                if (!spoken.TryGetValue(row.Slug, out var said))
                {
                    spoken[row.Slug] = said = [];
                }

                said.Add(row.CanonicalVerse);
            }
        }

        var before = verses.Keys.ToHashSet(StringComparer.Ordinal);
        await AddPassages(db, book, chapter, verses, cancellationToken);
        foreach (var slug in verses.Keys.Where(slug => !before.Contains(slug)))
        {
            Route(how, slug, "passage");
        }

        return (verses, how, spoken);
    }

    private static void Route(Dictionary<string, SortedSet<string>> how, string slug, string route)
    {
        if (!how.TryGetValue(slug, out var routes))
        {
            how[slug] = routes = new SortedSet<string>(StringComparer.Ordinal);
        }

        routes.Add(route);
    }

    private static readonly IReadOnlyDictionary<string, string> NoPlaces = new Dictionary<string, string>();

    /// <summary>
    /// The objects and observances whose passages run through the chapter, where no word of it names
    /// them: Exodus 25 describes the ark in verses that say <em>it</em> and <em>thereof</em>, and a
    /// panel that listed the ark only at the verses spelling its name would miss what the chapter is
    /// about. A record a word already names keeps the verses the words give it — the passage is what
    /// the chapter is about, and the words are where the text says it.
    /// </summary>
    private static async Task AddPassages(
        AppDbContext db,
        int book,
        int chapter,
        Dictionary<string, SortedSet<int>> verses,
        CancellationToken cancellationToken)
    {
        var passages = await db.EntityPassages
            .Where(p => p.CanonicalBook == book && p.CanonicalChapter <= chapter && p.EndChapter >= chapter)
            .Select(p => new { p.Entity!.Slug, p.CanonicalChapter, p.CanonicalVerse, p.EndChapter, p.EndVerse })
            .ToListAsync(cancellationToken);
        passages = [.. passages.Where(p => !verses.ContainsKey(p.Slug))];
        if (passages.Count == 0)
        {
            return;
        }

        var last = await db.VerseReferences
            .Where(r => r.IsPrimary && r.CanonicalBook == book && r.CanonicalChapter == chapter)
            .MaxAsync(r => (int?)r.CanonicalVerse, cancellationToken) ?? 0;
        foreach (var passage in passages)
        {
            var from = passage.CanonicalChapter == chapter ? passage.CanonicalVerse ?? 1 : 1;
            var to = passage.EndChapter == chapter ? passage.EndVerse ?? last : last;
            if (!verses.TryGetValue(passage.Slug, out var at))
            {
                verses[passage.Slug] = at = [];
            }

            for (var verse = from; verse <= to; verse++)
            {
                at.Add(verse);
            }
        }
    }

    /// <summary>
    /// The periods a chapter falls in, and what placed it there.
    ///
    /// <para>
    /// A chapter carries no year of its own; the events set in it do. So a period holds the chapter
    /// where it opens or closes on one of them, or where one of their years falls inside it. The
    /// test is half-open, because the event that ends an era usually begins the next: Jeroboam's
    /// reign begins in the year Solomon's ends, and 1 Kings 12 is the first chapter of the
    /// divided kingdom, not the last of the united one.
    /// </para>
    ///
    /// <para>
    /// A chapter no event is set in is placed by the dated events on either side of it in the same
    /// book, and only in a period that holds both: Exodus 25 lies between the covenant at Sinai and the
    /// building of the tabernacle, so it is in the wilderness whichever year between them it describes.
    /// Where only one side has an event the chapter is not placed at all, because one neighbour
    /// says where the book was, not where this chapter is.
    /// </para>
    /// </summary>
    private static async Task<(IList<ContextPeriodResponse> Periods, string? Dated)> Periods(
        AppDbContext db,
        int book,
        int chapter,
        IReadOnlyList<EncyclopediaEndpoints.EventRow> events,
        CancellationToken cancellationToken)
    {
        var candidates = db.Periods.Where(p =>
            p.Realm == Realms.Scripture && p.Kind != LifeKind
            && p.StartYear != null && p.EndYear != null);

        var years = events
            .Select(row => row.Event.YearFromCreation)
            .OfType<int>()
            .Distinct()
            .ToList();

        if (years.Count > 0)
        {
            var eventIds = events.Select(row => row.Event.Id).ToList();
            var holding = await candidates
                .Where(p => (p.StartEventId != null && eventIds.Contains(p.StartEventId.Value))
                            || (p.EndEventId != null && eventIds.Contains(p.EndEventId.Value))
                            || years.Any(y => p.StartYear <= y && (y < p.EndYear || y == p.StartYear)))
                .OrderBy(p => p.Level).ThenBy(p => p.StartYear).ThenBy(p => p.Slug)
                .Select(Row)
                .ToListAsync(cancellationToken);
            return (await WithYears(db, holding, cancellationToken),
                holding.Count > 0 ? ChapterPeriods.FromItsEvents : null);
        }

        var dated = db.Events.Where(e =>
            e.CanonicalBook == book && e.CanonicalChapter != null && e.YearFromCreation != null);
        var before = await dated
            .Where(e => e.CanonicalChapter < chapter)
            .OrderByDescending(e => e.CanonicalChapter).ThenByDescending(e => e.CanonicalVerse)
            .ThenByDescending(e => e.YearFromCreation)
            .Select(e => e.YearFromCreation)
            .FirstOrDefaultAsync(cancellationToken);
        var after = await dated
            .Where(e => e.CanonicalChapter > chapter)
            .OrderBy(e => e.CanonicalChapter).ThenBy(e => e.CanonicalVerse).ThenBy(e => e.YearFromCreation)
            .Select(e => e.YearFromCreation)
            .FirstOrDefaultAsync(cancellationToken);
        if (before is not { } one || after is not { } other)
        {
            return ([], null);
        }

        var (from, to) = (Math.Min(one, other), Math.Max(one, other));
        var around = await candidates
            .Where(p => p.StartYear <= from && to <= p.EndYear)
            .OrderBy(p => p.Level).ThenBy(p => p.StartYear).ThenBy(p => p.Slug)
            .Select(Row)
            .ToListAsync(cancellationToken);
        return (await WithYears(db, around, cancellationToken),
            around.Count > 0 ? ChapterPeriods.FromTheEventsAround : null);
    }

    private sealed record PeriodRow(
        string Slug, string Name, string? Kind, int? StartYear, int? EndYear, int? StartEventId, int? EndEventId);

    private static readonly Expression<Func<Period, PeriodRow>> Row =
        p => new PeriodRow(p.Slug, p.Name, p.Kind, p.StartYear, p.EndYear, p.StartEventId, p.EndEventId);

    /// <summary>
    /// Each period's span in every reckoning that dates both the events bounding it, exactly as the
    /// timeline reads it, so the panel can follow the reckoning the reader picks.
    /// </summary>
    private static async Task<IList<ContextPeriodResponse>> WithYears(
        AppDbContext db,
        IReadOnlyList<PeriodRow> periods,
        CancellationToken cancellationToken)
    {
        if (periods.Count == 0)
        {
            return [];
        }

        var anchors = periods
            .SelectMany(p => new[] { p.StartEventId, p.EndEventId })
            .OfType<int>()
            .Distinct()
            .ToList();
        var years = await EncyclopediaEndpoints.EventYears(
            db, db.EventDates.Where(d => anchors.Contains(d.EventId)), cancellationToken);

        return
        [
            .. periods.Select(p => new ContextPeriodResponse(
                p.Slug, p.Name, p.Kind, p.StartYear, p.EndYear,
                EncyclopediaEndpoints.Span(years, p.StartEventId, p.EndEventId, p.StartYear, p.EndYear))),
        ];
    }

    /// <summary>
    /// The units of length, weight, volume and money this panel speaks of, by the lexicon entry that
    /// is the unit. Only entries whose every sense is the unit: <em>kikkar</em> is a talent and also
    /// the plain of the Jordan and a loaf, <em>chomer</em> a homer and also clay, <em>omer</em> also a
    /// sheaf, <em>tephach</em> also a coping, <em>stadion</em> also a race, and counting any of those by
    /// its number would set a weight beside a verse about a plain. The cubit's entry also covers a
    /// door post once, in Isaiah 6:4, which is the one place this list says something the text does not.
    /// </summary>
    internal static readonly string[] Units =
    [
        "H520", "H2239", "H2948", "H1626", "H1235", "H8255", "H4488", "H3849", "H6894", "H1969", "H5429",
        "H374", "H1324", "H3734",
        "G4083", "G3712", "G3400", "G3046", "G5518", "G4568", "G943", "G3355", "G2884",
        "G1220", "G1406", "G1323", "G4715", "G3414", "G5007", "G787", "G2835", "G3016",
    ];

    /// <summary>The verses of the chapter where a word in any text carries each unit's number, once each.</summary>
    private static async Task<IList<ContextMeasureResponse>> Measures(
        AppDbContext db,
        int book,
        int chapter,
        CancellationToken cancellationToken)
    {
        var rows = await (
                from reference in db.VerseReferences
                where reference.IsPrimary
                      && reference.CanonicalBook == book
                      && reference.CanonicalChapter == chapter
                join word in db.Words on reference.VerseId equals word.VerseId
                where word.StrongNumber != null && Units.Contains(word.StrongNumber)
                select new { Number = word.StrongNumber!, reference.CanonicalVerse })
            .Distinct()
            .ToListAsync(cancellationToken);

        return
        [
            .. rows.GroupBy(row => row.Number)
                .Select(unit => new ContextMeasureResponse(
                    unit.Key, [.. unit.Select(row => row.CanonicalVerse).Order()]))
                .OrderBy(unit => unit.Verses[0])
                .ThenBy(unit => Array.IndexOf(Units, unit.Number)),
        ];
    }

    private const string ProperName = "proper name";

    private const string TitleName = "title";

    /// <summary>
    /// What a record's own name means, where the corpus holds a meaning for it: the entry its headword
    /// is filed under, or failing that its one proper name. A title's meaning is not a name's meaning,
    /// and a record with several proper names and none of them its headword says nothing rather than
    /// picking one.
    /// </summary>
    private static async Task<Dictionary<int, string>> Meanings(
        AppDbContext db,
        IReadOnlyCollection<int> ids,
        CancellationToken cancellationToken)
    {
        var names = await db.EntityNames
            .Where(n => ids.Contains(n.EntityId) && n.Meaning != null && n.Meaning != "")
            .Select(n => new { n.EntityId, n.Label, n.Kind, Meaning = n.Meaning!, Headword = n.Entity!.Name })
            .ToListAsync(cancellationToken);

        var meanings = new Dictionary<int, string>();
        foreach (var entity in names.GroupBy(n => n.EntityId))
        {
            var headword = entity.FirstOrDefault(n => n.Label == n.Headword && n.Kind != TitleName);
            var proper = entity.Where(n => n.Kind == ProperName).ToList();
            var meaning = headword?.Meaning ?? (proper.Count == 1 ? proper[0].Meaning : null);
            if (meaning is not null)
            {
                meanings[entity.Key] = meaning;
            }
        }

        return meanings;
    }
}

/// <summary>How a chapter was placed in its periods, as <see cref="ChapterContextResponse.PeriodsFrom"/> spells it.</summary>
internal static class ChapterPeriods
{
    public const string FromItsEvents = "events";

    public const string FromTheEventsAround = "neighbours";
}

/// <param name="Entities">
/// Every record the chapter names, the most present first: by the verses whose words name it, a
/// verse only a source's list gives counting half; then the record found in fewer chapters of the
/// Bible first, then by the name shown.
/// </param>
/// <param name="Events">The events the sources set in this chapter, in the order of its verses.</param>
/// <param name="Periods">The periods the chapter falls in, the widest first.</param>
/// <param name="Measures">
/// The units of measure and money the chapter's words carry, by lexicon entry, in the order the
/// chapter first uses them.
/// </param>
/// <param name="Commandments">
/// The commandments of Maimonides' count that rest on a verse of this chapter, in the order the
/// chapter gives them. Empty outside the Torah.
/// </param>
/// <param name="Topics">
/// The subjects of Nave's Topical Bible filing verses of this chapter: its themes, the most particular
/// to it first, then the entries of the people it names, then the index's readings of it.
/// </param>
/// <param name="PeriodsFrom">
/// <c>events</c> where the chapter's own events placed it, <c>neighbours</c> where it has none and
/// the dated events on either side of it in the book did, and null where nothing placed it.
/// </param>
internal record ChapterContextResponse(
    BookRefResponse Book,
    int Chapter,
    IList<ContextEntityResponse> Entities,
    IList<EventResponse> Events,
    IList<ContextPeriodResponse> Periods,
    string? PeriodsFrom,
    IList<ContextMeasureResponse> Measures,
    IList<ContextCommandmentResponse> Commandments,
    IList<ContextTopicResponse> Topics);

/// <param name="StartYear">Its first year from creation in the default reckoning, which placed the chapter in it.</param>
/// <param name="Years">
/// Its span in each reckoning that dates both its bounding events, keyed by the reckoning's slug, as
/// the timeline carries it; under the empty key for a period with no anchors, whose years belong to
/// no reckoning.
/// </param>
internal record ContextPeriodResponse(
    string Slug,
    string Name,
    string? Kind,
    int? StartYear,
    int? EndYear,
    Dictionary<string, int[]> Years);

/// <param name="Number">The lexicon entry that is the unit: <c>H520</c> for the cubit.</param>
/// <param name="Verses">The verses of the chapter where a word carries it.</param>
internal record ContextMeasureResponse(string Number, IList<int> Verses);

/// <param name="Kind">person, place, people, title or term.</param>
/// <param name="Verses">The verses of this chapter that name it, in the shared numbering.</param>
internal record ContextEntityResponse(
    string Slug,
    string Kind,
    string Name,
    string? Distinguisher,
    IList<int> Verses)
{
    /// <summary>The name in the language asked for, where the corpus has one.</summary>
    public string? LocalName { get; init; }

    public EntityDescriptorResponse? Descriptor { get; init; }

    /// <summary>What its name means, where the corpus holds a meaning for the name; in English, as the sources give it.</summary>
    public string? Meaning { get; init; }

    /// <summary>A place's kind as its source classifies it; null for everything else.</summary>
    public string? PlaceKind { get; init; }

    /// <summary>What sort of object or observance it is; null for everything else.</summary>
    public string? Subtype { get; init; }

    /// <summary>Where a place is, where the gazetteer identifies it; null for a place it cannot.</summary>
    public EntityLocationResponse? Location { get; init; }

    /// <summary>
    /// How it reaches the chapter: <c>words</c> where a word of some text names it, <c>listed</c>
    /// where its source lists a verse here, <c>passage</c> where a passage about it runs through.
    /// </summary>
    public IList<string> How { get; init; } = [];

    /// <summary>The declared dataset its record comes from, where one claims it.</summary>
    public string? SourceId { get; init; }

    /// <summary>
    /// <c>god</c> for the records of the words for God, which a reader is shown as one row saying
    /// which of them the chapter uses; null for everything else.
    /// </summary>
    public string? Group { get; init; }

    /// <summary>
    /// The name the chapter calls it by where that is not its headword: John 1 calls the man his
    /// record heads Bartholomew <em>Nathanael</em>. English, as the source labels the verse; null
    /// where the chapter uses the headword or labels nothing.
    /// </summary>
    public string? ChapterName { get; init; }

    /// <summary>The picture a person's page leads with, to show small beside the name; null for everything else.</summary>
    public EntityThumbnailResponse? Thumbnail { get; init; }
}
