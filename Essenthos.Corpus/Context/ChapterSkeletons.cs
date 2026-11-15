using System.IO.Compression;
using System.Linq.Expressions;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Descriptions;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Context;

/// <summary>
/// What a chapter's context is made of whatever languages the reader reads in (TSK-1669): every record the chapter names
/// with the verses and the routes that bring it, the chapter's events, periods, measures, commandments and topics, and the
/// families among its people. The serving build stores one per canonical chapter (<c>context_chapter</c>); a request puts
/// it together with the records' lines in its languages (<see cref="ContextLine"/>), the texts the reader has open and the
/// canon mode. Nothing here depends on the reader.
/// </summary>
/// <param name="Verses">Each record the chapter names, with the verses naming it, before any open text adds its own.</param>
/// <param name="How">The routes each record reaches the chapter by: <c>words</c>, <c>listed</c>, <c>passage</c>.</param>
/// <param name="Spoken">The verses where the words themselves name each record.</param>
/// <param name="Records">Every record of <paramref name="Verses"/>, with what its row says of it whatever the languages.</param>
/// <param name="Topics">Nave's topics filing verses of the chapter, before they are weighed against the records shown.</param>
/// <param name="Said">The topics whose heading the chapter's own English says.</param>
/// <param name="Length">The chapter's last verse in the frame, which a topic's verses are a share of.</param>
/// <param name="Trees">The families among the chapter's people, as the panel's trees draw them.</param>
/// <param name="TreeMembers">Everyone in any of the trees, in the order the family card lists them.</param>
internal sealed record ChapterSkeleton(
    int Book,
    int Chapter,
    Dictionary<string, int[]> Verses,
    Dictionary<string, string[]> How,
    Dictionary<string, int[]> Spoken,
    List<SkeletonRecord> Records,
    List<SkeletonEvent> Events,
    List<ContextPeriodResponse> Periods,
    string? PeriodsFrom,
    List<ContextMeasureResponse> Measures,
    List<ContextCommandmentResponse> Commandments,
    List<SkeletonTopic> Topics,
    string[] Said,
    int Length,
    List<SkeletonTree> Trees,
    string[] TreeMembers);

/// <param name="Kind">The record's kind as the answer spells it.</param>
/// <param name="Canons">The canons that attest the record (<c>entity_canon</c>), which a canon mode filters by.</param>
/// <param name="ChapterName">The name the chapter calls it by where that is not its headword (<see cref="ChapterSalience.NameUsed"/>).</param>
internal sealed record SkeletonRecord(
    string Slug,
    string Kind,
    string Name,
    string? Distinguisher,
    string? PlaceKind,
    SkeletonLocation? Location,
    string[] Canons,
    string? Meaning,
    string? ChapterName);

internal sealed record SkeletonLocation(double Lon, double Lat, string Kind, double? Confidence);

/// <param name="Name">The title without the source's asterisk.</param>
/// <param name="LocalNames">The title in each language a machine translation of it is held in, by the translation's language.</param>
internal sealed record SkeletonEvent(
    string Slug,
    string Name,
    VerseRefResponse? Reference,
    List<ContextEventDateResponse> Dates,
    Dictionary<string, string>? LocalNames);

/// <param name="Name">The heading as Nave wrote it, which the records' names are compared with.</param>
/// <param name="Title">The heading as the panel shows it.</param>
/// <param name="Named">How many verses of the chapter the topic files by number.</param>
/// <param name="Whole">Whether the topic cites the whole chapter, or a run to its end.</param>
internal sealed record SkeletonTopic(
    string Slug,
    string Name,
    string Title,
    List<int> Verses,
    List<ContextTopicEntryResponse> Entries,
    int Named,
    bool Whole);

internal sealed record SkeletonTree(string[] Named, string[] Between);

/// <summary>A name an open text carries against its base text in the chapter (<see cref="ChapterSkeletons.Exceptions"/>).</summary>
internal sealed record ContextException(string Slug, string Text, string Origin, SortedSet<int> Verses);

