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

/// <param name="Key">The entry's own key where it has one, else a short digest of its record, number and verses, which is stable while the entry is.</param>
/// <param name="List">Which review list it is on, as the list's file is named without its extension.</param>
/// <param name="Record">The record the entry is about, or null where the entry proposes one nobody has written yet.</param>
/// <param name="Addresses">
/// Each reference as the reading API addresses it, for showing its text, in step with
/// <paramref name="References"/>: null where a reference names more than one verse.
/// </param>
/// <param name="Decision">What the owner answered and what that did, or null while it is open.</param>
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
    JsonNode? Decision);

/// <param name="List">The file's name without its extension.</param>
/// <param name="About">What the list says it is.</param>
internal sealed record ThingList(string List, string? About, int Entries, int Open);

internal sealed record ThingQuestionsResponse(IReadOnlyList<ThingList> Lists, IReadOnlyList<ThingQuestion> Entries);

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
///
/// <para>
/// Every list in the review folder of the same shape — <c>{ "about", "entries": [{ "question",
/// "options", ... }] }</c> — is answered here: the verses under a people's ancestor, which the load
/// reads back, and any list written since, whose answers wait for an agent to act on.
/// </para>
/// </summary>
internal sealed class ThingReview(DeskPaths paths, ChangeLog log)
{
    public const string OccurrencesSection = "occurrences";

    public const string RecordsSection = "records";

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

    /// <summary>What an answer did: it is kept in its list, which the next load reads and applies.</summary>
    public const string OnLoad = "load";

    private const string AnswerStampSuffix = ", on the review list";

    /// <summary>The longest note taken, which is a paragraph.</summary>
    public const int LongestNote = 4000;

    /// <summary>The stamp a record review writes, in the form the record files document.</summary>
    public static string RecordStamp(string date) => $"the project owner, {date}";

    /// <summary>The stamp an answer writes on the rule it adds, which says where the ruling was made.</summary>
    public static string AnswerStamp(string date) => $"the project owner, {date}{AnswerStampSuffix}";

    /// <summary>How an option that leaves things as they are begins, in the lists written so far.</summary>
    private static readonly string[] LeftAlone = ["none", "leave", "keep ", "stay a place", "show them as printed"];

    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Every list of questions in the review folder: the objects' first, the ancestors' next, then the rest by name.</summary>
    private IEnumerable<string> QuestionLists =>
        (Directory.Exists(paths.Review) ? Directory.EnumerateFiles(paths.Review, "*.json") : [])
        .Where(IsQuestionList)
        .OrderBy(path => Path.GetFileName(path) switch { QuestionsFile => 0, AncestorsFile => 1, _ => 2 })
        .ThenBy(path => path, StringComparer.Ordinal);

    /// <summary>
    /// Whether a file in the review folder is a list of questions, remembered while the file is
    /// unchanged: the folder also holds records of past decisions, some of them large, and every page
    /// and count asks.
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

    private string[] RecordFiles => [Path.Combine(paths.Records, ObjectsFile), Path.Combine(paths.Records, ObservancesFile)];

    public bool Exists => QuestionLists.Any();

    public ThingQuestionsResponse ReadQuestions()
    {
        var records = Records(RecordFiles.Select(JsonFiles.Read).ToArray());
        var lists = new List<ThingList>();
        var entries = new List<ThingQuestion>();
        foreach (var path in QuestionLists)
        {
            var node = JsonFiles.Read(path);
            var listed = (node["entries"]?.AsArray() ?? []).OfType<JsonObject>().ToList();
            lists.Add(new ThingList(ListOf(path), node["about"]?.GetValue<string>(), listed.Count,
                listed.Count(e => e["decision"] is null)));
            entries.AddRange(listed.Select(entry => Question(ListOf(path), entry, records)));
        }

        return new ThingQuestionsResponse(lists, entries);
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
        ThingQuestion answered;
        JsonNode? before;
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

            var name = ListOf(list!);
            before = entry["decision"]?.DeepClone();
            var files = RecordFiles.Select(JsonFiles.Read).ToArray();
            var records = Records(files);
            var strong = entry["strong"]?.GetValue<string>() ?? string.Empty;
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
                var effect = request.Answer is null ? Nothing : EffectOf(name, request.Answer, records);
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
            answered = Question(name, entry, Records(files));
        }
        finally
        {
            _gate.Release();
        }

