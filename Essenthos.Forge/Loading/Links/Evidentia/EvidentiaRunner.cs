using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Essenthos.Core.Loading.Links.Evidentia;

/// <summary>One book of a run, optionally narrowed to a chapter range.</summary>
internal sealed record EvidentiaBookScope(int Book, int? FromChapter = null, int? ToChapter = null)
{
    /// <summary>Reads <c>8</c>, <c>1:3</c> or <c>1:1-10</c>.</summary>
    public static EvidentiaBookScope Parse(string typed)
    {
        var parts = typed.Split(':');
        if (!int.TryParse(parts[0], out var book) || parts.Length > 2)
        {
            throw new ArgumentException(
                $"'{typed}' is not a book scope. Write a canonical book number, optionally with chapters: 8, 1:3 or 1:1-10.");
        }

        if (parts.Length == 1)
        {
            return new EvidentiaBookScope(book);
        }

        var range = parts[1].Split('-');
        if (!int.TryParse(range[0], out var from) || range.Length > 2
            || (range.Length == 2 && !int.TryParse(range[1], out _)))
        {
            throw new ArgumentException($"'{typed}' has an unreadable chapter range. Write it as 1:3 or 1:1-10.");
        }

        var to = range.Length == 2 ? int.Parse(range[1], CultureInfo.InvariantCulture) : from;
        return from <= to
            ? new EvidentiaBookScope(book, from, to)
            : throw new ArgumentException($"'{typed}' runs its chapters backwards. Write the lower chapter first.");
    }

    public override string ToString() => (FromChapter, ToChapter) switch
    {
        (null, _) => Book.ToString(CultureInfo.InvariantCulture),
        var (from, to) when from == to => $"{Book}:{from}",
        var (from, to) => $"{Book}:{from}-{to}",
    };
}

/// <param name="Proposals">Words placed on a word of the other text.</param>
/// <param name="Absences">Words said to have no counterpart, supplied or not rendered.</param>
internal sealed record EvidentiaRunOutcome(
    int RunId,
    int Decisions,
    int Proposals,
    int Safe,
    int Abstentions,
    int Absences,
    TimeSpan Elapsed,
    IReadOnlyList<EvidentiaBookMeasurement> Measurements)
{
    public override string ToString() =>
        $"EVIDENTIA run {RunId}: {Decisions:N0} decisions stored — {Proposals:N0} proposals ({Safe:N0} in the safe tier), " +
        $"{Absences:N0} absences, {Abstentions:N0} words placed nowhere — in {Elapsed}. Nothing was written to the corpus.\n" +
        string.Join("\n", Measurements);
}

/// <summary>
/// Runs EVIDENTIA over a scope and stores what it decided, word by word, under one run.
///
/// <para>
/// It measures exactly as the measurement commands do and keeps what the measurement scored, so a
/// stored run and a published figure cannot describe two different selections. It writes the run
/// and its decisions and nothing else: the corpus is reached through an approved review and
/// <see cref="EvidentiaLinkWriter"/>, never from here.
/// </para>
/// </summary>
internal sealed class EvidentiaRunner(AppDbContext db, EvidentiaCorpusPreviewLoader loader)
{
    private const string DecisionImport =
        """
        COPY evidentia_decision (
            run_id, source_word_id, canonical_book, canonical_chapter, canonical_verse, content,
            target_word_id, kind, tier, rationale, confidence, score, margin, candidates, abstention,
            exact_address, neighbouring_address, matching_form, shared_strong, dictionary_sense,
            target_gloss, known_rendering, morphology, syntax, statistical_aligner,
            rendering_observations, rendering_share, rendering_next_share, rendering_form,
            alternative_word_ids, alternative_scores, alternative_distances, alternative_taken,
            absence, anchor_source_word_id, anchor_target_word_id)
        FROM STDIN (FORMAT BINARY)
        """;

