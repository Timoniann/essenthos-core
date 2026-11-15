using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace Essenthos.Core.Loading.Links.Evidentia;

/// <param name="Folder">The run's folder under the ledger, named by its texts and when it started.</param>
/// <param name="Verdicts">The verdicts written, one line each.</param>
internal sealed record EvidentiaExportOutcome(int RunId, string Folder, int Verdicts, int Books)
{
    public override string ToString() =>
        $"EVIDENTIA run {RunId}: {Verdicts:N0} verdicts written to the ledger at {Folder}, in {Books} book files";
}

/// <summary>
/// The verdicts given on EVIDENTIA's proposals, kept as files beside the other decisions of this
/// project so that a rebuild of the corpus gives them back.
///
/// <para>
/// **A verdict is a person's decision and the database is a build artefact.** A run, its decisions
/// and the reviews on them live in tables a cold load does not write, and every word id they hold is
/// renumbered by the next release. So each run with any verdict is written out under
/// <c>Resources/Essenthos/evidentia/</c>, one folder per run: <c>run.json</c> with what identifies the run
/// and who reviewed, and one tab-separated file per canonical book with a line per verdict. A word is
/// named by its text, its canonical address, which of that address's words it is, and its surface
/// as it then read, so a word that no longer reads the same is reported rather than guessed at.
/// </para>
///
/// <para>
/// **The build reads the ledger, not the database.** <c>evidentia-pairs</c> makes each pair's <c>evidentia</c> method file from
/// these folders and the texts' words and frames (<see cref="Mapping.EvidentiaFiles"/>); nothing writes a verdict into a corpus
/// database any more (PRB-1597).
/// </para>
///
/// </summary>
internal sealed class EvidentiaLedger(AppDbContext db)
{
    /// <summary>Under the corpus sources, in this project's own folder.</summary>
    public static readonly string DefaultFolder = Path.Combine("Essenthos", "evidentia");

    public const string RunFile = "run.json";

    private const string BookPattern = "*.tsv";

    /// <summary>A run folder's book files, <c>01.tsv</c> to <c>87.tsv</c>, in order; not <see cref="HeadsFile"/>.</summary>
    internal static IEnumerable<string> BookFiles(string folder) =>
        Directory.EnumerateFiles(folder, BookPattern)
            .Where(file => int.TryParse(Path.GetFileNameWithoutExtension(file), NumberStyles.None, CultureInfo.InvariantCulture, out _))
            .Order(StringComparer.Ordinal);

    /// <summary>
    /// The head each attached word of the run goes with, where the corpus knows it: the word its decision anchored it
    /// to on the same counterpart, or the one its link marks. A file-built mapping cannot find it again, because the
    /// head's own decision may have no verdict.
    /// </summary>
    public const string HeadsFile = "heads.tsv";

    private static readonly string[] HeadColumns = ["at", "word", "surface", "head_at", "head", "head_surface"];

    private const char Tab = '\t';

    private const char NewLine = '\n';

    /// <summary>The digits a confidence is written with, which is all of it a link keeps.</summary>
    private const int ConfidenceDigits = 4;

    private const string About =
        "The verdicts given on one EVIDENTIA run, kept so that a rebuild of the corpus gives them back. " +
        "One file per canonical book, a line per verdict; a word is its canonical address, which of the " +
        "address's words it is, and its surface as it then read. Written by the Forge whenever a verdict " +
        "is recorded, and replayed by every load. Do not edit by hand: record verdicts with the evidentia verbs.";

