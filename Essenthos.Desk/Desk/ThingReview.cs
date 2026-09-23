using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Essenthos.Core.Desk;

/// <param name="Answer">One of the entry's options, or null to take the answer back.</param>
internal sealed record ThingAnswerRequest(string? Answer, string? Note);

/// <param name="Scope"><c>all</c> for the record and every rule of it, <c>record</c> for its description, names and relations alone, or null to take the review back.</param>
internal sealed record ThingRecordRequest(string? Scope);

/// <summary>A record an option names, so the page can say what answering it would link the verses to.</summary>
internal sealed record ThingRecordName(string Slug, string Name, string? LocalName, string Kind);

internal sealed record ThingOption(string Text, ThingRecordName? Record, string Effect);

/// <param name="Key">A short digest of the entry's record, number and verses, which is stable while the entry is.</param>
/// <param name="Record">The record the entry is about, or null where the entry proposes one nobody has written yet.</param>
/// <param name="Addresses">
/// Each reference as the reading API addresses it, for showing its text, in step with
/// <paramref name="References"/>: null where a reference names more than one verse.
/// </param>
/// <param name="Decision">What the owner answered and what that did, or null while it is open.</param>
internal sealed record ThingQuestion(
    string Key,
    string RecordSlug,
    ThingRecordName? Record,
    string Strong,
    IReadOnlyList<string> References,
    IReadOnlyList<string?> Addresses,
    string Question,
    IReadOnlyList<ThingOption> Options,
    JsonNode? Decision);

internal sealed record ThingQuestionsResponse(string? About, IReadOnlyList<ThingQuestion> Entries);

/// <param name="File"><c>objects</c> or <c>observances</c>: which of the two record files holds it.</param>
/// <param name="Scope">How far the owner has reviewed it: <c>all</c>, <c>record</c>, or null.</param>
/// <param name="Record">The record as its file holds it.</param>
internal sealed record ThingRecordEntry(string File, string? Scope, int Rules, int ReviewedRules, JsonNode Record);

internal sealed record ThingRecordsResponse(IReadOnlyList<ThingRecordEntry> Records);

/// <summary>
/// The objects and appointed times: the occurrences a reading could not settle, which the owner
/// answers, and the records themselves, which a model wrote and the owner reviews.
///
/// <para>
/// **An answer is the change the loader reads.** Answering an open occurrence with a record adds its
/// verses to that record's occurrence rules in <c>ObjectRecords.json</c> or
/// <c>ObservanceRecords.json</c>, in a rule marked reviewed, which the loader writes as the owner's
/// ruling and not as a reading. Answering that it is none of them leaves the verses unlinked, which
/// they already are. An option that asks for a record nobody has written yet — <em>a record of its
/// own</em> — is recorded as the owner's answer and waits for somebody to write the record. Every
/// answer is kept in the review list beside its question, so taking one back undoes exactly what it
/// did.
/// </para>
///
/// <para>
/// **A review is a stamp in the file.** The loader treats a record or a rule carrying
/// <c>reviewed</c> as the owner's, whatever the stamp says, so the stamps are chosen to tell the two
/// sources apart: a record review writes <see cref="RecordStamp"/>, an answer writes
/// <see cref="AnswerStamp"/>, and taking either back removes only its own.
/// </para>
///
/// <para>
/// The record files are embedded in the loader, so a change reaches the corpus on the next build and
/// load of Essenthos.Forge, and not before.
/// </para>
/// </summary>
internal sealed class ThingReview(DeskPaths paths)
{
    public const string QuestionsFile = "objects-and-observances.json";

    /// <summary>
    /// The verses a dataset files under a man a people is named after that the load could not settle,
    /// which the load keeps beside this list in the same shape and reads the answers back from.
    /// </summary>
    public const string AncestorsFile = "eponym-verses.json";

    public const string ObjectsFile = "ObjectRecords.json";

    public const string ObservancesFile = "ObservanceRecords.json";

    public const string All = "all";

    public const string RecordOnly = "record";

    /// <summary>What an answer did: its verses were written into a record's rules.</summary>
    public const string Written = "written";

    /// <summary>What an answer did: nothing, because the verses stay as they are.</summary>
    public const string Nothing = "nothing";

    /// <summary>What an answer did: nothing yet, because it asks for a record somebody has to write.</summary>
    public const string ByHand = "by-hand";

    private const string AnswerStampSuffix = ", on the review list";

    /// <summary>The longest note taken, which is a paragraph.</summary>
    public const int LongestNote = 4000;

    /// <summary>The stamp a record review writes, in the form the record files document.</summary>
    public static string RecordStamp(string date) => $"the project owner, {date}";