    public async Task<EvidentiaRunOutcome> Run(
        string fromSlug,
        string toSlug,
        IReadOnlyList<EvidentiaBookScope> books,
        EvidentiaMeasurementOptions options,
        CancellationToken cancellationToken = default) =>
        await Run(fromSlug, toSlug, books, options, parentRunId: null, verses: null, cancellationToken);

    /// <summary>
    /// Repeats a run over some of its verses, with the configuration it was run with, as a new run
    /// whose parent is the old one. A verse is measured with its whole chapter, because the learned
    /// index holds the chapter out, and only the chosen verses are kept.
    /// </summary>
    public async Task<EvidentiaRunOutcome> Rerun(
        int parentRunId,
        IReadOnlyCollection<EvidentiaAddress> verses,
        CancellationToken cancellationToken = default)
    {
        var parent = await db.EvidentiaRuns.AsNoTracking()
            .Where(run => run.Id == parentRunId)
            .Select(run => new { FromSlug = run.FromText!.Slug, ToSlug = run.ToText!.Slug, run.Configuration })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException(
                $"There is no EVIDENTIA run {parentRunId}. List the stored runs with evidentia-runs.");
        if (verses.Count == 0)
        {
            throw new InvalidOperationException($"Run {parentRunId} was asked to repeat no verses; name at least one.");
        }

        var books = verses
            .GroupBy(verse => (verse.Book, verse.Chapter))
            .OrderBy(group => group.Key)
            .Select(group => new EvidentiaBookScope(group.Key.Book, group.Key.Chapter, group.Key.Chapter))
            .ToList();
        return await Run(parent.FromSlug, parent.ToSlug, books, Options(parent.Configuration), parentRunId,
            verses.ToHashSet(), cancellationToken);
    }

    private async Task<EvidentiaRunOutcome> Run(
        string fromSlug,
        string toSlug,
        IReadOnlyList<EvidentiaBookScope> books,
        EvidentiaMeasurementOptions options,
        int? parentRunId,
        IReadOnlySet<EvidentiaAddress>? verses,
        CancellationToken cancellationToken)
    {
        var elapsed = Stopwatch.StartNew();
        var texts = await db.Texts.AsNoTracking()
            .Where(text => text.Slug == fromSlug || text.Slug == toSlug)
            .ToDictionaryAsync(text => text.Slug, text => text.Id, cancellationToken);
        if (!texts.TryGetValue(fromSlug, out var fromId) || !texts.TryGetValue(toSlug, out var toId))
        {
            throw new InvalidOperationException(
                $"EVIDENTIA needs two loaded texts and {(texts.ContainsKey(fromSlug) ? toSlug : fromSlug)} is not one.");
        }

        options = await WithConfirmed(options, parentRunId, cancellationToken);
        if (options.SecondPass && parentRunId is null)
        {
            // The first pass, kept nowhere: what it placed over the whole scope by evidence of its own, the
            // aligner not consulted, is what the stored pass reads.
            var learnt = options.Confirmed ?? new EvidentiaConfirmedRenderings();
            var first = options with
            {
                Confirmed = null, Learns = learnt, AlignerLinks = false, AlignerPairs = null,
                Decisions = null, RecordWords = false, RecordDisagreements = false,
            };
            foreach (var book in books)
            {
                await loader.MeasureBook(fromSlug, toSlug, book.Book, first, book.FromChapter, book.ToChapter, cancellationToken);
            }

            options = options with { Confirmed = learnt };
        }

        var run = new EvidentiaRun
        {
            FromTextId = fromId,
            ToTextId = toId,
            ParentRunId = parentRunId,
            StartedAt = DateTimeOffset.UtcNow,
            RuleVersion = RuleVersion,
            Configuration = Configuration(options, new SortedDictionary<string, SortedSet<string>>()),
            Scope = JsonSerializer.SerializeToDocument(new
            {
                books = books.Select(book => book.ToString()).ToArray(),
                verses = verses?.OrderBy(verse => (verse.Book, verse.Chapter, verse.Verse)).Select(Address).ToArray(),
            }),
        };
        db.EvidentiaRuns.Add(run);
        await db.SaveChangesAsync(cancellationToken);

        var evidenceSources = new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        var measurements = new List<EvidentiaBookMeasurement>(books.Count);
        int decisions = 0, proposals = 0, safe = 0, absences = 0;
        foreach (var book in books)
        {
            var recorder = new EvidentiaDecisionRecorder(run.Id, verses);
            measurements.Add(await loader.MeasureBook(fromSlug, toSlug, book.Book, options with { Decisions = recorder },
                book.FromChapter, book.ToChapter, cancellationToken));
            await Write(recorder.Decisions, cancellationToken);

            decisions += recorder.Decisions.Count;
            proposals += recorder.Decisions.Count(decision => decision.Absence is null && decision.TargetWordId is not null);
            absences += recorder.Decisions.Count(decision => decision.Absence is not null);
            safe += recorder.Decisions.Count(decision => decision.Tier == EvidentiaDecisionRecorder.SafeTier);
            foreach (var (kind, sources) in recorder.EvidenceSources)
            {
                if (!evidenceSources.TryGetValue(kind, out var all))
                {
                    evidenceSources[kind] = all = new SortedSet<string>(StringComparer.Ordinal);
                }

                all.UnionWith(sources);
            }
        }

        run.Configuration = Configuration(options, evidenceSources);
        run.FinishedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        return new EvidentiaRunOutcome(run.Id, decisions, proposals, safe, decisions - proposals - absences, absences,
            elapsed.Elapsed, measurements);
    }