    private static readonly string[] Columns =
    [
        "at", "word", "surface", "target_at", "target", "target_surface", "absence", "rule", "tier",
        "confidence", "verdict", "review", "corrected_at", "corrected", "corrected_surface", "rationale",
    ];

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private const string Addressed =
        """
        SELECT x.id, x.book, x.chapter, x.verse, x.nth, x.surface
        FROM (
            SELECT w.id, r.canonical_book AS book, r.canonical_chapter AS chapter, r.canonical_verse AS verse,
                   row_number() OVER (PARTITION BY r.canonical_book, r.canonical_chapter, r.canonical_verse
                                      ORDER BY v.chapter_number, v.number, w.position, b.canonical_ordinal, v.label COLLATE "C")::int AS nth,
                   w.text AS surface
            FROM word w
            JOIN verse v ON v.id = w.verse_id
            JOIN book b ON b.id = v.book_id
            JOIN verse_reference r ON r.verse_id = w.verse_id AND r.is_primary
            WHERE w.text_id = @text AND r.canonical_book = ANY(@books)) x
        """;

    private const string DecisionImport =
        """
        COPY evidentia_decision (
            run_id, source_word_id, canonical_book, canonical_chapter, canonical_verse, content,
            target_word_id, kind, tier, rationale, confidence, candidates, absence)
        FROM STDIN (FORMAT BINARY)
        """;

    private const string ReviewImport =
        """
        COPY evidentia_review (decision_id, verdict, examined, corrected_target_word_id, reviewer, reviewed_at, note)
        FROM STDIN (FORMAT BINARY)
        """;

    public static string Folder(string resources) => Path.Combine(resources, DefaultFolder);

