using System.Diagnostics;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Essenthos.Core.Loading.Encyclopedia;

/// <param name="Read">The dataset's verse rows on a man a people is named after, read this run.</param>
/// <param name="ToThePeople">Of those, the rows moved to the people, by the rule or by the owner's answer.</param>
/// <param name="Decided">Rows the owner answered on the review list, applied as answered.</param>
/// <param name="Unsettled">Rows the rule could not settle, left where they are and listed for the owner.</param>
/// <param name="Misfiled">Rows of <see cref="MisfiledVerseLoader.Misfiled"/> moved or withdrawn this run.</param>
internal sealed record MisfiledVerseOutcome(
    int Read,
    int ToThePeople,
    int Decided,
    int Unsettled,
    int Misfiled,
    TimeSpan Elapsed)
{
    public override string ToString() =>
        $"{Read} verse rows the dataset files under a people's ancestor read, {ToThePeople} moved to the " +
        $"people the verse names ({Decided} of the rows read answered by the owner), {Unsettled} left for " +
        $"the owner to answer, and {Misfiled} rows filed under the wrong record put right, in {Elapsed}";
}

/// <summary>
/// The verses a dataset files under a record they are not about.
///
/// <para>
/// **Most of them are one mistake: an ancestor holding his people's verses.** BibleData files
/// <em>the children of Israel</em> under Jacob, <em>the children of Ammon</em> under Lot's son
/// Ben-ammi and <em>the tribe of Judah</em> under Jacob's fourth son — 782 of Jacob's rows are
/// labelled Israel. The phrase does name the man, but the verse is about the nation, and a reader
/// shown Jacob in Exodus 25 has been shown somebody four hundred years dead. Where a verse names the
/// people, the row moves to the people's record. It keeps the dataset's source and label, because it
/// is still the dataset's testimony that the verse names them; what changes is whose list counts it.
/// </para>
///
/// <para>
/// **What says a verse names the people is read off the Hebrew and the Greek, one occurrence of the
/// name at a time.** A word this corpus already annotates to the people says so, and so does the
/// people's own word, the gentilic, where the verse does not print the name. So does the name standing
/// as the second noun of a phrase that makes a collective of it — <em>sons of</em>, <em>house of</em>,
/// <em>tribe of</em>, <em>elders of</em>, <em>king of</em>, <em>God of</em> — once the man is dead,
/// because while he lives <em>the sons of Israel</em> are his sons. Against that, a phrase of kinship
/// or of one man's body — <em>son of</em>, <em>firstborn of</em>, <em>loins of</em> — names the man,
/// and so does a verse naming his parents, children or brothers as persons, the way a genealogy does:
/// <em>these are the sons of Israel; Reuben, Simeon, Levi, and Judah</em>.
/// </para>
///
/// <para>
/// **Where none of that decides, the row stays and the owner is asked**: the name bare after the
/// man's death, a collective phrase within his lifetime, a verse that does not print the name at all.
/// They are listed in <see cref="ReviewFile"/> beside the other review lists, and his answers there are
/// applied on the next load.
/// </para>
///
/// <para>
/// **The rest are single rows, listed by hand with the reason** (<see cref="Misfiled"/>): a label
/// filed one verse early, a comparison read as a presence.
/// </para>
///
/// <para>
/// Idempotent: a row once moved is no longer the ancestor's to read, a row put right is not found
/// where it was, and a row left for the owner is listed again with his answer kept.
/// </para>
/// </summary>
internal sealed class MisfiledVerseLoader(AppDbContext db, ILogger<MisfiledVerseLoader> logger)
{
    /// <summary>The review list this pass keeps, beside the others the owner's console reads.</summary>
    public const string ReviewFile = "eponym-verses.json";

    /// <summary>The answer on the review list that keeps an entry's verses with the man.</summary>
    public const string LeaveIt = "leave as it is";

    /// <summary>Why a row is unsettled: the man is dead and the name stands in no phrase that decides.</summary>
    internal const string AfterHisLife = "after-his-life";

    /// <summary>Why: a collective phrase while the man lived, when <em>the sons of Israel</em> may be his sons.</summary>
    internal const string InHisLife = "in-his-life";

    /// <summary>Why: the verse does not print the name the row is labelled with, in the Hebrew or the Greek.</summary>
    internal const string NotPrinted = "not-printed";

    private const string ReviewAbout =
        "Verses BibleData files under a man a people is named after — Jacob for Israel, Ben-ammi for the " +
        "Ammonites, Judah, Levi, Ephraim — that the rule reading the Hebrew and the Greek could not settle. " +
        "A verse whose words name the people is moved to the people's record on every load; these are the " +
        "ones the words do not decide: the name in a collective phrase while the man was still alive, the " +
        "name standing on its own after his death, and verses that do not print the name at all. Each entry " +
        "is one man, one name the dataset uses for him and one of those three. Answering with the people's " +
        "record moves every verse of the entry to the people on the next load of Essenthos.Forge; '" +
        LeaveIt + "' keeps them with the man. The load rewrites the open entries and keeps the answered ones; " +
        "an answer taken back after a load has applied it does not move the verses back.";