    /// <summary>
    /// The options with the confirmed renderings they name read in: the files, and what the stored runs
    /// placed by evidence of their own. A run repeated over some verses reads its parent's placements as
    /// its first pass, since the whole text is not measured again.
    /// </summary>
    internal async Task<EvidentiaMeasurementOptions> WithConfirmed(
        EvidentiaMeasurementOptions options, int? parentRunId = null, CancellationToken cancellationToken = default)
    {
        List<int> runs = [.. options.ConfirmedByRuns ?? []];
        if (options.SecondPass && parentRunId is { } parent)
        {
            runs.Add(parent);
        }

        if (runs.Count == 0 && options.ConfirmedByFiles is not { Count: > 0 })
        {
            return options;
        }

        var confirmed = EvidentiaConfirmedRenderings.Read(options.ConfirmedByFiles ?? []);
        if (runs.Count > 0)
        {
            var known = await db.EvidentiaRuns.AsNoTracking().Where(run => runs.Contains(run.Id)).Select(run => run.Id)
                .ToListAsync(cancellationToken);
            if (runs.Except(known).ToList() is { Count: > 0 } missing)
            {
                throw new InvalidOperationException(
                    $"There is no EVIDENTIA run {string.Join(", ", missing)} to confirm renderings by. List the stored runs with evidentia-runs.");
            }

            var placed = await db.EvidentiaDecisions.AsNoTracking()
                .Where(decision => runs.Contains(decision.RunId) && decision.Absence == null
                    && decision.Kind != null && !NotLexical.Contains(decision.Kind)
                    && decision.TargetWord!.StrongNumber != null)
                .GroupBy(decision => new
                {
                    Form = decision.SourceWord!.Surface.ToLower(),
                    decision.TargetWord!.StrongNumber,
                })
                .Select(pair => new
                {
                    pair.Key.Form,
                    pair.Key.StrongNumber,
                    Safe = pair.Count(decision => decision.Tier == EvidentiaDecisionRecorder.SafeTier),
                    Placed = pair.Count(),
                })
                .ToListAsync(cancellationToken);
            foreach (var pair in placed)
            {
                confirmed.Add(pair.Form, pair.StrongNumber!, pair.Safe, pair.Placed);
            }
        }

        return options with { Confirmed = confirmed };
    }

