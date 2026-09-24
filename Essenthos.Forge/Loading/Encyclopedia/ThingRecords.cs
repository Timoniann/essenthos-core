using System.Reflection;
using System.Text.Json;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;

namespace Essenthos.Core.Loading.Encyclopedia;

/// <summary>
/// The records the files hold, as the files say them. The objects and the observances are one list,
/// read from two files because they were decided and reviewed as two lists and not because they are
/// two shapes: an object and an observance are the same record with different fields filled. The
/// beings, things and places the narratives turn on are another list, in a file of their own.
/// </summary>
internal static class ThingFiles
{
    private static readonly JsonSerializerOptions Shape = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static IReadOnlyList<ThingRecord> Read() => Read(ThingSet.MadeAndKept).Records;

    /// <summary>Every file of a list, as one: its records, and the words it names for records it does not write.</summary>
    public static ThingFile Read(ThingSet set)
    {
        var files = set.Resources.Select(Read).ToList();
        return new ThingFile(
            string.Join(" ", files.Select(f => f.DecidedBy)),
            [.. files.SelectMany(f => f.Records)],
            [.. files.SelectMany(f => f.Refers ?? [])]);
    }

    private static ThingFile Read(string resource)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource)
                           ?? throw new InvalidOperationException($"{resource} is not embedded in the Forge assembly.");
        return JsonSerializer.Deserialize<ThingFile>(stream, Shape) ?? new ThingFile("", [], null);
    }
}

/// <summary>
/// One list of records, and the sources its rows are written under — which is also how a load of
/// the list finds what it wrote last time and takes back only that.
/// </summary>
/// <param name="About">What the list is, in a phrase the load's log reads with.</param>
/// <param name="Source">What wrote its records and the words no person has reviewed yet.</param>
/// <param name="ReviewedSource">What wrote a rule or a record the owner has reviewed, which is then his ruling.</param>
/// <param name="PassageSource">What says a verse stands in a passage that commands a record.</param>
/// <param name="Resources">The embedded files the list is read from.</param>
internal sealed record ThingSet(
    string About,
    string Source,
    string ReviewedSource,
    string PassageSource,
    IReadOnlyList<string> Resources)
{
    public const string MadeAndKeptAbout = "the objects and the appointed times";

    public const string MadeAndKeptSource =
        "Essenthos, read from each verse by a language model on 2026-09-23, on the project owner's " +
        "decision that objects and appointed times are records of their own";

    public const string MadeAndKeptReviewedSource =
        "Essenthos, on the project owner's review of the readings of objects and appointed times";

    /// <summary>
    /// What says an appointed time stands in each verse of the passages that command it. Not that a
    /// word there names it — Leviticus 16 never says <em>the day of atonement</em> and is the whole of
    /// how it is kept — so these are references from the passage, read as such by the model that
    /// listed the passages, and they say so beside the ones the words give.
    /// </summary>
    public const string MadeAndKeptPassageSource =
        "Essenthos, from the passages that command it, as a language model read them on 2026-09-23";

    public static readonly ThingSet MadeAndKept = new(
        MadeAndKeptAbout,
        MadeAndKeptSource,
        MadeAndKeptReviewedSource,
        MadeAndKeptPassageSource,
        [
            "Essenthos.Core.Loading.Encyclopedia.ObjectRecords.json",
            "Essenthos.Core.Loading.Encyclopedia.ObservanceRecords.json",
        ]);

    /// <summary>
    /// The serpent and the cherubim of Eden, the Holy Spirit, the New Jerusalem, Nathanael, and the
    /// words the narrative refers to a named record by without its name.
    /// </summary>
    public static readonly ThingSet Narratives = new(
        "the beings, the things and the places the narratives turn on",
        "Essenthos, read from each verse by a language model on 2026-09-24, on the project owner's " +
        "decision that the beings, things and places the narratives turn on are records of their own",
        "Essenthos, on the project owner's rulings on the beings, things and places the narratives turn on",
        "Essenthos, from the passages about the beings, things and places the narratives turn on, as a " +
        "language model read them on 2026-09-24",
        ["Essenthos.Core.Loading.Encyclopedia.NarrativeRecords.json"]);

    public string SourceOf(bool reviewed) => reviewed ? ReviewedSource : Source;
}

/// <param name="DecidedBy">Who decided that these are records, and when, in a sentence.</param>
/// <param name="Refers">Words the file names for records it does not write itself.</param>
internal sealed record ThingFile(
    string DecidedBy,
    IReadOnlyList<ThingRecord> Records,
    IReadOnlyList<ThingReference>? Refers = null);

/// <summary>
/// Words that refer to a record the file does not write — a dataset's, or another list's — without
/// spelling its name: <em>the woman</em> of Genesis 3, who is Eve. The record is left as it is; only
/// the words are named, by the same rules and under the same sources as the file's own records.
/// </summary>
/// <param name="Why">Why these occurrences are that record, which is what each word's note rests on.</param>
internal sealed record ThingReference(string Slug, IReadOnlyList<OccurrenceRule> Occurrences, string Why);