    /// <summary>What a reading of one row concluded.</summary>
    internal enum Reading
    {
        ThePeople,
        TheMan,
        Unsettled,
    }

    /// <summary>One occurrence of a name in the verse, as the Hebrew or the Greek writes it.</summary>
    /// <param name="Position">Where it stands among the verse's words.</param>
    /// <param name="Head">The Strong number of the noun the name is the second of, where it is one.</param>
    /// <param name="HeadPlural">Whether that noun is plural: <em>sons of</em> against <em>son of</em>.</param>
    /// <param name="NamesThePeople">Whether this corpus already annotates the word to the people.</param>
    /// <param name="NamesTheMan">Whether it annotates the word to the man.</param>
    internal sealed record Occurrence(
        int Position, string? Head, bool HeadPlural, bool NamesThePeople = false, bool NamesTheMan = false);

    /// <summary>
    /// The nouns that make a collective of a name they govern: the name as the people, or as the land
    /// and the state that are theirs, which is still not the man.
    /// </summary>
    internal static readonly IReadOnlySet<string> Collective = new HashSet<string>(StringComparer.Ordinal)
    {
        "H1004", // house of
        "H7626", "H4294", // tribe of
        "H4940", // families of
        "H5712", "H6951", "H6952", // congregation, assembly of
        "H5971", // people of
        "H2205", // elders of
        "H505", // thousands of
        "H6635", // hosts of
        "H4264", // camp of
        "H376", "H582", // man, men of: אִישׁ יִשְׂרָאֵל is the army
        "H8269", "H5387", // princes, leaders of
        "H4428", "H4467", // king, kingdom of
        "H1366", "H776", "H127", "H5892", "H2022", // border, land, ground, cities, mountains of
        "H3605", // all
        "H7611", // remnant of
        "H1330", // virgin of
        "H2233", // seed of
        "H4735", // cattle of
        "H1347", // pride of
        "H430", "H6918", "H46", "H6697", "H3444", "H8438", // God, Holy One, Mighty One, Rock, salvation, worm of
        "G3624", "G5443", "G2992", "G935", "G2316", "G1093", "G4172", "G3956", "G4690", // the same in Greek
    };

    /// <summary>
    /// Kinship nouns: in the plural they are a people — <em>the sons of Ammon</em> — and in the singular
    /// one man's own — <em>the son of Israel</em>, <em>the daughter of Jacob</em>.
    /// </summary>
    internal static readonly IReadOnlySet<string> Kindred = new HashSet<string>(StringComparer.Ordinal)
    {
        "H1121", "H1323", "G5207", "G2364",
    };

    /// <summary>
    /// Nouns of one man's family and life, so the name they govern is the man: <em>firstborn of</em>,
    /// <em>loins of</em>, <em>the years of the life of Levi</em>. Not <em>hand</em>, <em>eyes</em> or
    /// <em>face</em>, which the text says of a nation as readily as of a man.
    /// </summary>
    internal static readonly IReadOnlySet<string> Personal = new HashSet<string>(StringComparer.Ordinal)
    {
        "H1060", "H1", "H251", "H269", "H802", "H517", "H3409", "H2416", "H8141", "H3117",
        "G4416", "G3962", "G80", "G1135", "G3384",
    };

    /// <summary>
    /// The nouns of God that a name stands second to — <em>the God of Isaac</em> — which make the man's
    /// God the nation's where they govern his own name, and leave a kinsman's name his own.
    /// </summary>
    internal static readonly IReadOnlySet<string> Divine = new HashSet<string>(StringComparer.Ordinal)
    {
        "H430", "H6918", "H46", "H6697", "H3444", "G2316",
    };

    /// <summary>
    /// How many of the man's kin who are themselves ancestors of peoples a verse has to name before it
    /// is a genealogy rather than the tribes: <em>Judah and Israel</em> are two kingdoms, and
    /// <em>Gershon, Kohath and Merari</em> are the sons of Levi.
    /// </summary>
    internal const int Genealogy = 2;