    /// <summary>The kinds <see cref="EvidentiaConfirmedRenderings.Learn"/> leaves out, as a run stores them.</summary>
    private static readonly string[] NotLexical =
    [
        EvidentiaDecisionRecorder.Spelling(EvidentiaProposalKind.AttachedWord),
        EvidentiaDecisionRecorder.Spelling(EvidentiaProposalKind.UniqueCounterpart),
        EvidentiaDecisionRecorder.Spelling(EvidentiaProposalKind.ConfirmedRendering),
    ];

    /// <summary>Every stored run, newest first, with what it holds and how much of it was reviewed.</summary>
    public async Task<string> List(CancellationToken cancellationToken = default)
    {
        var runs = await db.EvidentiaRuns.AsNoTracking()
            .OrderByDescending(run => run.Id)
            .Select(run => new
            {
                run.Id,
                From = run.FromText!.Slug,
                To = run.ToText!.Slug,
                run.ParentRunId,
                run.StartedAt,
                run.FinishedAt,
                run.RuleVersion,
                run.Scope,
                Decisions = run.Decisions.Count(),
                Proposals = run.Decisions.Count(decision => decision.Absence == null && decision.TargetWordId != null),
                Absences = run.Decisions.Count(decision => decision.Absence != null),
                Reviewed = run.Decisions.Count(decision => decision.Review != null),
                Applied = run.Decisions.Count(decision => decision.Review != null && decision.Review.AppliedAt != null),
            })
            .ToListAsync(cancellationToken);
        return runs.Count == 0
            ? "No EVIDENTIA run is stored. Start one with evidentia-run."
            : string.Join("\n", runs.Select(run =>
                $"run {run.Id} {run.From} → {run.To} {run.Scope.RootElement.GetRawText()}"
                + (run.ParentRunId is { } parent ? $" repeating run {parent}" : string.Empty)
                + $"; {run.Decisions:N0} decisions, {run.Proposals:N0} proposals, {run.Absences:N0} absences, {run.Reviewed:N0} reviewed, {run.Applied:N0} written; "
                + $"{run.StartedAt:u}" + (run.FinishedAt is null ? " UNFINISHED" : string.Empty) + $"; {run.RuleVersion}"));
    }

