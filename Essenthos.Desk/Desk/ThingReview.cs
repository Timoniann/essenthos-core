using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Essenthos.Core.Corpus;
using Essenthos.Core.Products;

namespace Essenthos.Core.Desk;

/// <summary>A record an option names, so the page can show its name rather than its slug.</summary>
internal sealed record ThingRecordName(string Slug, string Name, string? LocalName, string Kind);

internal sealed record ThingOption(string Text, ThingRecordName? Record);

/// <summary>
/// One edit the build's correction files hold for a question (<c>Essenthos.Forge/Records/OwnerRows/*.tsv</c>,
/// applied by <c>forge owner-rows</c>): a name put on or taken off a word, or a dataset's verse row filed
/// under another record or withdrawn.
/// </summary>
/// <param name="File">The correction file it is in, by name.</param>
/// <param name="Product">The build's file it edits: <c>names/BHSA</c>, <c>external/bibledata/references</c>.</param>
/// <param name="Op"><c>add</c>, <c>remove</c>, <c>refile</c> or <c>withdraw</c>.</param>
/// <param name="Reference">The verse it edits (<c>1 Kgs 7:21</c>), or the row's own key where it is not a verse.</param>
/// <param name="Word">The word of the verse, counted from 1, for an edit of a name; null for a dataset's row.</param>
/// <param name="Record">The record it names, files the row under, or whose row goes.</param>
/// <param name="From">For a refile, the record the row was under.</param>
/// <param name="Why">The reading the edit rests on.</param>
internal sealed record ThingRow(
    string File,
    string Product,
    string Op,
    string Reference,
    int? Word,
    ThingRecordName? Record,
    string RecordSlug,
    string? From,
    string? Strong,
    string Why);

/// <param name="Key">The entry's own key where it has one, else a short digest of its record, number and verses, which is stable while the entry is.</param>
/// <param name="List">Which review list it is on, as the list's file is named without its extension.</param>
/// <param name="Record">The record the entry is about, or null where the entry proposes one nobody has written yet.</param>
/// <param name="Addresses">
/// Each reference as the reading API addresses it, for showing its text, in step with
/// <paramref name="References"/>: null where a reference names more than one verse.
/// </param>
/// <param name="Decision">What the owner answered in the console before it stopped taking answers, or null.</param>
/// <param name="Rows">What the build's correction files hold for the entry: the edits filed against it.</param>
internal sealed record ThingQuestion(
    string Key,
    string List,
    string RecordSlug,
    ThingRecordName? Record,
    string Strong,
    IReadOnlyList<string> References,
    IReadOnlyList<string?> Addresses,
    string Question,
    IReadOnlyList<ThingOption> Options,
    JsonNode? Decision,
    IReadOnlyList<ThingRow> Rows);

/// <param name="List">The file's name without its extension.</param>
/// <param name="About">What the list says it is.</param>
/// <param name="Open">Entries with no answer in the list.</param>
/// <param name="Filed">Entries the correction files hold an edit for.</param>
internal sealed record ThingList(string List, string? About, int Entries, int Open, int Filed);

internal sealed record ThingQuestionsResponse(IReadOnlyList<ThingList> Lists, IReadOnlyList<ThingQuestion> Entries);

/// <param name="File"><c>objects</c>, <c>observances</c> or <c>narratives</c>: which of the record files holds it.</param>
/// <param name="Scope">How far the owner reviewed it while the console took reviews: <c>all</c>, <c>record</c>, or null.</param>
/// <param name="Record">The record as its file holds it.</param>
internal sealed record ThingRecordEntry(string File, string? Scope, int Rules, int ReviewedRules, JsonNode Record);

internal sealed record ThingRecordsResponse(IReadOnlyList<ThingRecordEntry> Records);

/// <summary>
/// The objects and appointed times, read and never written: the questions the review lists asked (the
/// occurrences a reading could not settle, the verses under a people's ancestor, where the Greek stands),
/// with the edits the build's correction files hold for each, and the object, feast and story records
/// with their occurrence rules.
///
/// <para>
/// The console stopped taking answers here on 2026-11-05 (NOT-0219): a correction the owner has, he tells
/// the lead in chat, and the lead writes it into the file the build reads, stamped as his. So these pages
/// show what was asked and what the files now say, and change nothing.
/// </para>
/// </summary>
internal sealed class ThingReview(DeskPaths paths)
{
    public const string QuestionsFile = "objects-and-observances.json";

    /// <summary>The verses a dataset files under a man a people is named after that the load could not settle.</summary>
    public const string AncestorsFile = "eponym-verses.json";

    public const string ObjectsFile = "ObjectRecords.json";

    public const string ObservancesFile = "ObservanceRecords.json";

    /// <summary>The persons and places a story turns on that no dataset holds: the serpent, the cherubim.</summary>
    public const string NarrativesFile = "NarrativeRecords.json";

    /// <summary>What each of <see cref="RecordFiles"/> is called to the console, in the same order.</summary>
    private static readonly string[] FileKeys = ["objects", "observances", "narratives"];