    /// <summary>What one row of the dataset is, from the occurrences of the man's name in its verse.</summary>
    /// <param name="kinNamed">
    /// Whether the verse names the man's kin as persons the way a genealogy does: somebody of his
    /// family no people is named after, <see cref="Genealogy"/> of them who are, or one of his
    /// children after <em>the sons of</em> him — <em>the sons of Dan; Hushim</em>.
    /// </param>
    /// <param name="inHisLife">Whether the verse stands in the stretch of the text where the man lives.</param>
    /// <param name="peoplesWord">
    /// Whether the verse carries the people's own word, the gentilic. Where the name is missing that
    /// word is the people; where the name stands bare beside it, the verse has told the two apart —
    /// <em>of Hamul, the family of the Hamulites</em>.
    /// </param>
    internal static (Reading Reading, string? Why) Read(
        IReadOnlyList<Occurrence> occurrences, bool kinNamed, bool inHisLife, bool peoplesWord)
    {
        if (occurrences.Any(o => o.NamesThePeople && !o.NamesTheMan))
        {
            return (Reading.ThePeople, null);
        }

        if (occurrences.Count == 0)
        {
            return peoplesWord ? (Reading.ThePeople, null) : (Reading.Unsettled, NotPrinted);
        }

        if (kinNamed || occurrences.Any(IsPersonal))
        {
            return (Reading.TheMan, null);
        }

        if (occurrences.Any(IsCollective))
        {
            return inHisLife ? (Reading.Unsettled, InHisLife) : (Reading.ThePeople, null);
        }

        return inHisLife || peoplesWord ? (Reading.TheMan, null) : (Reading.Unsettled, AfterHisLife);
    }

    private static bool IsCollective(Occurrence occurrence) =>
        occurrence.Head is { } head
        && (Collective.Contains(head) || (Kindred.Contains(head) && occurrence.HeadPlural));

    private static bool IsPersonal(Occurrence occurrence) =>
        occurrence.Head is { } head
        && (Personal.Contains(head) || (Kindred.Contains(head) && !occurrence.HeadPlural));

    /// <summary>
    /// Whether the verse names the man's kin the way a genealogy does. A kinsman counts where his name
    /// is his own and not his tribe's or his land's — not under <em>the sons of</em>, <em>the cities
    /// of</em> — and the verse is a genealogy where it names somebody of the family no people is named
    /// after, <see cref="Genealogy"/> who are, or one of the man's children as the very next word after
    /// <em>the sons of</em> him.
    /// </summary>
    /// <param name="occurrences">The man's own name in the verse.</param>
    /// <param name="kin">Each occurrence of a kinsman's name in the verse, by the kinsman.</param>
    /// <param name="ancestors">Every record a people is named after.</param>
    internal static bool Genealogical(
        IReadOnlyList<Occurrence> occurrences,
        IReadOnlyList<(int Kinsman, Occurrence At)> kin,
        Family family,
        IReadOnlySet<int> ancestors)
    {
        var persons = kin.Where(k => !IsCollective(k.At) || Divine.Contains(k.At.Head!)).ToList();
        var named = persons.Select(k => k.Kinsman).ToHashSet();
        var sonsOfHim = occurrences
            .Where(o => o.Head is { } head && Kindred.Contains(head) && o.HeadPlural)
            .Select(o => o.Position)
            .ToList();
        return named.Any(k => !ancestors.Contains(k))
               || named.Count >= Genealogy
               || persons.Any(k => family.Children.Contains(k.Kinsman) && sonsOfHim.Contains(k.At.Position - 1));
    }

    /// <summary>The man's kin as <see cref="Read"/> weighs them.</summary>
    /// <param name="Kin">His parents, children and siblings.</param>
    /// <param name="Children">Those of them who are his children.</param>
    internal sealed record Family(IReadOnlySet<int> Kin, IReadOnlySet<int> Children);

    /// <summary>
    /// A row the dataset files under the wrong record, by the record's source id, the verse and the
    /// label, with the verse it belongs at — or none, where it belongs nowhere on this record — and why.
    /// </summary>
    internal sealed record MisfiledRow(string SourceId, string Reference, string Label, string? MovesTo, string Why);

    internal static readonly IReadOnlyList<MisfiledRow> Misfiled =
    [
        new("person:Satan_1", "ACT 13:9", "the devil", "ACT 13:10",
            "Acts 13:9 is 'Then Saul, (who also is called Paul,) filled with the Holy Ghost'; 'thou child of " +
            "the devil' is the next verse, and the label is that verse's."),
        new("person:Satan_1", "2JN 1:7", "Deceiver", null,
            "2 John 1:7 speaks of the many deceivers entered into the world and says of whoever confesses " +
            "not that Jesus Christ is come in the flesh 'This is a deceiver and an antichrist'; Satan is not named."),
        new("person:the angel of the LORD_1", "2SA 14:17", "the angel of G-d", null,
            "The woman of Tekoa tells David 'as an angel of God, so is my lord the king': a comparison, " +
            "and no angel is there."),
        new("person:the angel of the LORD_1", "2SA 14:20", "the angel of G-d", null,
            "The woman of Tekoa tells David he is wise 'according to the wisdom of an angel of God': a " +
            "comparison, and no angel is there."),
        new("person:the angel of the LORD_1", "2SA 19:27", "the angel of G-d", null,
            "Mephibosheth tells David 'my lord the king is as an angel of God': a comparison, and no angel " +
            "is there."),
    ];

    private const string Dataset = BibleDataLoader.Source;

    private static readonly string[] Witnesses = ["BHSA", "NESTLE1904"];

    /// <summary>The separator a name row joins the numbers of a phrase's words with.</summary>
    private const char Joined = ',';