/// <summary>The stored documents read and written as the build and the API both see them.</summary>
[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ChapterSkeleton))]
[JsonSerializable(typeof(ContextLineBody))]
internal partial class ContextDocumentJson : JsonSerializerContext;

internal static class ChapterSkeletons
{
    /// <summary>Raise with any change to what a skeleton holds or how it is read: a stored one is made again only when this moves.</summary>
    public const int Version = 1;

    /// <summary>
    /// The one kind of period a chapter is never placed in. Eras, reigns, judgeships and the spans
    /// below them say where a chapter stands; a lifetime does not, because every chapter falls inside
    /// the lives of dozens of people it never mentions.
    /// </summary>
    private const string LifeKind = "life";

    /// <summary>The route of a record a text the reader has open names in the chapter against its base.</summary>
    public const string Excepted = "exception";

    public static string Write(ChapterSkeleton skeleton) => JsonSerializer.Serialize(skeleton, ContextDocumentJson.Default.ChapterSkeleton);

    public static ChapterSkeleton Read(string body) =>
        JsonSerializer.Deserialize(body, ContextDocumentJson.Default.ChapterSkeleton)
        ?? throw new InvalidDataException("A stored chapter context is empty.");

    /// <summary>The first 16 hex of the SHA-256 of a stored body, which a table is matched to its product by.</summary>
    public static string Hash(string body) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(body)))[..16];

    /// <summary>The size of a body gzipped, which the build reports.</summary>
    public static long Gzipped(string body)
    {
        using var buffer = new MemoryStream();
        using (var gzip = new GZipStream(buffer, CompressionLevel.Optimal, leaveOpen: true))
        {
            gzip.Write(Encoding.UTF8.GetBytes(body));
        }

        return buffer.Length;
    }

    /// <summary>
    /// The chapter's skeleton read from the served tables. <paramref name="extra"/> names records an open text brings that
    /// the chapter's own routes do not, so a request composing them gets their rows too.
    /// </summary>
    public static async Task<ChapterSkeleton> Gather(
        AppDbContext db,
        int book,
        int chapter,
        CancellationToken cancellationToken,
        IReadOnlyCollection<string>? extra = null)
    {
        var (verses, how, spoken) = await NamedAndHow(db, book, chapter, cancellationToken);
        var slugs = verses.Keys.Concat(extra ?? []).Distinct(StringComparer.Ordinal).ToList();
        var rows = await db.Entities
            .Where(e => slugs.Contains(e.Slug))
            .Select(e => new
            {
                e.Id,
                e.Slug,
                e.Kind,
                e.Name,
                e.Distinguisher,
                e.PlaceKind,
                Location = e.Location == null
                    ? null
                    : new SkeletonLocation(e.Location.Longitude, e.Location.Latitude, e.Location.Kind, e.Location.Score / ScoreScale),
                Canons = e.Canons.Select(c => c.Canon).ToList(),
            })
            .ToListAsync(cancellationToken);
        var meanings = await Meanings(db, [.. rows.Select(r => r.Id)], cancellationToken);
        var namesUsed = await ChapterSalience.NamesUsed(db, book, chapter, cancellationToken);
        var records = rows
            .OrderBy(r => r.Id)
            .Select(r => new SkeletonRecord(
                r.Slug,
                EnumSpelling.Of(r.Kind),
                r.Name,
                r.Distinguisher,
                r.PlaceKind,
                r.Location,
                [.. r.Canons.Order(StringComparer.Ordinal)],
                meanings.GetValueOrDefault(r.Id),
                ChapterSalience.WordsForGod.Contains(r.Slug) ? null : ChapterSalience.NameUsed(r.Name, namesUsed.GetValueOrDefault(r.Slug))))
            .ToList();

        var events = await db.Events
            .Where(e => e.CanonicalBook == book && e.CanonicalChapter == chapter)
            .OrderBy(e => e.CanonicalVerse)
            .ThenBy(e => e.YearFromCreation)
            .ThenBy(e => e.SequenceInYear == null)
            .ThenBy(e => e.SequenceInYear)
            .ThenBy(e => e.Slug)
            .Select(EventRows.Rows)
            .ToListAsync(cancellationToken);
        var (periods, dated) = await Periods(db, book, chapter, events, cancellationToken);
        var (topics, said, length) = await Topics(db, book, chapter, cancellationToken);
        var (trees, members) = await Trees(db, verses.Keys, cancellationToken);

        return new ChapterSkeleton(
            book,
            chapter,
            verses.ToDictionary(v => v.Key, v => v.Value.ToArray(), StringComparer.Ordinal),
            how.ToDictionary(h => h.Key, h => h.Value.ToArray(), StringComparer.Ordinal),
            spoken.ToDictionary(s => s.Key, s => s.Value.ToArray(), StringComparer.Ordinal),
            records,
            await Events(db, events, cancellationToken),
            [.. periods],
            dated,
            [.. await Measures(db, book, chapter, cancellationToken)],
            [.. await Commandments(db, book, chapter, cancellationToken)],
            topics,
            said,
            length,
            trees,
            members);
    }

    /// <summary>Where a record names a place, its gazetteer score over a thousand, as the record page divides it.</summary>
    public const double ScoreScale = 1000.0;

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
        // Disputed rows are left out: they are the references the source itself would not assign
        // to the record, and listing them here would assign them.
        var stated = await db.EntityVerses.Shown()
            .Where(v => v.CanonicalBook == book && v.CanonicalChapter == chapter && !v.Disputed)
            .Select(v => new { v.Entity!.Slug, v.CanonicalVerse, v.Source, v.Names, v.Withheld })
            .Distinct()
            .ToListAsync(cancellationToken);

        var verses = new Dictionary<string, SortedSet<int>>(StringComparer.Ordinal);
        var withheld = new HashSet<(string Slug, int Verse)>();
        foreach (var row in stated.Where(row => ShownVerses.ReadOffTheWords(row.Source, row.Names)))
        {
            if (row.Withheld)
            {
                withheld.Add((row.Slug, row.CanonicalVerse));
                continue;
            }

            if (!verses.TryGetValue(row.Slug, out var named))
            {
                verses[row.Slug] = named = [];
            }

            named.Add(row.CanonicalVerse);
        }

        // The settled names of the reference texts are the passage's own: what the base texts name, whatever a
        // translation adds or names otherwise (those are exceptions, flagged with the text that carries them).
        foreach (var settled in await Settled(db, book, chapter, cancellationToken))
        {
            if (withheld.Contains((settled.Slug, settled.CanonicalVerse)))
            {
                continue;
            }

            if (!verses.TryGetValue(settled.Slug, out var named))
            {
                verses[settled.Slug] = named = [];
            }

            named.Add(settled.CanonicalVerse);
        }

        var how = verses.Keys.ToDictionary(slug => slug, _ => new SortedSet<string>(StringComparer.Ordinal) { "words" });
        var spoken = verses.ToDictionary(pair => pair.Key, pair => new SortedSet<int>(pair.Value), StringComparer.Ordinal);

        foreach (var row in stated)
        {
            // The verse the ranking leaves to the people, on this record's row and on any other list
            // read off the same words.
            if (ChapterSalience.FromTheWords(row.Source) && withheld.Contains((row.Slug, row.CanonicalVerse)))
            {
                continue;
            }

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

    public static void Route(Dictionary<string, SortedSet<string>> how, string slug, string route)
    {
        if (!how.TryGetValue(slug, out var routes))
        {
            how[slug] = routes = new SortedSet<string>(StringComparer.Ordinal);
        }

        routes.Add(route);
    }

    private sealed record NamedAt(string Slug, int CanonicalVerse);

    /// <summary>
    /// Where a reference text's settled names fall in the chapter, by the verse of the shared numbering. Only the reference
    /// texts' words are read: a chapter holds the words of some forty texts, and only these seven can carry a settled name.
    /// </summary>
    private static async Task<List<NamedAt>> Settled(AppDbContext db, int book, int chapter, CancellationToken cancellationToken) =>
        await (
                from reference in db.VerseReferences
                where reference.IsPrimary && reference.CanonicalBook == book && reference.CanonicalChapter == chapter
                      && WordEntityOrigins.SettledTexts.Contains(reference.Verse!.Text!.Slug)
                join word in db.Words on reference.VerseId equals word.VerseId
                join named in db.WordEntities on word.Id equals named.WordId
                where named.Origin == WordEntityOrigins.Settled
                select new NamedAt(named.Entity!.Slug, reference.CanonicalVerse))
            .Distinct()
            .ToListAsync(cancellationToken);

    /// <summary>
    /// The names the open texts carry against their base in the chapter: a record a text adds (<c>added</c>) or names in
    /// place of its base's (<c>differs</c>), each with the text that carries it. A suppression takes nothing away: the
    /// context is about the passage, and the base still names the record.
    /// </summary>
    public static async Task<List<ContextException>> Exceptions(
        AppDbContext db,
        int book,
        int chapter,
        IReadOnlyList<TextEntry> open,
        CancellationToken cancellationToken)
    {
        if (open.Count == 0)
        {
            return [];
        }

        // The exceptions are a few hundred words in the whole corpus, held apart by a partial index on word_entity; only the
        // open texts' verses of the chapter are joined to them.
        var ids = open.Select(t => t.Id).ToList();
        var rows = await (
                from reference in db.VerseReferences
                where reference.IsPrimary && reference.CanonicalBook == book && reference.CanonicalChapter == chapter
                      && ids.Contains(reference.Verse!.TextId)
                join word in db.Words on reference.VerseId equals word.VerseId
                join named in db.WordEntities on word.Id equals named.WordId
                where named.Origin == WordEntityOrigins.Added || named.Origin == WordEntityOrigins.Differs
                select new { named.Entity!.Slug, word.TextId, Origin = named.Origin!, reference.CanonicalVerse })
            .Distinct()
            .ToListAsync(cancellationToken);
        var order = open.Select((text, at) => (text.Id, at)).ToDictionary(pair => pair.Id, pair => pair.at);
        var slugs = open.ToDictionary(t => t.Id, t => t.Slug);
        return
        [
            .. rows.GroupBy(row => (row.Slug, row.TextId, row.Origin))
                .OrderBy(group => group.Key.Slug, StringComparer.Ordinal)
                .ThenBy(group => order[group.Key.TextId])
                .ThenBy(group => group.Key.Origin, StringComparer.Ordinal)
                .Select(group => new ContextException(
                    group.Key.Slug, slugs[group.Key.TextId], group.Key.Origin, [.. group.Select(row => row.CanonicalVerse)])),
        ];
    }

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
    /// The events as the panel lists them, each with its title in every language a translation of it is held in. The
    /// title's asterisk comes off once the translation is found by the starred English, as the event pages do it.
    /// </summary>
    private static async Task<List<SkeletonEvent>> Events(AppDbContext db, IReadOnlyList<EventRow> rows, CancellationToken cancellationToken)
    {
        var digests = rows.Select(row => row.Event.Name).Where(name => !string.IsNullOrWhiteSpace(name)).Distinct(StringComparer.Ordinal)
            .ToDictionary(EnglishNotes.Digest, name => name, StringComparer.Ordinal);
        var shas = digests.Keys.ToList();
        var translations = shas.Count == 0
            ? []
            : await db.TextTranslations
                .Where(t => shas.Contains(t.EnglishSha256))
                .Select(t => new { t.EnglishSha256, t.Language, t.Text })
                .ToListAsync(cancellationToken);
        var local = translations
            .GroupBy(t => digests[t.EnglishSha256], StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => g.GroupBy(t => t.Language, StringComparer.Ordinal).ToDictionary(l => l.Key, l => EventMarks.Unstarred(l.First().Text), StringComparer.Ordinal),
                StringComparer.Ordinal);

        return
        [
            .. rows.Select(row => new SkeletonEvent(
                row.Event.Slug,
                EventMarks.Unstarred(row.Event.Name),
                BookReferences.At(row.Event.CanonicalBook, row.Event.CanonicalChapter, row.Event.CanonicalVerse),
                [.. row.Dates.Select(d => new ContextEventDateResponse(d.Chronology, d.Year, d.BceYear, d.Era))],
                local.GetValueOrDefault(row.Event.Name))),
        ];
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
        IReadOnlyList<EventRow> events,
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
        var years = await ChronologySpans.EventYears(
            db, db.EventDates.Where(d => anchors.Contains(d.EventId)), cancellationToken);

        return
        [
            .. periods.Select(p => new ContextPeriodResponse(
                p.Slug, p.Name, p.Kind, p.StartYear, p.EndYear,
                ChronologySpans.Span(years, p.StartEventId, p.EndEventId, p.StartYear, p.EndYear))),
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

    /// <summary>
    /// The commandments resting on a verse of this chapter, in the order the chapter gives them:
    /// by the first of its verses they rest on, then positive before negative, then by number.
    /// </summary>
    private static async Task<IList<ContextCommandmentResponse>> Commandments(
        AppDbContext db,
        int book,
        int chapter,
        CancellationToken cancellationToken)
    {
        var rows = await db.CommandmentReferences
            .Where(r => r.CanonicalBook == book && r.CanonicalChapter == chapter)
            .Select(r => new { r.Commandment!.Kind, r.Commandment.Number, r.Commandment.Title, r.FirstVerse, r.LastVerse })
            .ToListAsync(cancellationToken);

        return
        [
            .. rows.GroupBy(r => (r.Kind, r.Number, r.Title))
                .Select(commandment => new ContextCommandmentResponse(
                    commandment.Key.Kind,
                    commandment.Key.Number,
                    commandment.Key.Title,
                    [
                        .. commandment
                            .SelectMany(r => Enumerable.Range(r.FirstVerse, r.LastVerse - r.FirstVerse + 1))
                            .Distinct()
                            .Order(),
                    ]))
                .OrderBy(c => c.Verses[0])
                .ThenBy(c => c.Kind == CommandmentKinds.Negative)
                .ThenBy(c => c.Number),
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

    /// <summary>
    /// The topics of Nave's filing verses of the chapter, each with the verses it files by number and whether it cites the
    /// whole chapter, and which of them the chapter's own English says (<see cref="ChapterTopicRows"/>).
    /// </summary>
    private static async Task<(List<SkeletonTopic> Topics, string[] Said, int Length)> Topics(
        AppDbContext db,
        int book,
        int chapter,
        CancellationToken cancellationToken)
    {
        var rows = await db.TopicReferences
            .Where(r => r.CanonicalBook == book && r.CanonicalChapter == chapter)
            .Select(r => new { r.Topic!.Slug, r.Topic.Name, r.FirstVerse, r.LastVerse, r.Heading })
            .ToListAsync(cancellationToken);
        rows = [.. rows.Where(r => !ChapterTopicRows.IndexLists.Contains(r.Slug))];
        if (rows.Count == 0)
        {
            return ([], [], 0);
        }

        // A whole chapter, or a run to its end, is closed by the chapter's own length in the frame,
        // which is also what a topic's verses are a share of.
        var length = await db.VerseReferences
            .Where(r => r.CanonicalBook == book && r.CanonicalChapter == chapter)
            .MaxAsync(r => (int?)r.CanonicalVerse, cancellationToken) ?? 0;

        var topics = rows.GroupBy(r => (r.Slug, r.Name))
            .Select(topic =>
            {
                var runs = topic
                    .Select(r =>
                    {
                        var first = r.FirstVerse ?? 1;
                        return (Heading: ChapterTopicRows.Heading(r.Heading), First: first,
                            Last: r.LastVerse ?? Math.Max(first, length), Whole: r.FirstVerse is null);
                    })
                    .ToList();
                var entries = runs
                    .GroupBy(run => run.Heading)
                    .Select(line => new ContextTopicEntryResponse(
                        line.Key,
                        [.. line.SelectMany(run => Enumerable.Range(run.First, run.Last - run.First + 1)).Distinct().Order()],
                        line.Any(run => run.Whole)))
                    .OrderBy(e => e.Verses.Count == 0 ? 0 : e.Verses[0])
                    .ThenBy(e => e.Heading, StringComparer.Ordinal)
                    .ToList();
                var verses = entries.SelectMany(e => e.Verses).Distinct().Order().ToList();
                var named = runs.Where(run => !run.Whole)
                    .SelectMany(run => Enumerable.Range(run.First, run.Last - run.First + 1))
                    .Distinct()
                    .Count();
                return new SkeletonTopic(topic.Key.Slug, topic.Key.Name, ChapterTopicRows.Title(topic.Key.Name), verses, entries, named,
                    runs.Any(run => run.Whole));
            })
            .ToList();

        var said = await ChapterTopicRows.Said(db, book, chapter, [.. topics.Select(t => (t.Slug, t.Name, (IList<int>)t.Verses))], cancellationToken);
        return (topics, [.. said.Order(StringComparer.Ordinal)], length);
    }

    /// <summary>
    /// The families among the chapter's people, as the panel's trees draw them (<see cref="ChapterFamily.Trees"/>): who of
    /// them is in each tree, and whom the tree needs to join them, by slug.
    /// </summary>
    internal static async Task<(List<SkeletonTree> Trees, string[] Members)> Trees(
        AppDbContext db,
        IEnumerable<string> named,
        CancellationToken cancellationToken)
    {
        var slugs = named.ToList();
        var people = await db.Entities
            .Where(e => slugs.Contains(e.Slug) && e.Kind == EntityKind.Person)
            .Select(e => new { e.Id, e.Slug })
            .ToListAsync(cancellationToken);
        var chapterIds = people.Select(p => p.Id).ToHashSet();

        var ties = new List<ChapterFamily.Tie>();
        var reached = new HashSet<int>(chapterIds);
        var ring = chapterIds.ToList();
        for (var step = 0; step < 2 && ring.Count > 0; step++)
        {
            var rows = await db.EntityRelationships
                .Where(r => ChapterFamily.Types.Contains(r.Type)
                            && (ring.Contains(r.FromEntityId) || ring.Contains(r.ToEntityId))
                            && r.FromEntityId != r.ToEntityId
                            && !ChapterFamily.Deities.Contains(r.From!.Slug)
                            && !ChapterFamily.Deities.Contains(r.To!.Slug))
                .Select(r => new { r.FromEntityId, r.ToEntityId, r.Type })
                .ToListAsync(cancellationToken);
            var next = new List<int>();
            foreach (var row in rows)
            {
                ties.Add(new ChapterFamily.Tie(row.FromEntityId, row.ToEntityId, ChapterFamily.Descent(row.Type)));
                foreach (var id in (int[])[row.FromEntityId, row.ToEntityId])
                {
                    if (reached.Add(id))
                    {
                        next.Add(id);
                    }
                }
            }

            ring = next;
        }

        var trees = ChapterFamily.Trees(chapterIds, ties);
        if (trees.Count == 0)
        {
            return ([], []);
        }

        var members = trees.SelectMany(t => t.Named.Concat(t.Between)).Distinct().ToList();
        var slugOf = await db.Entities
            .Where(e => members.Contains(e.Id))
            .Select(e => new { e.Id, e.Slug })
            .ToDictionaryAsync(e => e.Id, e => e.Slug, cancellationToken);
        return (
        [
            .. trees.Select(t => new SkeletonTree(
                [.. t.Named.Select(id => slugOf[id]).Order(StringComparer.Ordinal)],
                [.. t.Between.Select(id => slugOf[id]).Order(StringComparer.Ordinal)])),
        ], [.. slugOf.Values.Distinct(StringComparer.Ordinal)]);
    }
}