    /// <summary>
    /// How a correction file names a review list in its <c>entry</c> column (<c>objects:41</c> is the 42nd
    /// entry of the objects' list); a list not named here is its file name without the extension.
    /// </summary>
    private static readonly Dictionary<string, string> RowLists = new(StringComparer.Ordinal)
    {
        ["objects"] = "objects-and-observances",
        ["eponyms"] = "eponym-verses",
        ["septuagint"] = "septuagint-placement",
    };

    /// <summary>Where the decided edits ship, under the Forge's folder (<c>OwnerRows.Folder</c> there).</summary>
    public string RowsFolder => Path.Combine(paths.Repository, "Essenthos.Forge", "Records", "OwnerRows");

    /// <summary>Every list of questions in the review folder: the objects' first, the ancestors' next, then the rest by name.</summary>
    private IEnumerable<string> QuestionLists =>
        (Directory.Exists(paths.Review) ? Directory.EnumerateFiles(paths.Review, "*.json") : [])
        .Where(IsQuestionList)
        .OrderBy(path => Path.GetFileName(path) switch { QuestionsFile => 0, AncestorsFile => 1, _ => 2 })
        .ThenBy(path => path, StringComparer.Ordinal);

    /// <summary>
    /// Whether a file in the review folder is a list of questions, remembered while the file is
    /// unchanged: the folder also holds records of past decisions, some of them large.
    /// </summary>
    private bool IsQuestionList(string path)
    {
        var written = File.GetLastWriteTimeUtc(path);
        lock (_shapes)
        {
            if (_shapes.TryGetValue(path, out var known) && known.Written == written)
            {
                return known.IsList;
            }
        }

        bool isList;
        try
        {
            isList = JsonFiles.Read(path) is JsonObject root
                     && root["entries"] is JsonArray entries
                     && entries.OfType<JsonObject>().Any()
                     && entries.OfType<JsonObject>().All(e => e["question"] is not null && e["options"] is JsonArray);
        }
        catch (System.Text.Json.JsonException)
        {
            isList = false;
        }

        lock (_shapes)
        {
            _shapes[path] = (written, isList);
        }

        return isList;
    }

    private readonly Dictionary<string, (DateTime Written, bool IsList)> _shapes = new(StringComparer.OrdinalIgnoreCase);

    private static string ListOf(string path) => Path.GetFileNameWithoutExtension(path);

    private string[] RecordFiles =>
    [
        Path.Combine(paths.Records, ObjectsFile), Path.Combine(paths.Records, ObservancesFile),
        Path.Combine(paths.Records, NarrativesFile),
    ];

    /// <summary>One record file, or none where a checkout does not have it.</summary>
    private static JsonNode ReadRecords(string path) =>
        File.Exists(path) ? JsonFiles.Read(path) : new JsonObject { ["records"] = new JsonArray() };

    public bool Exists => QuestionLists.Any();

    public ThingQuestionsResponse ReadQuestions()
    {
        var records = Records(RecordFiles.Select(ReadRecords).ToArray());
        var rows = Rows(records);
        var lists = new List<ThingList>();
        var entries = new List<ThingQuestion>();
        foreach (var path in QuestionLists)
        {
            var node = JsonFiles.Read(path);
            var listed = (node["entries"]?.AsArray() ?? []).OfType<JsonObject>().ToList();
            var questions = listed
                .Select((entry, index) => Question(ListOf(path), entry, records,
                    rows.TryGetValue($"{ListOf(path)}:{index}", out var filed) ? filed : []))
                .ToList();
            lists.Add(new ThingList(ListOf(path), node["about"]?.GetValue<string>(), listed.Count,
                listed.Count(e => e["decision"] is null), questions.Count(q => q.Rows.Count > 0)));
            entries.AddRange(questions);
        }

        return new ThingQuestionsResponse(lists, entries);
    }

    public ThingRecordsResponse ReadRecords()
    {
        var files = RecordFiles.Select(ReadRecords).ToArray();
        var entries = new List<ThingRecordEntry>();
        for (var index = 0; index < files.Length; index++)
        {
            foreach (var record in (files[index]["records"]?.AsArray() ?? []).OfType<JsonObject>())
            {
                var rules = (record["occurrences"]?.AsArray() ?? []).OfType<JsonObject>().ToList();
                entries.Add(new ThingRecordEntry(
                    FileKeys[index],
                    Scope(record),
                    rules.Count,
                    rules.Count(rule => rule["reviewed"] is not null),
                    record.DeepClone()));
            }
        }

        return new ThingRecordsResponse(entries);
    }