        var after = answered.Decision;
        await log.Append(OccurrencesSection, "answer", TargetOf(answered), Said(before), Said(after), request.Note,
            NeedsOf(after?["applied"]?.GetValue<string>(), before?["applied"]?.GetValue<string>()),
            $"{answered.Record?.LocalName ?? answered.Record?.Name ?? answered.RecordSlug} · {string.Join(", ", answered.References)}".Trim(' ', '·'));
        return answered;
    }

    private static JsonObject? Said(JsonNode? decision) =>
        decision is null ? null : new JsonObject { ["answer"] = decision["answer"]?.DeepClone(), ["note"] = decision["note"]?.DeepClone() };

    /// <summary>
    /// What has to happen before an answer shows: the next load where it wrote a rule or the load
    /// reads it, an agent where it asks for a change by hand, and — for an answer taken back — whatever
    /// the answer it replaced needed.
    /// </summary>
    private static string? NeedsOf(string? applied, string? was) =>
        (applied, was) switch
        {
            (Written or OnLoad, _) or (_, Written or OnLoad) => "load",
            (ByHand, _) or (_, ByHand) => "agent",
            _ => null,
        };

    /// <summary>An entry as the change log names it: its list, its record or key, its number and its verses.</summary>
    private static string TargetOf(ThingQuestion question) =>
        string.Join(" ", new[]
        {
            question.List,
            question.RecordSlug.Length > 0 ? question.RecordSlug : question.Key,
            question.Strong,
            string.Join(", ", question.References),
        }.Where(part => part.Length > 0));

    /// <summary>Marks a record reviewed, or takes the review back. Null where no record has the slug or the scope is not one of the three.</summary>
    public async Task<ThingRecordEntry?> Review(string slug, ThingRecordRequest request)
    {
        if (request.Scope is not (null or All or RecordOnly))
        {
            return null;
        }

        await _gate.WaitAsync();
        ThingRecordEntry reviewed;
        string? before;
        try
        {
            var files = RecordFiles.Select(JsonFiles.Read).ToArray();
            if (!Records(files).TryGetValue(slug, out var found))
            {
                return null;
            }

            var record = found.Record;
            before = Scope(record);
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
            reviewed = new ThingRecordEntry(
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

        await log.Append(RecordsSection, "review",
            $"{(reviewed.File == "objects" ? "object" : "observance")}/{slug}",
            before is null ? null : JsonValue.Create(before), reviewed.Scope is null ? null : JsonValue.Create(reviewed.Scope),
            null, "load", reviewed.Record["names"]?["ukr"]?.GetValue<string>() ?? reviewed.Record["name"]?.GetValue<string>());
        return reviewed;
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
            ? All
            : RecordOnly;
    }

    /// <summary>
    /// What answering an entry of <paramref name="list"/> with <paramref name="answer"/> does. The
    /// objects' list writes a record's rule; the ancestors' list is read back by the load, whatever
    /// the answer; any other list is read by whoever acts on it, so only an answer that leaves things
    /// as they are does nothing.
    /// </summary>
    private static string EffectOf(string list, string answer, IReadOnlyDictionary<string, (int File, JsonObject Record)> records) =>
        list == ListOf(AncestorsFile) ? OnLoad
        : list == ListOf(QuestionsFile) && records.ContainsKey(answer) ? Written
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

    private static ThingQuestion Question(
        string list, JsonObject entry, IReadOnlyDictionary<string, (int File, JsonObject Record)> records)
    {
        var slug = entry["record"]?.GetValue<string>() ?? string.Empty;
        var options = (entry["options"]?.AsArray() ?? [])
            .Select(o => o!.GetValue<string>())
            .Select(text => new ThingOption(text, Named(text, records), EffectOf(list, text, records)))
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
            entry["decision"]?.DeepClone());
    }
}