    /// <summary>The article, which BHSA writes as a word of its own between a construct noun and its name.</summary>
    private static readonly string[] Articles = ["H9009", "G3588"];

    /// <summary>BHSA's construct state: the noun is the first of a phrase, <em>sons of</em>.</summary>
    private const string Construct = "c";

    private const string Plural = "pl";

    private const string GreekPlural = "plural";

    /// <summary>The relation words a row reading from a parent to the child is written with.</summary>
    private static readonly string[] Parents = ["father", "mother"];

    /// <summary>The relation words a row reading from a child to the parent is written with.</summary>
    private static readonly string[] Offspring = ["son", "daughter"];

    /// <summary>The other relation words that make somebody the man's close kin, as the dataset writes them.</summary>
    private static readonly string[] Kin =
    [
        "brother", "sister", "half-brother", "half-sister", "grandfather", "grandmother", "grandson",
        "granddaughter",
    ];

    /// <summary>The book codes the review lists write a verse with, in canonical order.</summary>
    private static readonly string[] Codes =
    [
        "GEN", "EXO", "LEV", "NUM", "DEU", "JOS", "JDG", "RUT", "1SA", "2SA", "1KI", "2KI", "1CH",
        "2CH", "EZR", "NEH", "EST", "JOB", "PSA", "PRO", "ECC", "SNG", "ISA", "JER", "LAM", "EZK",
        "DAN", "HOS", "JOL", "AMO", "OBA", "JON", "MIC", "NAM", "HAB", "ZEP", "HAG", "ZEC", "MAL",
        "MAT", "MRK", "LUK", "JHN", "ACT", "ROM", "1CO", "2CO", "GAL", "EPH", "PHP", "COL", "1TH",
        "2TH", "1TI", "2TI", "TIT", "PHM", "HEB", "JAS", "1PE", "2PE", "1JN", "2JN", "3JN", "JUD",
        "REV",
    ];

    /// <param name="resources">The corpus sources, under which the review lists are kept.</param>
    public async Task<MisfiledVerseOutcome> Load(string resources, CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.StartNew();
        var misfiled = await PutRight(cancellationToken);

        var review = Path.Combine(resources, "Essenthos", "review", ReviewFile);
        var answers = Answers(review);

        var rows = await Rows(cancellationToken);
        var moving = new List<Row>();
        var open = new List<(Row Row, string Why, string Strong)>();
        var decided = 0;
        foreach (var (row, reading, why, strong) in await ReadAll(rows, cancellationToken))
        {
            if (reading == Reading.ThePeople)
            {
                moving.Add(row);
            }
            else if (reading == Reading.Unsettled && answers.TryGetValue((row.Eponym, row.Label, row.Reference), out var answer))
            {
                decided++;
                if (answer == row.People)
                {
                    moving.Add(row);
                }
            }
            else if (reading == Reading.Unsettled)
            {
                open.Add((row, why!, strong));
            }
        }

        await Move(moving, cancellationToken);
        if (Directory.Exists(Path.GetDirectoryName(review)))
        {
            Write(review, open);
        }

        var outcome = new MisfiledVerseOutcome(
            rows.Count, moving.Count, decided, open.Count, misfiled, started.Elapsed);
        logger.LogInformation("The verses filed under the wrong record: {Outcome}", outcome);
        return outcome;
    }

    /// <summary>One of the dataset's rows on a man a people is named after.</summary>
    internal sealed record Row(
        int Id,
        int EponymId,
        string Eponym,
        string EponymName,
        int PeopleId,
        string People,
        string PeopleName,
        string Label,
        int Book,
        int Chapter,
        int Verse)
    {
        /// <summary>The verse as the review lists write it: <c>EXO 25:2</c>.</summary>
        public string Reference => $"{Codes[Book - 1]} {Chapter}:{Verse}";
    }