    /// <summary>The stamp an answer writes on the rule it adds, which says where the ruling was made.</summary>
    public static string AnswerStamp(string date) => $"the project owner, {date}{AnswerStampSuffix}";

    private static readonly string[] LeftAlone = ["none", "leave as it is", "stay a place"];

    private readonly SemaphoreSlim _gate = new(1, 1);

    private string Questions => Path.Combine(paths.Review, QuestionsFile);

    /// <summary>Every list of open occurrences on this disk, the objects' first.</summary>
    private IEnumerable<string> QuestionLists =>
        new[] { QuestionsFile, AncestorsFile }.Select(file => Path.Combine(paths.Review, file)).Where(File.Exists);

    private string[] RecordFiles => [Path.Combine(paths.Records, ObjectsFile), Path.Combine(paths.Records, ObservancesFile)];

    public bool Exists => File.Exists(Questions);

    public ThingQuestionsResponse ReadQuestions()
    {
        var records = Records(RecordFiles.Select(JsonFiles.Read).ToArray());
        var entries = QuestionLists
            .SelectMany(list => (JsonFiles.Read(list)["entries"]?.AsArray() ?? []).OfType<JsonObject>())
            .Select(entry => Question(entry, records))
            .ToList();
        return new ThingQuestionsResponse(JsonFiles.Read(Questions)["about"]?.GetValue<string>(), entries);
    }

    public int Unanswered() =>
        QuestionLists.Sum(list =>
            (JsonFiles.Read(list)["entries"]?.AsArray() ?? []).OfType<JsonObject>().Count(e => e["decision"] is null));

    public ThingRecordsResponse ReadRecords()
    {
        var files = RecordFiles.Select(JsonFiles.Read).ToArray();
        var entries = new List<ThingRecordEntry>();
        for (var index = 0; index < files.Length; index++)
        {
            foreach (var record in (files[index]["records"]?.AsArray() ?? []).OfType<JsonObject>())
            {
                var rules = (record["occurrences"]?.AsArray() ?? []).OfType<JsonObject>().ToList();
                entries.Add(new ThingRecordEntry(
                    index == 0 ? "objects" : "observances",
                    Scope(record),
                    rules.Count,
                    rules.Count(rule => rule["reviewed"] is not null),
                    record.DeepClone()));
            }
        }

        return new ThingRecordsResponse(entries);
    }

    public int Unreviewed() => ReadRecords().Records.Count(r => r.Scope is null);