/// <summary>One object or observance, with everything the page and the reader need of it.</summary>
/// <param name="Kind"><c>object</c> or <c>observance</c>, as the kind is spelled in the database.</param>
/// <param name="Names">The name in a reader's language, keyed by the language's code: <c>ukr</c>, <c>deu</c>, <c>spa</c>.</param>
/// <param name="Forms">
/// The name in the other cases a phrase puts it in, where a language declines it — the genitive of
/// <em>ковчег заповіту</em> that <em>частина ковчега заповіту</em> needs.
/// </param>
/// <param name="Called">
/// The names it is called by, each whole: the text's own phrase in Hebrew or Greek with the Strong
/// number of each of its words comma-joined, the transliteration a reader searches with, and its
/// other names in English and Ukrainian. Never one word of a phrase on its own — <em>day</em> is not a
/// name of the Day of Atonement.
/// </param>
/// <param name="Why">Why these occurrences are this record, which is what the record's claim says.</param>
/// <param name="Reviewed">
/// Who reviewed the record itself — its claim, its relations and its names — and when, once somebody
/// has. Absent, the record is a model's reading and says so.
/// </param>
internal sealed record ThingRecord(
    string Slug,
    string Kind,
    string Subtype,
    string Name,
    IReadOnlyDictionary<string, string>? Names,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? Forms,
    string Distinguisher,
    string? Notes,
    IReadOnlyList<ThingWord>? Called,
    IReadOnlyList<ThingPassage>? Passages,
    IReadOnlyList<ThingTime>? Times,
    IReadOnlyList<ThingRelation>? Related,
    IReadOnlyList<OccurrenceRule>? Occurrences,
    string? Why,
    string? Reviewed = null)
{
    public EntityKind EntityKind => EnumSpelling.ToEntityKind(Kind);
}

/// <summary>
/// A name the thing is called by. <see cref="HebrewStrongNumber"/> and <see cref="GreekStrongNumber"/>
/// hold the number of each word of it, comma-joined as a title's are, and are absent on a name that is
/// no word of the text — a transliteration, or the name in another language.
/// </summary>
internal sealed record ThingWord(
    string Label,
    string? Hebrew,
    string? HebrewTransliterated,
    string? HebrewStrongNumber,
    string? Greek,
    string? GreekTransliterated,
    string? GreekStrongNumber,
    string? Meaning);

/// <param name="Role">One of <see cref="PassageRoles"/>.</param>
internal sealed record ThingPassage(string Reference, string Role, string? Note);

/// <param name="Cycle">One of <see cref="ObservanceCycles"/>.</param>
/// <param name="Reference">The verse that states it, which is required: a date nobody can check is a guess.</param>
internal sealed record ThingTime(string Cycle, int? Month, int? Day, int? LastDay, string Reference, string? Note);

/// <param name="Type">One of <see cref="ThingRelations"/>.</param>
/// <param name="To">The slug of the record it relates to, of any kind.</param>
/// <param name="Reference">The verse that says so.</param>
internal sealed record ThingRelation(string Type, string To, string Reference, string? Note);

/// <summary>
/// Which occurrences of one Strong number are the record: all of them, or those <see cref="Only"/>
/// names and <see cref="Except"/> does not, and only where <see cref="With"/> stands beside it when
/// that is given.
/// </summary>
/// <param name="Only">
/// Spans the occurrence has to stand in — a book (<c>LEV</c>), a chapter, a run of chapters
/// (<c>GEN 6-9</c>), a verse or a run of verses, and optionally which occurrence of the number in
/// the verse (<c>NUM 8:2#2</c>). Absent means everywhere.
/// </param>
/// <param name="With">
/// Another Strong number that has to stand within <see cref="ThingLoader.Reach"/> words of the
/// occurrence in the same verse: <em>altar</em> beside <em>incense</em>.
/// </param>
/// <param name="Reviewed">
/// Who reviewed these occurrences and when — <c>the project owner, 2026-10-01</c>. That is what turns
/// a model's reading of them into a ruling; absent, they are the reading.
/// </param>
internal sealed record OccurrenceRule(
    string Strong,
    IReadOnlyList<string>? Only,
    IReadOnlyList<string>? Except,
    string? With,
    string? Reviewed = null)
{
    public bool Admits(int book, int chapter, int verse, int nth) =>
        (Only is null || Only.Any(span => ScriptureSpan.Parse(span).Holds(book, chapter, verse, nth)))
        && (Except is null || !Except.Any(span => ScriptureSpan.Parse(span).Holds(book, chapter, verse, nth)));
}