    private async Task<List<Row>> Rows(CancellationToken cancellationToken)
    {
        // A record two peoples are named after has no one people to give a verse to, so its rows are
        // left alone; among persons there is none today.
        const string sql =
            """
            SELECT v.id, e.id, e.slug, e.name, min(p.id), min(p.slug), min(p.name), v.label,
                   v.canonical_book, v.canonical_chapter, v.canonical_verse
            FROM entity_verse v
            JOIN entity e ON e.id = v.entity_id AND e.kind = @person
            JOIN entity p ON p.origin_entity_id = e.id AND p.kind = @people
            WHERE v.source = @dataset AND v.label IS NOT NULL
            GROUP BY v.id, e.id
            HAVING count(p.id) = 1
            ORDER BY e.slug, v.canonical_book, v.canonical_chapter, v.canonical_verse, v.id
            """;
        var rows = new List<Row>();
        await using var command = await Command(sql, cancellationToken);
        command.Parameters.AddWithValue("person", EnumSpelling.Of(EntityKind.Person));
        command.Parameters.AddWithValue("people", EnumSpelling.Of(EntityKind.People));
        command.Parameters.AddWithValue("dataset", Dataset);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new Row(
                reader.GetInt32(0), reader.GetInt32(1), reader.GetString(2), reader.GetString(3),
                reader.GetInt32(4), reader.GetString(5), reader.GetString(6), reader.GetString(7),
                reader.GetInt32(8), reader.GetInt32(9), reader.GetInt32(10)));
        }

        return rows;
    }

    /// <summary>One word of a verse the rows cite, with the noun it stands second to.</summary>
    private sealed record Word(string Strong, int Position, string? Head, bool HeadPlural, int[] Names);

    /// <returns>Each row's reading, and the number of the name its label means, for the review list.</returns>
    private async Task<List<(Row Row, Reading Reading, string? Why, string Strong)>> ReadAll(
        List<Row> rows, CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
        {
            return [];
        }

        var eponyms = rows.Select(r => r.EponymId).Distinct().ToArray();
        var peoples = rows.Select(r => r.PeopleId).Distinct().ToArray();
        var families = await Families(eponyms, cancellationToken);
        var ancestors = await Ancestors(cancellationToken);
        var lives = await Lives(eponyms, cancellationToken);

        var named = eponyms.Concat(peoples).Concat(families.Values.SelectMany(f => f.Kin)).Distinct().ToArray();
        var (single, byLabel) = await Names(named, cancellationToken);
        var numbers = single.Values.SelectMany(n => n).Distinct(StringComparer.Ordinal).ToArray();
        var words = await Words(rows, numbers, cancellationToken);

        var readings = new List<(Row, Reading, string?, string)>(rows.Count);
        foreach (var row in rows)
        {
            var own = single.GetValueOrDefault(row.EponymId) ?? [];
            var labelled = byLabel.GetValueOrDefault((row.EponymId, row.Label))?
                .Where(own.Contains).ToHashSet(StringComparer.Ordinal) ?? [];
            var verse = words.GetValueOrDefault((row.Book, row.Chapter, row.Verse)) ?? [];

            var occurrences = Occurrences(verse, labelled, row);
            if (occurrences.Count == 0)
            {
                occurrences = Occurrences(verse, own, row);
            }

            var peoplesWord = verse.Any(w =>
                !own.Contains(w.Strong) && (single.GetValueOrDefault(row.PeopleId)?.Contains(w.Strong) ?? false));
            var family = families[row.EponymId];
            var kin = family.Kin
                .SelectMany(k => verse
                    .Where(w => single.GetValueOrDefault(k)?.Contains(w.Strong) ?? false)
                    .Select(w => (k, new Occurrence(w.Position, w.Head, w.HeadPlural))))
                .ToList();
            var kinNamed = Genealogical(occurrences, kin, family, ancestors);
            var inHisLife = row.Book <= lives.GetValueOrDefault(row.EponymId, int.MaxValue);

            var (reading, why) = Read(occurrences, kinNamed, inHisLife, peoplesWord);
            var language = row.Book > BookReferences.OldTestamentBookCount ? "G" : "H";
            var strong = labelled
                .OrderBy(n => n.StartsWith(language, StringComparison.Ordinal) ? 0 : 1)
                .ThenBy(n => n, StringComparer.Ordinal)
                .FirstOrDefault() ?? string.Empty;
            readings.Add((row, reading, why, strong));
        }

        return readings;
    }

    private static List<Occurrence> Occurrences(List<Word> verse, IReadOnlySet<string> numbers, Row row) =>
    [
        .. verse
            .Where(w => numbers.Contains(w.Strong))
            .Select(w => new Occurrence(
                w.Position, w.Head, w.HeadPlural, w.Names.Contains(row.PeopleId), w.Names.Contains(row.EponymId))),
    ];

    /// <summary>
    /// Each man's parents, children and siblings. The dataset writes a tie between parent and child
    /// from both ends, and siblings mostly not at all, so brothers are also read as the other children
    /// of his parents: <em>the sons of Levi; Gershon, Kohath, and Merari</em> is a genealogy for each of
    /// the three.
    /// </summary>
    private async Task<Dictionary<int, Family>> Families(int[] eponyms, CancellationToken cancellationToken)
    {
        const string sql =
            """
            WITH descent AS (
                SELECT CASE WHEN r.type = ANY(@parents) THEN r.from_entity_id ELSE r.to_entity_id END AS parent,
                       CASE WHEN r.type = ANY(@parents) THEN r.to_entity_id ELSE r.from_entity_id END AS child
                FROM entity_relationship r
                WHERE (r.type = ANY(@parents) OR r.type = ANY(@offspring)) AND r.from_entity_id <> r.to_entity_id)
            SELECT child, parent, FALSE FROM descent WHERE child = ANY(@eponyms)
            UNION SELECT parent, child, TRUE FROM descent WHERE parent = ANY(@eponyms)
            UNION SELECT a.child, b.child, FALSE FROM descent a JOIN descent b ON b.parent = a.parent
                  WHERE a.child = ANY(@eponyms) AND b.child <> a.child
            UNION SELECT r.from_entity_id, r.to_entity_id, FALSE FROM entity_relationship r
                  WHERE r.type = ANY(@kin) AND r.from_entity_id = ANY(@eponyms) AND r.from_entity_id <> r.to_entity_id
            UNION SELECT r.to_entity_id, r.from_entity_id, FALSE FROM entity_relationship r
                  WHERE r.type = ANY(@kin) AND r.to_entity_id = ANY(@eponyms) AND r.from_entity_id <> r.to_entity_id
            """;
        var kin = eponyms.ToDictionary(id => id, _ => new HashSet<int>());
        var children = eponyms.ToDictionary(id => id, _ => new HashSet<int>());
        await using var command = await Command(sql, cancellationToken);
        command.Parameters.AddWithValue("parents", Parents);
        command.Parameters.AddWithValue("offspring", Offspring);
        command.Parameters.AddWithValue("kin", Kin);
        command.Parameters.AddWithValue("eponyms", eponyms);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var (of, who) = (reader.GetInt32(0), reader.GetInt32(1));
            kin[of].Add(who);
            if (reader.GetBoolean(2))
            {
                children[of].Add(who);
            }
        }

        return eponyms.ToDictionary(id => id, id => new Family(kin[id], children[id]));
    }

    /// <summary>Every record some people is named after.</summary>
    private async Task<HashSet<int>> Ancestors(CancellationToken cancellationToken) =>
    [
        .. await db.Entities
            .Where(e => e.Kind == EntityKind.People && e.OriginEntityId != null)
            .Select(e => e.OriginEntityId!.Value)
            .ToListAsync(cancellationToken),
    ];

    /// <summary>
    /// The last book in which each man lives: the book of the latest event the dataset dates for him —
    /// his death, where it dates one — and where it dates none, the first book that names him.
    /// </summary>
    private async Task<Dictionary<int, int>> Lives(int[] eponyms, CancellationToken cancellationToken)
    {
        const string sql =
            """
            SELECT e.id,
                   coalesce((SELECT max(v.canonical_book) FROM event v WHERE v.entity_id = e.id),
                            (SELECT min(n.canonical_book) FROM entity_verse n WHERE n.entity_id = e.id))
            FROM entity e WHERE e.id = ANY(@eponyms)
            """;
        var lives = new Dictionary<int, int>();
        await using var command = await Command(sql, cancellationToken);
        command.Parameters.AddWithValue("eponyms", eponyms);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (!reader.IsDBNull(1))
            {
                lives[reader.GetInt32(0)] = reader.GetInt32(1);
            }
        }

        return lives;
    }

    /// <summary>
    /// The Strong numbers each record's names are written with: those of a name that is one word,
    /// which is what a verse's word can be matched to, and every number of each label, so a row's
    /// label can pick out which of the man's names it means. <em>Israel my servant</em> is two words
    /// and means the name Israel; <em>Ben-ammi</em> is two, and neither is a name of his alone.
    /// </summary>
    private async Task<(Dictionary<int, HashSet<string>> Single, Dictionary<(int, string), HashSet<string>> ByLabel)> Names(
        int[] ids, CancellationToken cancellationToken)
    {
        var names = await db.EntityNames
            .Where(n => ids.Contains(n.EntityId))
            .Select(n => new { n.EntityId, n.Label, n.HebrewStrongNumber, n.GreekStrongNumber })
            .ToListAsync(cancellationToken);

        var single = new Dictionary<int, HashSet<string>>();
        var byLabel = new Dictionary<(int, string), HashSet<string>>();
        foreach (var name in names)
        {
            foreach (var number in new[] { name.HebrewStrongNumber, name.GreekStrongNumber }.OfType<string>())
            {
                var parts = number.Split(Joined, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (!byLabel.TryGetValue((name.EntityId, name.Label), out var labelled))
                {
                    byLabel[(name.EntityId, name.Label)] = labelled = new HashSet<string>(StringComparer.Ordinal);
                }

                labelled.UnionWith(parts);
                if (parts.Length == 1)
                {
                    if (!single.TryGetValue(name.EntityId, out var own))
                    {
                        single[name.EntityId] = own = new HashSet<string>(StringComparer.Ordinal);
                    }

                    own.Add(parts[0]);
                }
            }
        }

        return (single, byLabel);
    }

    /// <summary>
    /// The words of the witnesses that carry one of the numbers, in the verses the rows cite, each with
    /// the noun it stands second to: the word before it, or the one before the article, and in Hebrew
    /// only where that word is in the construct state.
    /// </summary>
    private async Task<Dictionary<(int, int, int), List<Word>>> Words(
        List<Row> rows, string[] numbers, CancellationToken cancellationToken)
    {
        const string sql =
            """
            SELECT r.canonical_book, r.canonical_chapter, r.canonical_verse, w.strong_number, w.position,
                   head.strong_number, head.morphology ->> 'state', head.morphology ->> 'number',
                   left(w.strong_number, 1) = 'G',
                   array(SELECT a.entity_id FROM word_entity a WHERE a.word_id = w.id)
            FROM (SELECT DISTINCT * FROM unnest(@books, @chapters, @verses) AS q(b, c, v)) q
            JOIN verse_reference r
              ON r.canonical_book = q.b AND r.canonical_chapter = q.c AND r.canonical_verse = q.v AND r.is_primary
            JOIN word w ON w.verse_id = r.verse_id AND w.strong_number = ANY(@numbers)
            JOIN text t ON t.id = w.text_id AND t.slug = ANY(@witnesses)
            LEFT JOIN word before ON before.verse_id = w.verse_id AND before.position = w.position - 1
            LEFT JOIN word head ON head.verse_id = w.verse_id
                               AND head.position = w.position - CASE WHEN before.strong_number = ANY(@articles) THEN 2 ELSE 1 END
            """;
        var words = new Dictionary<(int, int, int), List<Word>>();
        await using var command = await Command(sql, cancellationToken);
        command.Parameters.AddWithValue("books", rows.Select(r => r.Book).ToArray());
        command.Parameters.AddWithValue("chapters", rows.Select(r => r.Chapter).ToArray());
        command.Parameters.AddWithValue("verses", rows.Select(r => r.Verse).ToArray());
        command.Parameters.AddWithValue("numbers", numbers);
        command.Parameters.AddWithValue("witnesses", Witnesses);
        command.Parameters.AddWithValue("articles", Articles);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var key = (reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2));
            var greek = reader.GetBoolean(8);
            var state = reader.IsDBNull(6) ? null : reader.GetString(6);
            var number = reader.IsDBNull(7) ? null : reader.GetString(7);
            var head = reader.IsDBNull(5) || (!greek && state != Construct) ? null : reader.GetString(5);
            if (!words.TryGetValue(key, out var verse))
            {
                words[key] = verse = [];
            }

            verse.Add(new Word(
                reader.GetString(3), reader.GetInt32(4), head, number is Plural or GreekPlural,
                reader.GetFieldValue<int[]>(9)));
        }

        return words;
    }

    /// <summary>Moves each row to its people, or drops it where the people already holds the same row.</summary>
    private async Task Move(List<Row> rows, CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
        {
            return;
        }

        const string sql =
            """
            WITH m AS (SELECT * FROM unnest(@ids, @peoples) AS m(id, people)),
            held AS (
                DELETE FROM entity_verse v USING m, entity_verse kept
                WHERE v.id = m.id AND kept.entity_id = m.people AND kept.source = v.source
                  AND kept.label IS NOT DISTINCT FROM v.label
                  AND (kept.canonical_book, kept.canonical_chapter, kept.canonical_verse)
                    = (v.canonical_book, v.canonical_chapter, v.canonical_verse)
                RETURNING v.id)
            UPDATE entity_verse v SET entity_id = m.people
            FROM m WHERE v.id = m.id AND v.id NOT IN (SELECT id FROM held)
            """;
        await using var command = await Command(sql, cancellationToken);
        command.Parameters.AddWithValue("ids", rows.Select(r => r.Id).ToArray());
        command.Parameters.AddWithValue("peoples", rows.Select(r => r.PeopleId).ToArray());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>Moves or withdraws each row of <see cref="Misfiled"/> still where the dataset put it.</summary>
    private async Task<int> PutRight(CancellationToken cancellationToken)
    {
        const string sql =
            """
            WITH row AS (
                SELECT v.id, v.entity_id FROM entity_verse v JOIN entity e ON e.id = v.entity_id
                WHERE e.source_id = @record AND v.source = @dataset AND v.label = @label
                  AND (v.canonical_book, v.canonical_chapter, v.canonical_verse) = (@book, @chapter, @verse)),
            there AS (
                SELECT 1 FROM entity_verse t, row
                WHERE t.entity_id = row.entity_id AND t.source = @dataset AND t.label = @label
                  AND (t.canonical_book, t.canonical_chapter, t.canonical_verse) = (@toBook, @toChapter, @toVerse)),
            withdrawn AS (
                DELETE FROM entity_verse v USING row
                WHERE v.id = row.id AND (@toBook = 0 OR EXISTS (SELECT 1 FROM there))
                RETURNING 1),
            moved AS (
                UPDATE entity_verse v
                SET canonical_book = @toBook, canonical_chapter = @toChapter, canonical_verse = @toVerse
                FROM row WHERE v.id = row.id AND @toBook <> 0 AND NOT EXISTS (SELECT 1 FROM there)
                RETURNING 1)
            SELECT (SELECT count(*) FROM withdrawn) + (SELECT count(*) FROM moved)
            """;
        var changed = 0;
        foreach (var misfiled in Misfiled)
        {
            var at = TitleLoader.Verse(misfiled.Reference)
                     ?? throw new InvalidDataException(
                         $"{misfiled.Reference} is not a verse; write a misfiled row's reference as ACT 13:9.");
            var to = misfiled.MovesTo is null ? (0, 0, 0)
                : TitleLoader.Verse(misfiled.MovesTo)
                  ?? throw new InvalidDataException(
                      $"{misfiled.MovesTo} is not a verse; write where a misfiled row belongs as ACT 13:10.");

            await using var command = await Command(sql, cancellationToken);
            command.Parameters.AddWithValue("record", misfiled.SourceId);
            command.Parameters.AddWithValue("dataset", Dataset);
            command.Parameters.AddWithValue("label", misfiled.Label);
            command.Parameters.AddWithValue("book", at.Book);
            command.Parameters.AddWithValue("chapter", at.Chapter);
            command.Parameters.AddWithValue("verse", at.Verse);
            command.Parameters.AddWithValue("toBook", to.Item1);
            command.Parameters.AddWithValue("toChapter", to.Item2);
            command.Parameters.AddWithValue("toVerse", to.Item3);
            var count = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
            if (count > 0)
            {
                logger.LogInformation("{Record} at {Reference}: {Why}", misfiled.SourceId, misfiled.Reference, misfiled.Why);
            }

            changed += count;
        }

        return changed;
    }

    /// <summary>
    /// The owner's answers on the review list, by man, label and verse: the people's slug to move the
    /// verse, or anything else to leave it with him.
    /// </summary>
    internal static Dictionary<(string Eponym, string Label, string Reference), string> Answers(string path)
    {
        var answers = new Dictionary<(string, string, string), string>();
        foreach (var entry in Entries(path).Where(e => e["decision"]?["answer"] is not null))
        {
            var answer = entry["decision"]!["answer"]!.GetValue<string>();
            var record = entry["record"]?.GetValue<string>() ?? string.Empty;
            var label = entry["label"]?.GetValue<string>() ?? string.Empty;
            foreach (var reference in entry["references"]?.AsArray() ?? [])
            {
                answers[(record, label, reference!.GetValue<string>())] = answer;
            }
        }

        return answers;
    }

    private static List<JsonObject> Entries(string path) =>
        File.Exists(path)
            ? [.. (JsonNode.Parse(File.ReadAllText(path))?["entries"]?.AsArray() ?? []).OfType<JsonObject>()]
            : [];

    private static readonly JsonWriterOptions Layout = new()
    {
        Indented = true,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// The review list: the answered entries as they stand, then one open entry for each man, label
    /// and reason still unsettled. Written only when it changes.
    /// </summary>
    internal static void Write(string path, IReadOnlyList<(Row Row, string Why, string Strong)> open)
    {
        var entries = new JsonArray();
        foreach (var answered in Entries(path).Where(e => e["decision"] is not null))
        {
            entries.Add(answered.DeepClone());
        }

        foreach (var group in open.GroupBy(o => (o.Row.Eponym, o.Row.Label, o.Why)))
        {
            var row = group.First().Row;
            entries.Add(new JsonObject
            {
                ["record"] = row.Eponym,
                ["label"] = row.Label,
                ["strong"] = group.First().Strong,
                ["references"] = new JsonArray([.. group.Select(o => (JsonNode?)o.Row.Reference).Distinct()]),
                ["question"] = Question(row, group.Key.Why),
                ["options"] = new JsonArray(row.People, LeaveIt),
            });
        }

        var document = new JsonObject
        {
            ["about"] = ReviewAbout,
            ["entries"] = entries,
        };

        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, Layout))
        {
            document.WriteTo(writer);
        }

        var text = Encoding.UTF8.GetString(buffer.ToArray()) + "\n";
        if (!File.Exists(path) || File.ReadAllText(path) != text)
        {
            File.WriteAllText(path, text);
        }
    }

    private static string Question(Row row, string why) => why switch
    {
        InHisLife =>
            $"BibleData files these verses under {row.EponymName} as '{row.Label}', where the name stands in a " +
            $"phrase that elsewhere names the people — the sons of, the house of — but within {row.EponymName}'s " +
            $"own lifetime, when it can mean his own children. Do they name the man, or the {row.PeopleName}?",
        NotPrinted =>
            $"BibleData files these verses under {row.EponymName} as '{row.Label}', and neither the Hebrew nor " +
            $"the Greek of them prints that name, so nothing in their words says whom it meant. Do they name the " +
            $"man, the {row.PeopleName}, or nobody of the name?",
        _ =>
            $"BibleData files these verses under {row.EponymName} as '{row.Label}'. They come after his death in " +
            $"the text, and the name stands on its own, in no phrase that says whether it is the man or his " +
            $"people. Do they name the man, or the {row.PeopleName}?",
    };

    private async Task<NpgsqlCommand> Command(string sql, CancellationToken cancellationToken)
    {
        await db.Database.OpenConnectionAsync(cancellationToken);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        return new NpgsqlCommand(sql, connection)
        {
            CommandTimeout = Annotating.Patient,
            Transaction = db.Database.CurrentTransaction?.GetDbTransaction() as NpgsqlTransaction,
        };
    }
}