    internal async Task Write(IReadOnlyList<EvidentiaDecision> decisions, CancellationToken cancellationToken)
    {
        if (decisions.Count == 0)
        {
            return;
        }

        await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            var connection = (NpgsqlConnection)db.Database.GetDbConnection();
            await using var writer = await connection.BeginBinaryImportAsync(DecisionImport, cancellationToken);
            foreach (var decision in decisions)
            {
                await writer.StartRowAsync(cancellationToken);
                await writer.WriteAsync(decision.RunId, NpgsqlDbType.Integer, cancellationToken);
                await Nullable(writer, decision.SourceWordId, NpgsqlDbType.Bigint, cancellationToken);
                await writer.WriteAsync(decision.CanonicalBook, NpgsqlDbType.Smallint, cancellationToken);
                await writer.WriteAsync(decision.CanonicalChapter, NpgsqlDbType.Smallint, cancellationToken);
                await writer.WriteAsync(decision.CanonicalVerse, NpgsqlDbType.Smallint, cancellationToken);
                await writer.WriteAsync(decision.Content, NpgsqlDbType.Boolean, cancellationToken);
                await Nullable(writer, decision.TargetWordId, NpgsqlDbType.Bigint, cancellationToken);
                await Reference(writer, decision.Kind, NpgsqlDbType.Text, cancellationToken);
                await Reference(writer, decision.Tier, NpgsqlDbType.Text, cancellationToken);
                await Reference(writer, decision.Rationale, NpgsqlDbType.Text, cancellationToken);
                await Nullable(writer, decision.Confidence, NpgsqlDbType.Real, cancellationToken);
                await Nullable(writer, decision.Score, NpgsqlDbType.Real, cancellationToken);
                await Nullable(writer, decision.Margin, NpgsqlDbType.Real, cancellationToken);
                await writer.WriteAsync(decision.Candidates, NpgsqlDbType.Smallint, cancellationToken);
                await Reference(writer, decision.Abstention is { } abstention ? EnumSpelling.Of(abstention) : null,
                    NpgsqlDbType.Text, cancellationToken);
                await Nullable(writer, decision.ExactAddress, NpgsqlDbType.Real, cancellationToken);
                await Nullable(writer, decision.NeighbouringAddress, NpgsqlDbType.Real, cancellationToken);
                await Nullable(writer, decision.MatchingForm, NpgsqlDbType.Real, cancellationToken);
                await Nullable(writer, decision.SharedStrong, NpgsqlDbType.Real, cancellationToken);
                await Nullable(writer, decision.DictionarySense, NpgsqlDbType.Real, cancellationToken);
                await Nullable(writer, decision.TargetGloss, NpgsqlDbType.Real, cancellationToken);
                await Nullable(writer, decision.KnownRendering, NpgsqlDbType.Real, cancellationToken);
                await Nullable(writer, decision.Morphology, NpgsqlDbType.Real, cancellationToken);
                await Nullable(writer, decision.Syntax, NpgsqlDbType.Real, cancellationToken);
                await Nullable(writer, decision.StatisticalAligner, NpgsqlDbType.Real, cancellationToken);
                await Nullable(writer, decision.RenderingObservations, NpgsqlDbType.Integer, cancellationToken);
                await Nullable(writer, decision.RenderingShare, NpgsqlDbType.Real, cancellationToken);
                await Nullable(writer, decision.RenderingNextShare, NpgsqlDbType.Real, cancellationToken);
                await Reference(writer, decision.RenderingForm, NpgsqlDbType.Text, cancellationToken);
                await Reference(writer, decision.AlternativeWordIds, NpgsqlDbType.Array | NpgsqlDbType.Bigint, cancellationToken);
                await Reference(writer, decision.AlternativeScores, NpgsqlDbType.Array | NpgsqlDbType.Real, cancellationToken);
                await Reference(writer, decision.AlternativeDistances, NpgsqlDbType.Array | NpgsqlDbType.Smallint, cancellationToken);
                await Reference(writer, decision.AlternativeTaken, NpgsqlDbType.Array | NpgsqlDbType.Boolean, cancellationToken);
                await Reference(writer, decision.Absence is { } absence ? EnumSpelling.Of(absence) : null,
                    NpgsqlDbType.Text, cancellationToken);
                await Nullable(writer, decision.AnchorSourceWordId, NpgsqlDbType.Bigint, cancellationToken);
                await Nullable(writer, decision.AnchorTargetWordId, NpgsqlDbType.Bigint, cancellationToken);
            }

            await writer.CompleteAsync(cancellationToken);
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    private static async Task Nullable<T>(
        NpgsqlBinaryImporter writer, T? value, NpgsqlDbType type, CancellationToken cancellationToken)
        where T : struct
    {
        if (value is { } present)
        {
            await writer.WriteAsync(present, type, cancellationToken);
        }
        else
        {
            await writer.WriteNullAsync(cancellationToken);
        }
    }

    private static async Task Reference<T>(
        NpgsqlBinaryImporter writer, T? value, NpgsqlDbType type, CancellationToken cancellationToken)
        where T : class
    {
        if (value is null)
        {
            await writer.WriteNullAsync(cancellationToken);
        }
        else
        {
            await writer.WriteAsync(value, type, cancellationToken);
        }
    }

    /// <summary>The build that ran, which carries its commit where the SDK could read one.</summary>
    public static string RuleVersion { get; } =
        typeof(EvidentiaRunner).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(EvidentiaRunner).Assembly.GetName().Version?.ToString()
        ?? "unversioned";

    private static string Address(EvidentiaAddress address) =>
        $"{address.Book}:{address.Chapter}:{address.Verse}";

    /// <summary>
    /// What the run was allowed to read, the thresholds it read with, and which source stood
    /// behind each signal. The thresholds are copied out of <see cref="EvidentiaDefaults"/> rather
    /// than named by version, because a version names a build and a build can be dirty.
    /// </summary>
    private static JsonDocument Configuration(
        EvidentiaMeasurementOptions options,
        SortedDictionary<string, SortedSet<string>> evidenceSources) =>
        JsonSerializer.SerializeToDocument(new Dictionary<string, object?>
        {
            ["allowSourceStrongEvidence"] = options.AllowSourceStrongEvidence,
            ["allowKnownRenderingEvidence"] = options.AllowKnownRenderingEvidence,
            ["learnRenderingsFrom"] = options.LearnRenderingsFrom,
            ["learnedRenderingMethods"] = options.LearnedRenderingMethods?.Select(EnumSpelling.Of).ToArray(),
            ["neighbourVerseDistance"] = options.NeighbourVerseDistance,
            ["entityAnchors"] = options.EntityAnchors,
            ["secondPass"] = options.SecondPass,
            ["confirmedByRuns"] = options.ConfirmedByRuns,
            ["confirmedByFiles"] = options.ConfirmedByFiles,
            ["alignerLinks"] = options.AlignerLinks,
            ["defaults"] = typeof(EvidentiaDefaults)
                .GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(field => field.IsLiteral)
                .OrderBy(field => field.Name, StringComparer.Ordinal)
                .ToDictionary(field => field.Name, field => field.GetRawConstantValue()),
            ["evidenceSources"] = evidenceSources.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray()),
        });