/// <summary>The relations a record of this kind stands in, each resting on a verse.</summary>
internal static class ThingRelations
{
    /// <summary>A is a part of B: the mercy seat of the ark.</summary>
    public const string PartOf = "part-of";

    /// <summary>A was kept or laid up in B: the tablets in the ark.</summary>
    public const string KeptIn = "kept-in";

    /// <summary>A stood in B, a structure: the menorah in the tabernacle.</summary>
    public const string StandsIn = "stands-in";

    /// <summary>A structure stands at a place: Solomon's temple on Mount Moriah.</summary>
    public const string StandsAt = "stands-at";

    /// <summary>Who made or built it.</summary>
    public const string MadeBy = "made-by";

    /// <summary>What was built in its stead.</summary>
    public const string ReplacedBy = "replaced-by";

    /// <summary>Where an observance was kept.</summary>
    public const string HeldAt = "held-at";

    /// <summary>What an observance recalls.</summary>
    public const string Commemorates = "commemorates";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        PartOf, KeptIn, StandsIn, StandsAt, MadeBy, ReplacedBy, HeldAt, Commemorates,
    };
}

/// <summary>
/// A stretch of Scripture as the record files write one: <c>EXO 25:10-22</c>, <c>EXO 25:10-26:37</c>,
/// <c>LEV 23</c>, <c>GEN 6-9</c>, <c>LEV</c>, <c>NUM 8:2#2</c>. Canonical numbering throughout.
/// </summary>
/// <param name="FromVerse">Null where the span opens at the start of its first chapter.</param>
/// <param name="ToVerse">Null where the span runs to the end of its last chapter.</param>
/// <param name="Nth">Which occurrence of a number in the verse, for a span naming one verse.</param>
internal sealed record ScriptureSpan(int Book, int FromChapter, int? FromVerse, int ToChapter, int? ToVerse, int? Nth)
{
    /// <summary>The last chapter any book has, which is what a book named alone runs to.</summary>
    private const int EveryChapter = 150;

    public bool Holds(int book, int chapter, int verse, int nth) =>
        book == Book
        && (chapter > FromChapter || (chapter == FromChapter && (FromVerse is null || verse >= FromVerse)))
        && (chapter < ToChapter || (chapter == ToChapter && (ToVerse is null || verse <= ToVerse)))
        && (Nth is null || nth == Nth);

    /// <summary>True where the span names a verse or verses rather than whole chapters.</summary>
    public bool IsVerse => FromVerse is not null;

    public static ScriptureSpan Parse(string written) =>
        TryParse(written) ?? throw new FormatException(
            $"\"{written}\" is not a span of Scripture. Write a book code and then a chapter, a verse or a " +
            "range, as LEV, GEN 6-9, EXO 25:10-22, EXO 25:10-26:37 or NUM 8:2#2.");

    public static ScriptureSpan? TryParse(string written)
    {
        var text = written.Trim();
        int? nth = null;
        var hash = text.IndexOf('#');
        if (hash > 0)
        {
            if (!int.TryParse(text[(hash + 1)..], out var n) || n < 1)
            {
                return null;
            }

            nth = n;
            text = text[..hash];
        }

        var space = text.IndexOf(' ');
        var code = space < 0 ? text : text[..space];
        if (BookReferences.ResolveOrdinal(code) is not { } book)
        {
            return null;
        }

        if (space < 0)
        {
            return nth is null ? new ScriptureSpan(book, 1, null, EveryChapter, null, null) : null;
        }

        var range = text[(space + 1)..].Split('-');
        if (range.Length is < 1 or > 2 || Point(range[0]) is not { } start)
        {
            return null;
        }

        var (fromChapter, fromVerse) = start;

        var (toChapter, toVerse) = (fromChapter, fromVerse);
        if (range.Length == 2)
        {
            if (range[1].Contains(':'))
            {
                if (Point(range[1]) is not { } end)
                {
                    return null;
                }

                (toChapter, toVerse) = end;
            }
            else if (int.TryParse(range[1], out var last))
            {
                (toChapter, toVerse) = fromVerse is null ? (last, (int?)null) : (fromChapter, last);
            }
            else
            {
                return null;
            }
        }

        if (nth is not null && (fromVerse is null || toChapter != fromChapter || toVerse != fromVerse))
        {
            return null;
        }

        return toChapter < fromChapter || (toChapter == fromChapter && toVerse < fromVerse)
            ? null
            : new ScriptureSpan(book, fromChapter, fromVerse, toChapter, toVerse, nth);
    }

    private static (int Chapter, int? Verse)? Point(string written)
    {
        var parts = written.Split(':');
        if (parts.Length is < 1 or > 2 || !int.TryParse(parts[0], out var chapter) || chapter < 1)
        {
            return null;
        }

        if (parts.Length == 1)
        {
            return (chapter, null);
        }

        return int.TryParse(parts[1], out var verse) && verse >= 1 ? (chapter, verse) : null;
    }
}