    /// <summary>The folder a run's verdicts are kept in, named by what identifies the run and nothing renumbered.</summary>
    public static string RunFolder(string from, string to, DateTimeOffset startedAt) =>
        $"{from}-{to}-{startedAt.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'.'ffffff", CultureInfo.InvariantCulture)}";

    /// <summary>Every run with a verdict, written out whole; the folder of each is replaced, so a verdict taken back leaves it.</summary>
    public async Task<IReadOnlyList<EvidentiaExportOutcome>> ExportAll(string resources, CancellationToken cancellationToken = default)
    {
        var runs = await db.EvidentiaRuns.AsNoTracking()
            .Where(run => run.Decisions.Any(decision => decision.Review != null))
            .OrderBy(run => run.Id)
            .Select(run => run.Id)
            .ToListAsync(cancellationToken);
        var outcomes = new List<EvidentiaExportOutcome>();
        foreach (var run in runs)
        {
            outcomes.Add(await Export(run, resources, cancellationToken));
        }

        return outcomes;
    }

    public async Task<EvidentiaExportOutcome> Export(int runId, string resources, CancellationToken cancellationToken = default)
    {
        var run = await db.EvidentiaRuns.AsNoTracking()
            .Include(row => row.FromText)
            .Include(row => row.ToText)
            .SingleOrDefaultAsync(row => row.Id == runId, cancellationToken)
            ?? throw new InvalidOperationException($"There is no EVIDENTIA run {runId}. List the stored runs with evidentia-runs.");

        var reviewed = await db.EvidentiaReviews.AsNoTracking()
            .Where(review => review.Decision!.RunId == runId)
            .Select(review => new Reviewed(
                review.Decision!.SourceWordId,
                review.Decision.TargetWordId,
                review.Decision.CanonicalBook,
                review.Decision.CanonicalChapter,
                review.Decision.CanonicalVerse,
                review.Decision.Absence,
                review.Decision.Kind,
                review.Decision.Tier,
                review.Decision.Confidence,
                review.Decision.Rationale,
                review.Verdict,
                review.Examined,
                review.Reviewer,
                review.ReviewedAt,
                review.Note,
                review.CorrectedTargetWordId))
            .ToListAsync(cancellationToken);

        var anchored = await Heads(runId, cancellationToken);
        var fromWords = await AddressesOf(run.FromTextId,
            [.. reviewed.Select(row => row.Source), .. anchored.Select(head => (long?)head.Head)], cancellationToken);
        var toWords = await AddressesOf(run.ToTextId, [.. reviewed.SelectMany(row => new[] { row.Target, row.Corrected })], cancellationToken);
        var sessions = reviewed
            .Select(row => new Session(row.Reviewer, row.ReviewedAt, row.Examined, row.Note))
            .Distinct()
            .OrderBy(session => session.ReviewedAt)
            .ThenBy(session => session.Reviewer, StringComparer.Ordinal)
            .ToList();
        var sessionIndex = sessions.Select((session, index) => (session, index)).ToDictionary(pair => pair.session, pair => pair.index);

        var lines = new List<(int Book, Address At, int Word, int Target, string Text)>(reviewed.Count);
        foreach (var row in reviewed)
        {
            var source = row.Source is { } sourceId ? Of(fromWords, sourceId, run.FromText!.Slug) : null;
            var target = row.Target is { } targetId ? Of(toWords, targetId, run.ToText!.Slug) : null;
            var corrected = row.Corrected is { } correctedId ? Of(toWords, correctedId, run.ToText!.Slug) : null;
            var at = source?.At ?? target?.At
                ?? throw new InvalidDataException($"A verdict of EVIDENTIA run {runId} names no word on either side. Nothing can address it.");
            string[] fields =
            [
                $"{at.Chapter}:{at.Verse}",
                source?.Nth.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                source?.Surface ?? string.Empty,
                target is null || target.At == at ? string.Empty : target.At.ToString(),
                target?.Nth.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                target?.Surface ?? string.Empty,
                row.Absence is { } absence ? EnumSpelling.Of(absence) : string.Empty,
                row.Kind ?? string.Empty,
                row.Tier ?? string.Empty,
                row.Confidence is { } confidence
                    ? Math.Round(confidence, ConfidenceDigits).ToString("0.####", CultureInfo.InvariantCulture)
                    : string.Empty,
                EnumSpelling.Of(row.Verdict),
                sessionIndex[new Session(row.Reviewer, row.ReviewedAt, row.Examined, row.Note)].ToString(CultureInfo.InvariantCulture),
                corrected is null || corrected.At == at ? string.Empty : corrected.At.ToString(),
                corrected?.Nth.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                corrected?.Surface ?? string.Empty,
                row.Rationale ?? string.Empty,
            ];
            lines.Add((at.Book, at, source?.Nth ?? 0, target?.Nth ?? 0, string.Join(Tab, fields.Select(Checked))));
        }

        var folder = Path.Combine(Folder(resources), RunFolder(run.FromText!.Slug, run.ToText!.Slug, run.StartedAt));
        Directory.CreateDirectory(folder);
        foreach (var stale in BookFiles(folder))
        {
            File.Delete(stale);
        }

        var byBook = lines.GroupBy(line => line.Book).OrderBy(group => group.Key).ToList();
        foreach (var book in byBook)
        {
            var text = new StringBuilder(string.Join(Tab, Columns)).Append('\n');
            foreach (var line in book.OrderBy(line => line.At.Chapter).ThenBy(line => line.At.Verse)
                         .ThenBy(line => line.Word).ThenBy(line => line.Target).ThenBy(line => line.Text, StringComparer.Ordinal))
            {
                text.Append(line.Text).Append('\n');
            }

            await Write(Path.Combine(folder, BookFile(book.Key)), text.ToString(), cancellationToken);
        }

        var headed = anchored
            .Select(pair => (Word: Of(fromWords, pair.Word, run.FromText.Slug), Head: Of(fromWords, pair.Head, run.FromText.Slug)))
            .Distinct()
            .OrderBy(pair => pair.Word.At.Book).ThenBy(pair => pair.Word.At.Chapter).ThenBy(pair => pair.Word.At.Verse)
            .ThenBy(pair => pair.Word.Nth).ThenBy(pair => pair.Head.At.Chapter).ThenBy(pair => pair.Head.At.Verse).ThenBy(pair => pair.Head.Nth)
            .ToList();
        var heads = new StringBuilder(string.Join(Tab, HeadColumns)).Append(NewLine);
        foreach (var (word, head) in headed)
        {
            heads.AppendJoin(Tab, word.At, word.Nth, Checked(word.Surface), head.At == word.At ? string.Empty : head.At.ToString(),
                head.Nth, Checked(head.Surface)).Append(NewLine);
        }

        await Write(Path.Combine(folder, HeadsFile), heads.ToString(), cancellationToken);

        var record = new JsonObject
        {
            ["about"] = About,
            ["run"] = run.Id,
            ["from"] = run.FromText.Slug,
            ["to"] = run.ToText.Slug,
            ["startedAt"] = Stamp(run.StartedAt),
            ["finishedAt"] = run.FinishedAt is { } finished ? Stamp(finished) : null,
            ["ruleVersion"] = run.RuleVersion,
            ["scope"] = JsonNode.Parse(run.Scope.RootElement.GetRawText()),
            ["configuration"] = JsonNode.Parse(run.Configuration.RootElement.GetRawText()),
            ["reviews"] = new JsonArray([.. sessions.Select(session => (JsonNode)new JsonObject
            {
                ["reviewer"] = session.Reviewer,
                ["reviewedAt"] = Stamp(session.ReviewedAt),
                ["examined"] = session.Examined,
                ["note"] = session.Note,
            })]),
            ["verdicts"] = Counted(reviewed.Select(row => row.Verdict)),
        };
        await Write(Path.Combine(folder, RunFile), record.ToJsonString(Json) + "\n", cancellationToken);
        return new EvidentiaExportOutcome(runId, folder, lines.Count, byBook.Count);
    }

    /// <summary>The approved attached words of a run and the head each goes with, where the corpus knows one.</summary>
    private async Task<List<(long Word, long Head)>> Heads(int runId, CancellationToken cancellationToken)
    {
        var heads = new List<(long, long)>();
        await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var command = new NpgsqlCommand(Headed, (NpgsqlConnection)db.Database.GetDbConnection());
            command.Parameters.AddWithValue("run", runId);
            command.Parameters.AddWithValue("kind", AttachedWords.DecisionKind);
            command.Parameters.AddWithValue("approved", EnumSpelling.Of(EvidentiaVerdict.Approved));
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                heads.Add((reader.GetInt64(0), reader.GetInt64(1)));
            }
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }

        return heads;
    }

    /// <summary>The heads an approved attached word's decision anchors on its own counterpart, or else its link marks.</summary>
    private const string Headed =
        """
        SELECT x.word, x.head
        FROM (
            SELECT d.source_word_id AS word,
                   coalesce(CASE WHEN d.anchor_target_word_id = d.target_word_id THEN d.anchor_source_word_id END, lw.head_word_id) AS head
            FROM evidentia_review r
            JOIN evidentia_decision d ON d.id = r.decision_id
            LEFT JOIN link_word lw ON lw.link_id = r.link_id AND lw.word_id = d.source_word_id AND lw.head_word_id IS NOT NULL
            WHERE d.run_id = @run AND d.kind = @kind AND r.verdict = @approved AND d.source_word_id IS NOT NULL) x
        WHERE x.head IS NOT NULL
        """;

    /// <summary>
    /// The runs whose verdicts in the database differ from what the ledger holds for them, each as a
    /// sentence saying what to run. Empty when every verdict is in a file.
    /// </summary>
    public static async Task<IReadOnlyList<string>> Unrecorded(AppDbContext db, string resources, CancellationToken cancellationToken = default)
    {
        var counts = await db.EvidentiaReviews.AsNoTracking()
            .GroupBy(review => new { review.Decision!.RunId, review.Verdict })
            .Select(group => new { group.Key.RunId, group.Key.Verdict, Count = group.Count() })
            .ToListAsync(cancellationToken);
        var reviewedRuns = counts.Select(count => count.RunId).Distinct().ToList();
        var runs = await db.EvidentiaRuns.AsNoTracking()
            .Where(run => reviewedRuns.Contains(run.Id))
            .Select(run => new { run.Id, From = run.FromText!.Slug, To = run.ToText!.Slug, run.StartedAt })
            .ToListAsync(cancellationToken);

        var problems = new List<string>();
        foreach (var run in runs.OrderBy(run => run.Id))
        {
            var held = counts.Where(count => count.RunId == run.Id)
                .ToDictionary(count => EnumSpelling.Of(count.Verdict), count => count.Count, StringComparer.Ordinal);
            var file = Path.Combine(Folder(resources), RunFolder(run.From, run.To, run.StartedAt), RunFile);
            var recorded = File.Exists(file)
                ? (JsonNode.Parse(await File.ReadAllTextAsync(file, cancellationToken))?["verdicts"] as JsonObject)?
                    .ToDictionary(pair => pair.Key, pair => pair.Value?.GetValue<int>() ?? 0, StringComparer.Ordinal)
                : null;
            if (recorded is null || !Same(held, recorded))
            {
                problems.Add($"run {run.Id} ({run.From} → {run.To}) holds {Said(held)} and the ledger "
                             + (recorded is null ? "holds nothing for it" : $"holds {Said(recorded)}")
                             + $"; record them with evidentia-export {run.Id}");
            }
        }

        return problems;
    }

    /// <summary>
    /// Every word of one text in these canonical books, with its address. The ordinal counts the
    /// words standing at one canonical address, in the text's own order, so an address two of the
    /// text's verses share still names each word once.
    /// </summary>
    private async Task<Dictionary<long, WordAddress>> Addresses(int textId, IReadOnlyCollection<int> books, CancellationToken cancellationToken)
    {
        var addresses = new Dictionary<long, WordAddress>();
        await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var command = new NpgsqlCommand(Addressed, (NpgsqlConnection)db.Database.GetDbConnection());
            command.Transaction = (NpgsqlTransaction?)db.Database.CurrentTransaction?.GetDbTransaction();
            command.Parameters.AddWithValue("text", textId);
            command.Parameters.AddWithValue("books", books.ToArray());
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                addresses[reader.GetInt64(0)] = new WordAddress(
                    new Address(reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3)), reader.GetInt32(4), reader.GetString(5));
            }
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }

        return addresses;
    }

    /// <summary>The addresses of every word in the canonical books these words stand in.</summary>
    private async Task<Dictionary<long, WordAddress>> AddressesOf(int textId, IReadOnlyCollection<long?> words, CancellationToken cancellationToken)
    {
        var named = words.OfType<long>().Distinct().ToList();
        var books = new List<int>();
        foreach (var chunk in named.Chunk(50_000))
        {
            books.AddRange(await db.Words.AsNoTracking()
                .Where(word => chunk.Contains(word.Id))
                .SelectMany(word => word.Verse!.References.Where(reference => reference.IsPrimary))
                .Select(reference => reference.CanonicalBook)
                .Distinct()
                .ToListAsync(cancellationToken));
        }

        return await Addresses(textId, books.Distinct().ToList(), cancellationToken);
    }

    private static WordAddress Of(Dictionary<long, WordAddress> addresses, long word, string text) =>
        addresses.TryGetValue(word, out var address)
            ? address
            : throw new InvalidDataException(
                $"Word {word} of {text} has no canonical address, so a verdict about it cannot be kept outside the corpus. " +
                "Place its verse in the frame before recording a verdict on it.");

    private static string Checked(string field) =>
        field.Contains(Tab) || field.Contains('\n') || field.Contains('\r')
            ? throw new InvalidDataException($"'{field}' holds a tab or a line break, which a line of the ledger cannot. Nothing was written.")
            : field;

    private static string BookFile(int book) => $"{book:00}.tsv";

    private static string Stamp(DateTimeOffset moment) => moment.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static JsonObject Counted(IEnumerable<EvidentiaVerdict> verdicts) =>
        new([.. verdicts.GroupBy(EnumSpelling.Of).OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => KeyValuePair.Create(group.Key, (JsonNode?)group.Count()))]);

    private static bool Same(Dictionary<string, int> held, Dictionary<string, int> recorded) =>
        held.Count == recorded.Count && held.All(pair => recorded.TryGetValue(pair.Key, out var count) && count == pair.Value);

    private static string Said(Dictionary<string, int> counts) =>
        string.Join(", ", counts.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => $"{pair.Value:N0} {pair.Key}"));

    private static async Task Write(string path, string text, CancellationToken cancellationToken)
    {
        var temporary = path + ".writing";
        await File.WriteAllTextAsync(temporary, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken);
        File.Move(temporary, path, overwrite: true);
    }

    internal sealed record Address(int Book, int Chapter, int Verse)
    {
        public override string ToString() => $"{Book}:{Chapter}:{Verse}";

        public static Address Parse(string typed, int book)
        {
            var parts = typed.Split(':');
            return parts.Length switch
            {
                2 => new Address(book, int.Parse(parts[0], CultureInfo.InvariantCulture), int.Parse(parts[1], CultureInfo.InvariantCulture)),
                3 => new Address(int.Parse(parts[0], CultureInfo.InvariantCulture), int.Parse(parts[1], CultureInfo.InvariantCulture),
                    int.Parse(parts[2], CultureInfo.InvariantCulture)),
                _ => throw new InvalidDataException($"'{typed}' is not an address of the ledger. Write chapter:verse, or book:chapter:verse."),
            };
        }
    }

    private sealed record WordAddress(Address At, int Nth, string Surface);

    private sealed record Reviewed(
        long? Source,
        long? Target,
        short Book,
        short Chapter,
        short Verse,
        LinkRelation? Absence,
        string? Kind,
        string? Tier,
        float? Confidence,
        string? Rationale,
        EvidentiaVerdict Verdict,
        bool Examined,
        string Reviewer,
        DateTimeOffset ReviewedAt,
        string? Note,
        long? Corrected);

    internal sealed record Session(string Reviewer, DateTimeOffset ReviewedAt, bool Examined, string? Note);

    /// <summary>What <see cref="RunFile"/> says about a run.</summary>
    /// <param name="Run">
    /// The run's number, which a rule's claim names (<see cref="EvidentiaClaims.RuleSource"/>) and by which a
    /// newer run supersedes an older; null in a file written before it was recorded.
    /// </param>
    internal sealed record LedgerRun(
        int? Run,
        string From,
        string To,
        DateTimeOffset StartedAt,
        DateTimeOffset? FinishedAt,
        string RuleVersion,
        JsonDocument Scope,
        JsonDocument Configuration,
        IReadOnlyList<Session> Reviews)
    {
        public static LedgerRun Read(string path)
        {
            var node = JsonNode.Parse(File.ReadAllText(path))
                       ?? throw new InvalidDataException($"{path} holds no JSON. Restore it from git.");
            return new LedgerRun(
                node["run"]?.GetValue<int>(),
                Required(node, "from", path),
                Required(node, "to", path),
                Moment(Required(node, "startedAt", path)),
                node["finishedAt"]?.GetValue<string>() is { } finished ? Moment(finished) : null,
                Required(node, "ruleVersion", path),
                JsonDocument.Parse(node["scope"]?.ToJsonString() ?? "{}"),
                JsonDocument.Parse(node["configuration"]?.ToJsonString() ?? "{}"),
                [.. (node["reviews"]?.AsArray() ?? []).Select(review => new Session(
                    Required(review!, "reviewer", path),
                    Moment(Required(review!, "reviewedAt", path)),
                    review!["examined"]?.GetValue<bool>() ?? false,
                    review["note"]?.GetValue<string>()))]);
        }

        private static string Required(JsonNode node, string key, string path) =>
            node[key]?.GetValue<string>() is { Length: > 0 } value
                ? value
                : throw new InvalidDataException($"{path} has no \"{key}\". Restore it from git, or write it again with evidentia-export.");

        private static DateTimeOffset Moment(string text) =>
            DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime();
    }

    /// <summary>One verdict as a line of a book file holds it.</summary>
    internal sealed record LedgerLine(
        Address At,
        int? Word,
        string Surface,
        Address TargetAt,
        int? Target,
        string TargetSurface,
        LinkRelation? Absence,
        string? Rule,
        string? Tier,
        float? Confidence,
        EvidentiaVerdict Verdict,
        int Review,
        Address CorrectedAt,
        int? Corrected,
        string CorrectedSurface,
        string? Rationale)
    {
        public string Where => $"{At} word {Word?.ToString(CultureInfo.InvariantCulture) ?? "-"}, target {Target?.ToString(CultureInfo.InvariantCulture) ?? "-"}";

        public static IEnumerable<LedgerLine> ReadAll(string path)
        {
            var book = int.Parse(Path.GetFileNameWithoutExtension(path), CultureInfo.InvariantCulture);
            foreach (var text in File.ReadLines(path).Skip(1).Where(text => text.Length > 0))
            {
                var field = text.Split(Tab);
                if (field.Length != Columns.Length)
                {
                    throw new InvalidDataException(
                        $"A line of {path} has {field.Length} fields where the ledger writes {Columns.Length}. Restore it from git.");
                }

                var at = Address.Parse(field[0], book);
                yield return new LedgerLine(
                    at,
                    Number(field[1]),
                    field[2],
                    field[3].Length == 0 ? at : Address.Parse(field[3], book),
                    Number(field[4]),
                    field[5],
                    field[6].Length == 0 ? null : EnumSpelling.ToLinkRelation(field[6]),
                    Empty(field[7]),
                    Empty(field[8]),
                    field[9].Length == 0 ? null : float.Parse(field[9], CultureInfo.InvariantCulture),
                    EnumSpelling.ToEvidentiaVerdict(field[10]),
                    int.Parse(field[11], CultureInfo.InvariantCulture),
                    field[12].Length == 0 ? at : Address.Parse(field[12], book),
                    Number(field[13]),
                    field[14],
                    Empty(field[15]));
            }
        }

        private static int? Number(string field) => field.Length == 0 ? null : int.Parse(field, CultureInfo.InvariantCulture);

        private static string? Empty(string field) => field.Length == 0 ? null : field;
    }

    /// <summary>One line of <see cref="HeadsFile"/>: an attached word and the head it goes with, each by address, ordinal and surface.</summary>
    internal sealed record LedgerHead(Address At, int Word, string Surface, Address HeadAt, int Head, string HeadSurface)
    {
        /// <summary>The heads a run's folder lists; null where the folder has no heads file, as one written before them.</summary>
        public static List<LedgerHead>? ReadAll(string folder)
        {
            var path = Path.Combine(folder, HeadsFile);
            if (!File.Exists(path))
            {
                return null;
            }

            var heads = new List<LedgerHead>();
            foreach (var text in File.ReadLines(path).Skip(1).Where(text => text.Length > 0))
            {
                var field = text.Split(Tab);
                if (field.Length != HeadColumns.Length)
                {
                    throw new InvalidDataException(
                        $"A line of {path} has {field.Length} fields where the ledger writes {HeadColumns.Length}. Restore it from git.");
                }

                var at = Address.Parse(field[0], 0);
                heads.Add(new LedgerHead(at, int.Parse(field[1], CultureInfo.InvariantCulture), field[2],
                    field[3].Length == 0 ? at : Address.Parse(field[3], at.Book), int.Parse(field[4], CultureInfo.InvariantCulture), field[5]));
            }

            return heads;
        }
    }

}