    public static string KeyOf(JsonNode entry)
    {
        if (entry["key"] is JsonValue own && own.TryGetValue<string>(out var key) && key.Length > 0)
        {
            return key;
        }

        var text = $"{entry["record"]}|{entry["strong"]}|{string.Join(",", References(entry))}";
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..12];
    }

    private static IReadOnlyList<string> References(JsonNode entry) =>
        (entry["references"]?.AsArray() ?? []).Select(r => r!.GetValue<string>()).ToList();

    /// <summary>
    /// <c>all</c> where the record and every rule carry a review, <c>record</c> where the record does
    /// and some rule does not, and null where the record carries none.
    /// </summary>
    private static string? Scope(JsonObject record)
    {
        if (record["reviewed"] is null)
        {
            return null;
        }

        return (record["occurrences"]?.AsArray() ?? []).OfType<JsonObject>().All(rule => rule["reviewed"] is not null)
            ? "all"
            : "record";
    }

    /// <summary>
    /// The edits of the correction files, by the list and entry they settle (<c>objects-and-observances:41</c>),
    /// each file in name order as the Forge applies them. None where the folder is not there.
    /// </summary>
    private Dictionary<string, List<ThingRow>> Rows(IReadOnlyDictionary<string, (int File, JsonObject Record)> records)
    {
        var rows = new Dictionary<string, List<ThingRow>>(StringComparer.Ordinal);
        if (!Directory.Exists(RowsFolder))
        {
            return rows;
        }

        foreach (var path in Directory.GetFiles(RowsFolder, "*.tsv").Order(StringComparer.Ordinal))
        {
            using var reader = ProductStore.HandFormat.Open(path);
            var columns = reader.Columns.Select(c => c.Name).ToList();
            int Column(string name) => columns.IndexOf(name) is var index and >= 0
                ? index
                : throw new InvalidDataException($"{path} has no column '{name}'.");
            var (entry, product, op, at, record, from, strong, why) = (Column("entry"), Column("product"), Column("op"),
                Column("at"), Column("record"), Column("from"), Column("strong"), Column("why"));

            foreach (var row in reader.Rows())
            {
                var named = row[entry] ?? string.Empty;
                var colon = named.LastIndexOf(':');
                if (colon < 1)
                {
                    continue;
                }

                var list = RowLists.GetValueOrDefault(named[..colon], named[..colon]);
                var (reference, word) = Point(row[at] ?? string.Empty);
                var slug = row[record] ?? string.Empty;
                var key = $"{list}:{named[(colon + 1)..]}";
                if (!rows.TryGetValue(key, out var filed))
                {
                    rows[key] = filed = [];
                }

                filed.Add(new ThingRow(Path.GetFileName(path), row[product] ?? string.Empty, row[op] ?? string.Empty,
                    reference, word, Named(slug, records), slug, row[from], row[strong], row[why]?.Trim() ?? string.Empty));
            }
        }

        return rows;
    }

    /// <summary>
    /// A word or verse key of the build's files (<c>11.7:21.33</c>: book, chapter, verse, word) as a verse in words
    /// (<c>1 Kgs 7:21</c>) and the word's number; a key of another shape is given back as it is.
    /// </summary>
    public static (string Reference, int? Word) Point(string at)
    {
        var dot = at.IndexOf('.');
        var colon = at.IndexOf(':');
        if (dot < 1 || colon < dot || !int.TryParse(at[..dot], out var book) || !BookReferences.IsInCanon(book))
        {
            return (at, null);
        }

        var verse = at[(colon + 1)..].Split('.');
        if (!int.TryParse(at[(dot + 1)..colon], out var chapter) || !int.TryParse(verse[0], out var number))
        {
            return (at, null);
        }

        int? word = verse.Length > 1 && int.TryParse(verse[1], out var w) ? w : null;
        return ($"{BookReferences.Abbreviation(book)} {chapter}:{number}", word);
    }

    private static Dictionary<string, (int File, JsonObject Record)> Records(JsonNode[] files)
    {
        var records = new Dictionary<string, (int, JsonObject)>(StringComparer.Ordinal);
        for (var index = 0; index < files.Length; index++)
        {
            foreach (var record in (files[index]["records"]?.AsArray() ?? []).OfType<JsonObject>())
            {
                if (record["slug"]?.GetValue<string>() is { } slug)
                {
                    records[slug] = (index, record);
                }
            }
        }

        return records;
    }

    private static ThingRecordName? Named(string slug, IReadOnlyDictionary<string, (int File, JsonObject Record)> records) =>
        records.TryGetValue(slug, out var found)
            ? new ThingRecordName(
                slug,
                found.Record["name"]?.GetValue<string>() ?? slug,
                found.Record["names"]?["ukr"]?.GetValue<string>(),
                found.Record["kind"]?.GetValue<string>() ?? string.Empty)
            : null;

    private static ThingQuestion Question(
        string list, JsonObject entry, IReadOnlyDictionary<string, (int File, JsonObject Record)> records, IReadOnlyList<ThingRow> rows)
    {
        var slug = entry["record"]?.GetValue<string>() ?? string.Empty;
        var options = (entry["options"]?.AsArray() ?? [])
            .Select(o => o!.GetValue<string>())
            .Select(text => new ThingOption(text, Named(text, records)))
            .ToList();
        return new ThingQuestion(
            KeyOf(entry),
            list,
            slug,
            Named(slug, records),
            entry["strong"]?.GetValue<string>() ?? string.Empty,
            References(entry),
            References(entry).Select(Desk.Addresses.Of).ToList(),
            entry["question"]?.GetValue<string>() ?? string.Empty,
            options,
            entry["decision"]?.DeepClone(),
            rows);
    }
}