    /// <summary>Answers one open occurrence, or takes its answer back. Null where the entry or the answer is not one the list holds.</summary>
    public async Task<ThingQuestion?> Answer(string key, ThingAnswerRequest request)
    {
        if ((request.Note?.Length ?? 0) > LongestNote)
        {
            return null;
        }

        await _gate.WaitAsync();
        try
        {
            var (list, questions, entry) = QuestionLists
                .Select(path => (Path: path, Node: JsonFiles.Read(path)))
                .Select(read => (read.Path, read.Node,
                    Entry: (read.Node["entries"]?.AsArray() ?? []).OfType<JsonObject>().FirstOrDefault(e => KeyOf(e) == key)))
                .FirstOrDefault(found => found.Entry is not null);
            var options = entry?["options"]?.AsArray().Select(o => o?.GetValue<string>()).ToList() ?? [];
            if (entry is null || (request.Answer is not null && !options.Contains(request.Answer)))
            {
                return null;
            }

            var files = RecordFiles.Select(JsonFiles.Read).ToArray();
            var records = Records(files);
            var strong = entry["strong"]!.GetValue<string>();
            var references = References(entry);
            var changed = new bool[files.Length];

            if (entry["decision"]?["answer"]?.GetValue<string>() is { } previous && records.TryGetValue(previous, out var was))
            {
                changed[was.File] |= Unwrite(was.Record, strong, references);
            }

            var note = request.Note?.Trim() ?? string.Empty;
            if (request.Answer is null && note.Length == 0)
            {
                entry.Remove("decision");
            }
            else
            {
                var effect = request.Answer is null ? Nothing : EffectOf(request.Answer, records);
                if (effect == Written && records.TryGetValue(request.Answer!, out var chosen))
                {
                    changed[chosen.File] |= Write(chosen.Record, strong, references, AnswerStamp(JsonFiles.Today()));
                }

                entry["decision"] = new JsonObject
                {
                    ["answer"] = request.Answer,
                    ["note"] = note,
                    ["decidedAt"] = JsonFiles.Now(),
                    ["applied"] = effect,
                };
            }

            for (var index = 0; index < files.Length; index++)
            {
                if (changed[index])
                {
                    JsonFiles.Write(RecordFiles[index], files[index]);
                }
            }

            JsonFiles.Write(list!, questions!);
            return Question(entry, Records(files));
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Marks a record reviewed, or takes the review back. Null where no record has the slug or the scope is not one of the three.</summary>
    public async Task<ThingRecordEntry?> Review(string slug, ThingRecordRequest request)
    {
        if (request.Scope is not (null or All or RecordOnly))
        {
            return null;
        }

        await _gate.WaitAsync();
        try
        {
            var files = RecordFiles.Select(JsonFiles.Read).ToArray();
            if (!Records(files).TryGetValue(slug, out var found))
            {
                return null;
            }

            var record = found.Record;
            var rules = (record["occurrences"]?.AsArray() ?? []).OfType<JsonObject>().ToList();
            if (record["reviewed"]?.GetValue<string>() is { } stamp)
            {
                record.Remove("reviewed");
                foreach (var rule in rules.Where(rule => rule["reviewed"]?.GetValue<string>() == stamp))
                {
                    rule.Remove("reviewed");
                }
            }

            if (request.Scope is not null)
            {
                var now = RecordStamp(JsonFiles.Today());
                record["reviewed"] = now;
                if (request.Scope == All)
                {
                    foreach (var rule in rules.Where(rule => rule["reviewed"] is null))
                    {
                        rule["reviewed"] = now;
                    }
                }
            }

            JsonFiles.Write(RecordFiles[found.File], files[found.File]);
            return new ThingRecordEntry(
                found.File == 0 ? "objects" : "observances",
                Scope(record),
                rules.Count,
                rules.Count(rule => rule["reviewed"] is not null),
                record.DeepClone());
        }
        finally
        {
            _gate.Release();
        }
    }

    public static string KeyOf(JsonNode entry)
    {
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
            ? All
            : RecordOnly;
    }

    private static string EffectOf(string answer, IReadOnlyDictionary<string, (int File, JsonObject Record)> records) =>
        records.ContainsKey(answer) ? Written
        : LeftAlone.Any(alone => answer.StartsWith(alone, StringComparison.Ordinal)) ? Nothing
        : ByHand;

    /// <summary>Adds the verses to the record's answered rule for the number, making the rule if there is none.</summary>
    private static bool Write(JsonObject record, string strong, IReadOnlyList<string> references, string stamp)
    {
        if (record["occurrences"] is not JsonArray rules)
        {
            rules = [];
            record["occurrences"] = rules;
        }

        var rule = rules.OfType<JsonObject>().FirstOrDefault(r => IsAnswered(r, strong));
        if (rule is null)
        {
            rules.Add(new JsonObject
            {
                ["strong"] = strong,
                ["only"] = new JsonArray(references.Select(r => (JsonNode?)r).ToArray()),
                ["reviewed"] = stamp,
            });
            return true;
        }

        var only = rule["only"]?.AsArray() ?? [];
        rule["only"] = only;
        var have = only.Select(r => r!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
        var added = false;
        foreach (var reference in references.Where(r => !have.Contains(r)))
        {
            only.Add(reference);
            added = true;
        }

        return added;
    }

    /// <summary>Takes the verses back out of the record's answered rule, and the rule with them once it holds none.</summary>
    private static bool Unwrite(JsonObject record, string strong, IReadOnlyList<string> references)
    {
        if (record["occurrences"] is not JsonArray rules)
        {
            return false;
        }

        var changed = false;
        foreach (var rule in rules.OfType<JsonObject>().Where(r => IsAnswered(r, strong)).ToList())
        {
            var only = rule["only"]?.AsArray() ?? [];
            foreach (var item in only.Where(r => references.Contains(r!.GetValue<string>())).ToList())
            {
                only.Remove(item);
                changed = true;
            }

            if (only.Count == 0)
            {
                rules.Remove(rule);
            }
        }

        return changed;
    }

    private static bool IsAnswered(JsonObject rule, string strong) =>
        rule["strong"]?.GetValue<string>() == strong
        && rule["reviewed"]?.GetValue<string>()?.EndsWith(AnswerStampSuffix, StringComparison.Ordinal) == true;

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

    private static ThingQuestion Question(JsonObject entry, IReadOnlyDictionary<string, (int File, JsonObject Record)> records)
    {
        var slug = entry["record"]?.GetValue<string>() ?? string.Empty;
        var options = (entry["options"]?.AsArray() ?? [])
            .Select(o => o!.GetValue<string>())
            .Select(text => new ThingOption(text, Named(text, records), EffectOf(text, records)))
            .ToList();
        return new ThingQuestion(
            KeyOf(entry),
            slug,
            Named(slug, records),
            entry["strong"]?.GetValue<string>() ?? string.Empty,
            References(entry),
            References(entry).Select(Desk.Addresses.Of).ToList(),
            entry["question"]?.GetValue<string>() ?? string.Empty,
            options,
            entry["decision"]?.DeepClone());
    }
}