    internal static EvidentiaMeasurementOptions Options(JsonDocument configuration)
    {
        var root = configuration.RootElement;
        var methods = root.TryGetProperty("learnedRenderingMethods", out var stored)
                      && stored.ValueKind == JsonValueKind.Array
            ? stored.EnumerateArray().Select(method => EnumSpelling.ToLinkMethod(method.GetString()!)).ToList()
            : null;
        return new EvidentiaMeasurementOptions(
            AllowSourceStrongEvidence: root.GetProperty("allowSourceStrongEvidence").GetBoolean(),
            AllowKnownRenderingEvidence: root.GetProperty("allowKnownRenderingEvidence").GetBoolean(),
            LearnRenderingsFrom: root.TryGetProperty("learnRenderingsFrom", out var learnFrom)
                                 && learnFrom.ValueKind == JsonValueKind.String
                ? learnFrom.GetString()
                : null,
            LearnedRenderingMethods: methods,
            NeighbourVerseDistance: NeighbourVerseDistance(root),
            EntityAnchors: root.TryGetProperty("entityAnchors", out var anchors) && anchors.ValueKind == JsonValueKind.True,
            SecondPass: root.TryGetProperty("secondPass", out var secondPass) && secondPass.ValueKind == JsonValueKind.True,
            ConfirmedByRuns: root.TryGetProperty("confirmedByRuns", out var byRuns) && byRuns.ValueKind == JsonValueKind.Array
                ? [.. byRuns.EnumerateArray().Select(run => run.GetInt32())]
                : null,
            ConfirmedByFiles: root.TryGetProperty("confirmedByFiles", out var byFiles) && byFiles.ValueKind == JsonValueKind.Array
                ? [.. byFiles.EnumerateArray().Select(file => file.GetString()!)]
                : null,
            AlignerLinks: root.TryGetProperty("alignerLinks", out var alignerLinks) && alignerLinks.ValueKind == JsonValueKind.True);
    }

    /// <summary>A run stored before the window was an option read with the default of its day.</summary>
    private static int NeighbourVerseDistance(JsonElement root) =>
        root.TryGetProperty("neighbourVerseDistance", out var distance)
            ? distance.GetInt32()
            : root.TryGetProperty("defaults", out var defaults)
              && defaults.TryGetProperty(nameof(EvidentiaDefaults.NeighbourVerseDistance), out var stored)
                ? stored.GetInt32()
                : EvidentiaDefaults.NeighbourVerseDistance;
}
