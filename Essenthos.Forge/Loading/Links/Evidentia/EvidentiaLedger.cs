using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
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

/// <param name="Runs">Runs the ledger holds that were read.</param>
/// <param name="Verdicts">Verdicts those runs hold.</param>
/// <param name="Reconstituted">Runs the database did not hold, written again from the ledger with their verdicts.</param>
/// <param name="AlreadyThere">Verdicts the database already holds exactly as the ledger does.</param>
/// <param name="Restored">
/// Verdicts the database had lost: a review missing, or a verdict written to a link that something
/// has since deleted, which is written again.
/// </param>
/// <param name="Conflicts">
/// What could not be replayed and why: a word whose address no longer reads as it did, a text not in
/// the corpus, a verdict the database holds differently. Each is left as the database has it.
/// </param>
internal sealed record EvidentiaReplayOutcome(
    int Runs,
    int Verdicts,
    int Reconstituted,
    int AlreadyThere,
    int Restored,
    IReadOnlyList<string> Conflicts,
    IReadOnlyList<EvidentiaApplyOutcome> Applied,
    TimeSpan Elapsed)
{
    /// <summary>How many conflicts a report names one by one before it only counts the rest.</summary>
    private const int Named = 20;

    public override string ToString() =>
        Runs == 0
            ? "The EVIDENTIA ledger holds no verdicts, so there is nothing to replay"
            : $"The EVIDENTIA ledger: {Runs} runs, {Verdicts:N0} verdicts — {Reconstituted} runs written again from the ledger, "
              + $"{AlreadyThere:N0} verdicts already in the corpus as recorded, {Restored:N0} restored, {Conflicts.Count:N0} not replayed; in {Elapsed}"
              + string.Concat(Applied.Select(applied => "\n" + applied))
              + string.Concat(Conflicts.Take(Named).Select(conflict => "\n  not replayed: " + conflict))
              + (Conflicts.Count > Named ? $"\n  … and {Conflicts.Count - Named:N0} more" : string.Empty);
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
/// **Replaying is idempotent and settles, it does not overwrite.** A run the database already holds
/// — the same texts, start and rule version — is compared verdict by verdict: a review it lacks is
/// added, and a verdict whose link something deleted since is written again. A run it does not hold
/// is written again with the decisions its verdicts are about, and the verdicts are then applied by
/// <see cref="EvidentiaLinkWriter"/> exactly as they were the first time, against the corpus as it
/// now stands. A verdict the database holds differently from the ledger is reported and left alone.
/// </para>
///
/// <para>
/// Only what the verdicts need is kept. A run written again from the ledger holds the decisions its
/// verdicts are about and not the rest of what it measured; its evidence columns are empty.
/// </para>
/// </summary>
internal sealed class EvidentiaLedger(AppDbContext db, EvidentiaLinkWriter writer)
{
    /// <summary>Under the corpus sources, in this project's own folder.</summary>
    public static readonly string DefaultFolder = Path.Combine("Essenthos", "evidentia");

    public const string RunFile = "run.json";

    private const string BookPattern = "*.tsv";

    private const char Tab = '\t';

    /// <summary>The digits a confidence is written with, which is all of it the link writer keeps.</summary>
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
                                      ORDER BY v.chapter_number, v.number, w.position, w.id)::int AS nth,
                   w.text AS surface
            FROM word w
            JOIN verse v ON v.id = w.verse_id
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

        var fromWords = await AddressesOf(run.FromTextId, [.. reviewed.Select(row => row.Source)], cancellationToken);
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
        foreach (var stale in Directory.EnumerateFiles(folder, BookPattern))
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

        var record = new JsonObject
        {
            ["about"] = About,
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

    /// <param name="pair">Which runs to replay, by their texts; null for every run the ledger holds.</param>
    public async Task<EvidentiaReplayOutcome> Replay(
        string resources,
        Func<string, string, bool>? pair = null,
        CancellationToken cancellationToken = default)
    {
        var elapsed = Stopwatch.StartNew();
        var root = Folder(resources);
        var folders = Directory.Exists(root)
            ? Directory.EnumerateDirectories(root).Where(folder => File.Exists(Path.Combine(folder, RunFile)))
                .Order(StringComparer.Ordinal).ToList()
            : [];

        var tally = new ReplayTally();
        foreach (var folder in folders)
        {
            var run = LedgerRun.Read(Path.Combine(folder, RunFile));
            if (pair is not null && !pair(run.From, run.To))
            {
                continue;
            }

            await ReplayRun(folder, run, tally, cancellationToken);
        }

        return new EvidentiaReplayOutcome(tally.Runs, tally.Verdicts, tally.Reconstituted, tally.AlreadyThere, tally.Restored,
            tally.Conflicts, tally.Applied, elapsed.Elapsed);
    }

    private async Task ReplayRun(string folder, LedgerRun recorded, ReplayTally tally, CancellationToken cancellationToken)
    {
        var name = Path.GetFileName(folder);
        var lines = Directory.EnumerateFiles(folder, BookPattern).Order(StringComparer.Ordinal)
            .SelectMany(file => LedgerLine.ReadAll(file))
            .ToList();
        tally.Runs++;
        tally.Verdicts += lines.Count;

        var texts = await db.Texts.AsNoTracking()
            .Where(text => text.Slug == recorded.From || text.Slug == recorded.To)
            .ToDictionaryAsync(text => text.Slug, text => text.Id, StringComparer.Ordinal, cancellationToken);
        if (!texts.TryGetValue(recorded.From, out var fromId) || !texts.TryGetValue(recorded.To, out var toId))
        {
            tally.Conflicts.Add($"{name}: {(texts.ContainsKey(recorded.From) ? recorded.To : recorded.From)} is not in the corpus, "
                                + $"so its {lines.Count:N0} verdicts wait for it");
            return;
        }

        var books = lines.SelectMany(line => new[] { line.At.Book, line.TargetAt.Book, line.CorrectedAt.Book }).ToHashSet();
        var fromWords = Reverse(await Addresses(fromId, books, cancellationToken));
        var toWords = Reverse(await Addresses(toId, books, cancellationToken));

        var resolved = new List<(LedgerLine Line, long? Source, long? Target, long? Corrected)>(lines.Count);
        foreach (var line in lines)
        {
            var source = line.Word is { } word ? Resolve(fromWords, line.At, word, line.Surface, recorded.From, name, tally) : null;
            var target = line.Target is { } counterpart ? Resolve(toWords, line.TargetAt, counterpart, line.TargetSurface, recorded.To, name, tally) : null;
            var corrected = line.Corrected is { } named ? Resolve(toWords, line.CorrectedAt, named, line.CorrectedSurface, recorded.To, name, tally) : null;
            if ((line.Word is null || source is not null) && (line.Target is null || target is not null)
                && (line.Corrected is null || corrected is not null))
            {
                resolved.Add((line, source?.Id, target?.Id, corrected?.Id));
            }
        }

        var run = await db.EvidentiaRuns
            .SingleOrDefaultAsync(row => row.FromTextId == fromId && row.ToTextId == toId
                && row.StartedAt == recorded.StartedAt && row.RuleVersion == recorded.RuleVersion, cancellationToken);
        var toApply = run is null
            ? await Reconstitute(recorded, fromId, toId, resolved, tally, cancellationToken)
            : await Settle(run, recorded, name, resolved, tally, cancellationToken);

        if (toApply is { } runId)
        {
            tally.Applied.Add(await writer.Apply(runId, write: true, cancellationToken));
        }
    }

    /// <summary>Writes a run the database does not hold, its decisions and its verdicts, and says which run to apply.</summary>
    private async Task<int?> Reconstitute(
        LedgerRun recorded,
        int fromId,
        int toId,
        IReadOnlyList<(LedgerLine Line, long? Source, long? Target, long? Corrected)> resolved,
        ReplayTally tally,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var run = new EvidentiaRun
        {
            FromTextId = fromId,
            ToTextId = toId,
            StartedAt = recorded.StartedAt,
            FinishedAt = recorded.FinishedAt,
            RuleVersion = recorded.RuleVersion,
            Configuration = recorded.Configuration,
            Scope = recorded.Scope,
        };
        db.EvidentiaRuns.Add(run);
        await db.SaveChangesAsync(cancellationToken);

        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        await using (var import = await connection.BeginBinaryImportAsync(DecisionImport, cancellationToken))
        {
            foreach (var (line, source, target, _) in resolved)
            {
                await import.StartRowAsync(cancellationToken);
                await import.WriteAsync(run.Id, NpgsqlDbType.Integer, cancellationToken);
                await Optional(import, source, NpgsqlDbType.Bigint, cancellationToken);
                await import.WriteAsync((short)line.At.Book, NpgsqlDbType.Smallint, cancellationToken);
                await import.WriteAsync((short)line.At.Chapter, NpgsqlDbType.Smallint, cancellationToken);
                await import.WriteAsync((short)line.At.Verse, NpgsqlDbType.Smallint, cancellationToken);
                await import.WriteAsync(false, NpgsqlDbType.Boolean, cancellationToken);
                await Optional(import, target, NpgsqlDbType.Bigint, cancellationToken);
                await Text(import, line.Rule, cancellationToken);
                await Text(import, line.Tier, cancellationToken);
                await Text(import, line.Rationale, cancellationToken);
                await Optional(import, line.Confidence, NpgsqlDbType.Real, cancellationToken);
                await import.WriteAsync((short)0, NpgsqlDbType.Smallint, cancellationToken);
                await Text(import, line.Absence is { } absence ? EnumSpelling.Of(absence) : null, cancellationToken);
            }

            await import.CompleteAsync(cancellationToken);
        }

        var decisions = await Decisions(run.Id, cancellationToken);
        await using (var import = await connection.BeginBinaryImportAsync(ReviewImport, cancellationToken))
        {
            foreach (var (line, source, target, corrected) in resolved)
            {
                var session = recorded.Reviews[line.Review];
                await import.StartRowAsync(cancellationToken);
                await import.WriteAsync(decisions[Key(source, target)].Id, NpgsqlDbType.Bigint, cancellationToken);
                await import.WriteAsync(EnumSpelling.Of(line.Verdict), NpgsqlDbType.Text, cancellationToken);
                await import.WriteAsync(session.Examined, NpgsqlDbType.Boolean, cancellationToken);
                await Optional(import, corrected, NpgsqlDbType.Bigint, cancellationToken);
                await import.WriteAsync(session.Reviewer, NpgsqlDbType.Text, cancellationToken);
                await import.WriteAsync(session.ReviewedAt, NpgsqlDbType.TimestampTz, cancellationToken);
                await Text(import, session.Note, cancellationToken);
            }

            await import.CompleteAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        tally.Reconstituted++;
        tally.Restored += resolved.Count;
        return run.Id;
    }

    /// <summary>
    /// Compares a run the database holds with the ledger, verdict by verdict, and puts back what it
    /// lost; says which run to apply, or null where nothing changed.
    /// </summary>
    private async Task<int?> Settle(
        EvidentiaRun run,
        LedgerRun recorded,
        string name,
        IReadOnlyList<(LedgerLine Line, long? Source, long? Target, long? Corrected)> resolved,
        ReplayTally tally,
        CancellationToken cancellationToken)
    {
        var decisions = await Decisions(run.Id, cancellationToken);
        var added = new List<EvidentiaReview>();
        var lost = new List<long>();
        foreach (var (line, source, target, corrected) in resolved)
        {
            if (!decisions.TryGetValue(Key(source, target), out var decision))
            {
                tally.Conflicts.Add($"{name}: {line.Where} — run {run.Id} holds no decision about this word, so the verdict has nothing to stand on");
                continue;
            }

            if (decision.ReviewId is null)
            {
                var session = recorded.Reviews[line.Review];
                added.Add(new EvidentiaReview
                {
                    DecisionId = decision.Id,
                    Verdict = line.Verdict,
                    Examined = session.Examined,
                    CorrectedTargetWordId = corrected,
                    Reviewer = session.Reviewer,
                    ReviewedAt = session.ReviewedAt,
                    Note = session.Note,
                });
                continue;
            }

            if (decision.Verdict != line.Verdict || decision.Corrected != corrected)
            {
                tally.Conflicts.Add($"{name}: {line.Where} — the corpus holds {EnumSpelling.Of(decision.Verdict!.Value)} "
                                    + $"and the ledger {EnumSpelling.Of(line.Verdict)}; the corpus's is kept, and evidentia-export {run.Id} records it");
                continue;
            }

            if (decision.AppliedAt is not null && decision.LinkId is null && decision.Withheld is null
                && line.Verdict != EvidentiaVerdict.Rejected)
            {
                lost.Add(decision.ReviewId.Value);
                continue;
            }

            tally.AlreadyThere++;
        }

        if (added.Count == 0 && lost.Count == 0)
        {
            return null;
        }

        db.EvidentiaReviews.AddRange(added);
        await db.SaveChangesAsync(cancellationToken);
        foreach (var chunk in lost.Chunk(10_000))
        {
            await db.EvidentiaReviews.Where(review => chunk.Contains(review.Id))
                .ExecuteUpdateAsync(set => set.SetProperty(review => review.AppliedAt, (DateTimeOffset?)null), cancellationToken);
        }

        db.ChangeTracker.Clear();
        tally.Restored += added.Count + lost.Count;
        return run.Id;
    }

    private sealed record Held(long Id, long? ReviewId, EvidentiaVerdict? Verdict, long? Corrected, DateTimeOffset? AppliedAt, long? LinkId, string? Withheld);

    private async Task<Dictionary<(long?, long?), Held>> Decisions(int runId, CancellationToken cancellationToken) =>
        (await db.EvidentiaDecisions.AsNoTracking()
            .Where(decision => decision.RunId == runId)
            .Select(decision => new
            {
                decision.Id,
                decision.SourceWordId,
                decision.TargetWordId,
                Review = decision.Review == null
                    ? null
                    : new { decision.Review.Id, decision.Review.Verdict, decision.Review.CorrectedTargetWordId, decision.Review.AppliedAt, decision.Review.LinkId, decision.Review.Withheld },
            })
            .ToListAsync(cancellationToken))
        .ToDictionary(
            decision => Key(decision.SourceWordId, decision.TargetWordId),
            decision => new Held(decision.Id, decision.Review?.Id, decision.Review?.Verdict, decision.Review?.CorrectedTargetWordId,
                decision.Review?.AppliedAt, decision.Review?.LinkId, decision.Review?.Withheld));

    /// <summary>A run decides once about each word of its source, and about each word of its target only where it has no source word.</summary>
    private static (long?, long?) Key(long? source, long? target) => source is null ? (null, target) : (source, null);

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
            command.CommandTimeout = 0;
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

    private static Dictionary<(Address, int), (long Id, string Surface)> Reverse(Dictionary<long, WordAddress> addresses) =>
        addresses.ToDictionary(pair => (pair.Value.At, pair.Value.Nth), pair => (pair.Key, pair.Value.Surface));

    private static WordAddress Of(Dictionary<long, WordAddress> addresses, long word, string text) =>
        addresses.TryGetValue(word, out var address)
            ? address
            : throw new InvalidDataException(
                $"Word {word} of {text} has no canonical address, so a verdict about it cannot be kept outside the corpus. " +
                "Place its verse in the frame before recording a verdict on it.");

    private static (long Id, string Surface)? Resolve(
        Dictionary<(Address, int), (long Id, string Surface)> words,
        Address at,
        int nth,
        string surface,
        string text,
        string run,
        ReplayTally tally)
    {
        if (!words.TryGetValue((at, nth), out var word))
        {
            tally.Conflicts.Add($"{run}: {text} {at} has no word {nth} any more");
            return null;
        }

        if (!string.Equals(word.Surface, surface, StringComparison.Ordinal))
        {
            tally.Conflicts.Add($"{run}: {text} {at} word {nth} read '{surface}' and reads '{word.Surface}' now");
            return null;
        }

        return word;
    }

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

    private static async Task Optional<T>(NpgsqlBinaryImporter import, T? value, NpgsqlDbType type, CancellationToken cancellationToken)
        where T : struct
    {
        if (value is { } present)
        {
            await import.WriteAsync(present, type, cancellationToken);
        }
        else
        {
            await import.WriteNullAsync(cancellationToken);
        }
    }

    private static async Task Text(NpgsqlBinaryImporter import, string? value, CancellationToken cancellationToken)
    {
        if (value is null)
        {
            await import.WriteNullAsync(cancellationToken);
        }
        else
        {
            await import.WriteAsync(value, NpgsqlDbType.Text, cancellationToken);
        }
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
    internal sealed record LedgerRun(
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

    private sealed class ReplayTally
    {
        public int Runs;
        public int Verdicts;
        public int Reconstituted;
        public int AlreadyThere;
        public int Restored;
        public List<string> Conflicts { get; } = [];
        public List<EvidentiaApplyOutcome> Applied { get; } = [];
    }
}
