using System.Diagnostics;
using System.Globalization;
using System.Text;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

using Candidate = (int Source, int Target, double Translation, double Alignment);

namespace Essenthos.Core.Loading.Links;

internal sealed record AlignmentOutcome(
    string From,
    string To,
    int Verses,
    int Proposed,
    int Collapsed,
    int BelowThreshold,
    int Written,
    TimeSpan Elapsed)
{
    public override string ToString() =>
        $"{From} to {To}: {Written} links from {Proposed} proposed over {Verses} verses in {Elapsed} — " +
        $"{BelowThreshold} below the threshold, {Collapsed} in collapsed clusters";
}

/// <summary>
/// Aligns two texts statistically and writes what survives as links.
///
/// The model is trained and run by SIL's <c>machine</c> tool rather than in this process, because
/// the same library crashes the process above a few hundred verses through its own API and does all
/// twenty-three thousand without complaint through the tool. Alignment is computed once per pair of
/// texts, so a batch step that writes a file is the right shape anyway.
///
/// Nothing it produces is stated by anyone. Every link carries <c>aligner</c> and the model's own
/// confidence, and the schema makes it impossible to store one as though a source had said it.
/// </summary>
internal sealed class AlignmentPipeline(AppDbContext db, ILogger<AlignmentPipeline> logger)
{
    /// <summary>
    /// Measured with <c>score kjv bhsa</c> against the 625,826 correspondences the mapping file
    /// states, so this number is a measurement anyone can repeat and not one somebody chose.
    ///
    ///     min    kept   content precision   where the file answers
    ///     0.20  379199        86.5 %              91.1 %
    ///     0.25  347690        87.6 %              92.1 %
    ///     0.30  319983        88.4 %              93.0 %
    ///     0.50  228354        91.5 %              95.8 %
    ///     0.60  198416        93.4 %              97.3 %
    ///
    /// The two columns differ because the file is often silent rather than contradicting: at 0.3
    /// half of what is scored wrong is the aligner reaching a Hebrew word the file links to nothing
    /// at all, and a third of the rest is it choosing between two words the file itself renders the
    /// same — "great" against גָּדֹל where the file states גְּדֹלִים. The second column drops the
    /// silence and asks only the decidable question: where the file names a Hebrew word for this
    /// English one, does the model name the same one.
    ///
    /// 0.25 is the trade taken. It keeps 347,690 pairs, agrees with the file 92.1% of the time where
    /// the file answers, and is low enough to keep the correct-but-unremarkable words a reader
    /// notices the absence of — Genesis 1:1 in Russian is seven words linked or it is wrong. Every
    /// link carries its own confidence, so a reader is never told that a pair scoring 0.26 and one
    /// scoring 0.99 are the same claim; raising this constant to 0.5 is available and costs a third
    /// of the corpus.
    ///
    /// The table above is what the model scores on its own. <see cref="SyntaxPrior"/> reads the
    /// target's own phrase and clause structure over the same proposals before this threshold sees
    /// them, and moves the numbers a little: 84.1% against 83.9% here, and 88.0% content precision
    /// against 87.7%, for 342,038 pairs instead of 346,263. It sharpens the order the proposals
    /// stand in rather than changing where the line should be drawn, so the threshold is unaffected.
    /// </summary>
    public const double DefaultMinimumConfidence = 0.25;

    /// <summary>
    /// When this many source words all point at one target word and none of them confidently, the
    /// model has run out of signal and is dumping the verse onto whatever it can reach. It is not
    /// an alignment and it looks like one.
    /// </summary>
    private const int CollapsedCluster = 4;

    private const string LinkImport =
        """
        COPY link (id, from_text_id, to_text_id, relation, method, confidence, source)
        FROM STDIN (FORMAT BINARY)
        """;

    private const string LinkWordImport =
        "COPY link_word (link_id, word_id, side) FROM STDIN (FORMAT BINARY)";

    public async Task<AlignmentOutcome> Run(
        string fromSlug,
        string toSlug,
        string workspace,
        double minimumConfidence,
        string modelType,
        CancellationToken cancellationToken = default)
    {
        var from = await Text(fromSlug, cancellationToken);
        var to = await Text(toSlug, cancellationToken);

        if (await db.Links.AnyAsync(
                l => l.FromTextId == from.Id && l.ToTextId == to.Id && l.Method == LinkMethod.Aligner,
                cancellationToken))
        {
            logger.LogInformation("{From} and {To} are already aligned; nothing to do", fromSlug, toSlug);
            return new AlignmentOutcome(fromSlug, toSlug, 0, 0, 0, 0, 0, TimeSpan.Zero);
        }

        var started = Stopwatch.StartNew();
        Directory.CreateDirectory(workspace);

        var source = await Words(fromSlug, Reduce, cancellationToken);
        var target = await Words(toSlug, Comparable, cancellationToken);
        var addresses = source.Keys.Intersect(target.Keys).OrderBy(a => a).ToList();
        if (addresses.Count == 0)
        {
            throw new InvalidOperationException(
                $"\"{fromSlug}\" and \"{toSlug}\" share no verse in the canonical frame. Both texts have to be " +
                "loaded and placed before they can be aligned.");
        }

        var alignmentFile = await Align(
            fromSlug, toSlug, workspace, modelType, addresses, source, target, cancellationToken);

        await db.Database.OpenConnectionAsync(cancellationToken);
        var prior = await SyntaxPrior.Read(
            (NpgsqlConnection)db.Database.GetDbConnection(), to.Id, cancellationToken);

        var (drafts, proposed, collapsed, below) = Read(
            alignmentFile, addresses, source, target, minimumConfidence, Selection.BestPerSource, prior);

        await Store(from.Id, to.Id, modelType, prior.Known, drafts, cancellationToken);

        var outcome = new AlignmentOutcome(
            fromSlug, toSlug, addresses.Count, proposed, collapsed, below, drafts.Count, started.Elapsed);
        logger.LogInformation("Aligned {Outcome}", outcome);
        return outcome;
    }

    /// <summary>
    /// Trains the model and runs it, unless the workspace already holds the answer. Training the
    /// twenty-three thousand verse pairs takes minutes, and the threshold is a question about how to
    /// read the output rather than how to produce it, so a measurement sweep reuses one run.
    /// </summary>
    private async Task<string> Align(
        string fromSlug,
        string toSlug,
        string workspace,
        string modelType,
        List<(int, int, int)> addresses,
        Dictionary<(int, int, int), List<Word>> source,
        Dictionary<(int, int, int), List<Word>> target,
        CancellationToken cancellationToken)
    {
        var sourceFile = Path.Combine(workspace, "source.txt");
        var targetFile = Path.Combine(workspace, "target.txt");
        var alignmentFile = Path.Combine(workspace, "alignment", "pharaoh.txt");
        var modelPrefix = Path.Combine(workspace, "model", $"{fromSlug}-{toSlug}");

        if (File.Exists(alignmentFile))
        {
            logger.LogInformation("Reusing the alignment already in {Workspace}", workspace);
            return alignmentFile;
        }

        Write(sourceFile, addresses, source);
        Write(targetFile, addresses, target);
        Directory.CreateDirectory(Path.GetDirectoryName(alignmentFile)!);
        Directory.CreateDirectory(Path.GetDirectoryName(modelPrefix)!);

        logger.LogInformation("Training {Model} over {Verses} verse pairs", modelType, addresses.Count);
        await Machine(["train", "alignment-model", "-mt", modelType, modelPrefix, sourceFile, targetFile],
            cancellationToken);
        await Machine(
            ["align", "-mt", modelType, "-sh", "och", "-s", modelPrefix, sourceFile, targetFile, alignmentFile],
            cancellationToken);

        return alignmentFile;
    }

    /// <summary>
    /// What the model proposes, without storing anything.
    ///
    /// Composition needs this rather than the links already stored, and the difference matters: a
    /// pair is written only if it clears the threshold on its own, but a pair that two routes both
    /// propose faintly is not faint evidence. Genesis 1:3 has "стал" against יְהִי at 0.16 by one
    /// route and 0.22 by the other — neither is worth writing alone, and the two together are worth
    /// a third of a reader's trust, which is more than the threshold asks. Reading only what was
    /// stored would have thrown both away before they could meet.
    /// </summary>
    public async Task<IReadOnlyList<(long From, long To, double Confidence)>> Proposals(
        string fromSlug,
        string toSlug,
        string workspace,
        double floor,
        bool asWritten = false,
        Selection selection = Selection.BestPerSource,
        string modelType = "ibm4",
        CancellationToken cancellationToken = default)
    {
        // Only the source is read as written. The target's own reduction is not a hedge — BHSA's
        // consonantal text and Nestle's lemmas are the forms those texts themselves carry, and both
        // were measured as plainly better than the pointing and the inflection they replace.
        var source = await Words(fromSlug, asWritten ? Written : Reduce, cancellationToken);
        var target = await Words(toSlug, Comparable, cancellationToken);
        var addresses = source.Keys.Intersect(target.Keys).OrderBy(a => a).ToList();

        Directory.CreateDirectory(workspace);
        var alignmentFile = await Align(
            fromSlug, toSlug, workspace, modelType, addresses, source, target, cancellationToken);

        await db.Database.OpenConnectionAsync(cancellationToken);
        var prior = await SyntaxPrior.Read(
            (NpgsqlConnection)db.Database.GetDbConnection(),
            (await Text(toSlug, cancellationToken)).Id,
            cancellationToken);

        var (drafts, _, _, _) = Read(alignmentFile, addresses, source, target, floor, selection, prior);
        return [.. drafts.Select(d => (d.SourceWordId, d.TargetWordId, d.Translation))];
    }

    /// <summary>
    /// Scores the model over a range of thresholds against the correspondences a source states for
    /// the same two texts, so the threshold is a measurement anyone can repeat rather than a number
    /// somebody once chose.
    ///
    /// It reports twice, because the two figures answer different questions. Every stated pair
    /// includes the function words a phrase carries - "the beginning" states both of its words
    /// against the one Hebrew word - and an aligner that declines to guess which of them is meant is
    /// marked wrong for it. The second figure drops the pairs whose Hebrew word is a prefix or the
    /// object marker, and is the closer answer to "when it says two words correspond, is it right".
    /// </summary>
    public async Task<string> Measure(
        string fromSlug,
        string toSlug,
        string workspace,
        IReadOnlyList<double> thresholds,
        string modelType,
        bool targetSurface = false,
        bool statedOnly = false,
        CancellationToken cancellationToken = default)
    {
        var from = await Text(fromSlug, cancellationToken);
        var to = await Text(toSlug, cancellationToken);
        var source = await Words(fromSlug, targetSurface ? Written : Reduce, cancellationToken);
        var target = await Words(toSlug, targetSurface ? Written : Comparable, cancellationToken);
        var addresses = source.Keys.Intersect(target.Keys).OrderBy(a => a).ToList();

        Directory.CreateDirectory(workspace);
        var alignmentFile = await Align(
            fromSlug, toSlug, workspace, modelType, addresses, source, target, cancellationToken);

        var (gold, structural) = await Stated(from.Id, to.Id, statedOnly, cancellationToken);
        var content = gold.Where(pair => !structural.Contains(pair.To)).ToHashSet();
        var prior = await SyntaxPrior.Read(
            (NpgsqlConnection)db.Database.GetDbConnection(), to.Id, cancellationToken);

        var report = new StringBuilder()
            .AppendLine($"{fromSlug} against {toSlug} as " + (targetSurface ? "written" : "reduced") +
                        $", scored on {gold.Count} " + (statedOnly ? "stated" : "stated and lexical") + " pairs")
            .AppendLine("  rule            min  syntax     kept  precision  recall   content precision");

        foreach (var selection in Enum.GetValues<Selection>())
        {
            foreach (var threshold in thresholds)
            {
                foreach (var read in prior.Known ? new SyntaxPrior?[] { null, prior } : [null])
                {
                    var (drafts, _, _, _) =
                        Read(alignmentFile, addresses, source, target, threshold, selection, read);
                    var proposed = drafts.Select(d => (d.SourceWordId, d.TargetWordId)).ToHashSet();
                    var all = Alignment.Score(proposed, gold);
                    var narrow = Alignment.Score(
                        proposed.Where(pair => !structural.Contains(pair.TargetWordId)), content);

                    report.AppendLine(
                        $"  {selection,-14}  {threshold:F2}  {(read is null ? "off" : "on"),-6}  " +
                        $"{drafts.Count,7}  {all.Precision,9:P1}  " +
                        $"{all.Recall,6:P1}  {narrow.Precision,9:P1} of {narrow.Proposed}");
                }
            }
        }

        return report.ToString();
    }

    /// <summary>
    /// The pairs a source states for these two texts, and the Hebrew words that are prefixes or the
    /// object marker rather than words a translation renders on their own.
    /// </summary>
    /// <param name="statedOnly">
    /// Whether to score against the correspondences a file states and nothing else. The wider set
    /// also holds the lexical matches, which are themselves inferred, so scoring against them is
    /// partly a measure of agreement with another guess. Both are worth having: the wider one is
    /// what every earlier measurement of this aligner used, and the narrower one is the claim.
    /// </param>
    private async Task<(HashSet<(long From, long To)> Gold, HashSet<long> Structural)> Stated(
        int fromTextId,
        int toTextId,
        bool statedOnly,
        CancellationToken cancellationToken)
    {
        await db.Database.OpenConnectionAsync(cancellationToken);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();

        var gold = new HashSet<(long, long)>(400_000);
        await using (var command = new NpgsqlCommand(
            """
            SELECT f.word_id, t.word_id
            FROM link l
            JOIN link_word f ON f.link_id = l.id AND f.side = 'from'
            JOIN link_word t ON t.link_id = l.id AND t.side = 'to'
            WHERE l.from_text_id = @from AND l.to_text_id = @to
              AND (l.method = 'stated-by-source' OR (l.method <> 'aligner' AND NOT @stated))
            """, connection))
        {
            command.Parameters.AddWithValue("stated", statedOnly);
            command.Parameters.AddWithValue("from", fromTextId);
            command.Parameters.AddWithValue("to", toTextId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                gold.Add((reader.GetInt64(0), reader.GetInt64(1)));
            }
        }

        var structural = new HashSet<long>(60_000);
        await using (var command = new NpgsqlCommand(
            """
            SELECT id FROM word
            WHERE text_id = @text AND (strong_number LIKE 'H9%' OR strong_number = 'H853')
            """, connection))
        {
            command.Parameters.AddWithValue("text", toTextId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                structural.Add(reader.GetInt64(0));
            }
        }

        return (gold, structural);
    }

    /// <summary>
    /// Reads the tool's Pharaoh output — <c>source-target:translation:alignment</c> per pair — one
    /// verse at a time, with the indices resolved to the words they stand for.
    ///
    /// A pair naming a word the verse does not have is dropped rather than trusted. The tool counts
    /// tokens and this counts words, and where the two disagree the rest of that line is about
    /// somebody else's words.
    /// </summary>
    private static IEnumerable<(List<Word> Source, List<Word> Target, List<Candidate> Pairs)> Parse(
        string path,
        List<(int, int, int)> addresses,
        Dictionary<(int, int, int), List<Word>> source,
        Dictionary<(int, int, int), List<Word>> target)
    {
        var line = 0;

        foreach (var text in File.ReadLines(path))
        {
            if (line >= addresses.Count)
            {
                yield break;
            }

            var address = addresses[line++];
            var sourceWords = source[address];
            var targetWords = target[address];
            var pairs = new List<Candidate>(24);

            foreach (var token in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = token.Split(':');
                var indices = parts[0].Split('-');
                if (indices.Length != 2 ||
                    !int.TryParse(indices[0], out var s) || !int.TryParse(indices[1], out var t) ||
                    s >= sourceWords.Count || t >= targetWords.Count)
                {
                    continue;
                }

                pairs.Add((s, t, Score(parts, 1), Score(parts, 2)));
            }

            yield return (sourceWords, targetWords, pairs);
        }
    }

    /// <summary>
    /// What the model proposed for one pair of texts, with the syntax of the target read over it,
    /// against what a source states. It answers the only question worth asking of a new signal
    /// before it is wired into anything: does knowing this separate the model's right answers from
    /// its wrong ones, and by how much.
    /// </summary>
    public async Task<string> Diagnose(
        string fromSlug,
        string toSlug,
        string workspace,
        string modelType,
        bool statedOnly = false,
        CancellationToken cancellationToken = default)
    {
        var from = await Text(fromSlug, cancellationToken);
        var to = await Text(toSlug, cancellationToken);
        var source = await Words(fromSlug, Reduce, cancellationToken);
        var target = await Words(toSlug, Comparable, cancellationToken);
        var addresses = source.Keys.Intersect(target.Keys).OrderBy(a => a).ToList();

        Directory.CreateDirectory(workspace);
        var alignmentFile = await Align(
            fromSlug, toSlug, workspace, modelType, addresses, source, target, cancellationToken);

        var (gold, _) = await Stated(from.Id, to.Id, statedOnly, cancellationToken);
        var prior = await SyntaxPrior.Read(
            (NpgsqlConnection)db.Database.GetDbConnection(), to.Id, cancellationToken);

        var cohesions = Enum.GetValues<Cohesion>();
        var seen = new int[Bands.Length + 1, cohesions.Length];
        var right = new int[Bands.Length + 1, cohesions.Length];

        foreach (var (sourceWords, targetWords, raw) in Parse(alignmentFile, addresses, source, target))
        {
            var ids = targetWords.Select(word => word.Id).ToList();
            var judged = prior.Judge(raw, ids);

            for (var at = 0; at < raw.Count; at++)
            {
                var band = Band(raw[at].Translation);
                var bucket = (int)judged[at];
                seen[band, bucket]++;
                if (gold.Contains((sourceWords[raw[at].Source].Id, ids[raw[at].Target])))
                {
                    right[band, bucket]++;
                }
            }
        }

        var report = new StringBuilder()
            .AppendLine($"{fromSlug} into {toSlug}, every proposal the model made, against {gold.Count} " +
                        (statedOnly ? "stated" : "stated and lexical") + " pairs")
            .AppendLine("  How often a source agrees, by what the model scored the pair and how the pair sits")
            .AppendLine("  among its neighbours' answers. The last row is the log ratio the rescorer adds to")
            .AppendLine("  the log odds — how much likelier that reading is among the agreed than the rest.")
            .AppendLine()
            .Append($"  {"confidence",-12}");

        foreach (var cohesion in cohesions)
        {
            report.Append($"{cohesion,20}");
        }

        report.AppendLine();

        for (var band = 0; band <= Bands.Length; band++)
        {
            report.Append($"  {Describe(band),-12}");
            foreach (var cohesion in cohesions)
            {
                var at = (int)cohesion;
                var agrees = seen[band, at] == 0 ? 0 : (double)right[band, at] / seen[band, at];
                report.Append($"{agrees,9:P1} of {seen[band, at],-7}");
            }

            report.AppendLine();
        }

        var agreed = Total(right);
        var disagreed = Total(seen) - agreed;
        report.Append($"  {"log ratio",-12}");

        foreach (var cohesion in cohesions)
        {
            var at = (int)cohesion;
            var hit = Column(right, at);
            var wrong = Column(seen, at) - hit;
            report.Append(
                $"{(hit == 0 || wrong == 0 ? 0 : Math.Log((double)hit / agreed / ((double)wrong / disagreed))),20:F3}");
        }

        return report.AppendLine().ToString();
    }

    /// <summary>
    /// Where the model's own confidence sits, so the syntax can be asked whether it says anything
    /// the confidence did not. A signal that only separates the sure pairs from the doubtful ones is
    /// the confidence over again under another name.
    /// </summary>
    private static readonly double[] Bands = [0.25, 0.50, 0.80];

    private static int Band(double confidence)
    {
        var band = 0;
        while (band < Bands.Length && confidence >= Bands[band])
        {
            band++;
        }

        return band;
    }

    private static string Describe(int band) =>
        band == 0 ? $"below {Bands[0]:F2}"
        : band == Bands.Length ? $"{Bands[^1]:F2} and up"
        : $"{Bands[band - 1]:F2} to {Bands[band]:F2}";

    private static int Total(int[,] counts)
    {
        var total = 0;
        foreach (var count in counts)
        {
            total += count;
        }

        return total;
    }

    private static int Column(int[,] counts, int column)
    {
        var total = 0;
        for (var row = 0; row < counts.GetLength(0); row++)
        {
            total += counts[row, column];
        }

        return total;
    }

    /// <summary>
    /// Reads the tool's Pharaoh output and keeps what is worth keeping.
    /// </summary>
    private static (List<AlignedDraft> Drafts, int Proposed, int Collapsed, int Below) Read(
        string path,
        List<(int, int, int)> addresses,
        Dictionary<(int, int, int), List<Word>> source,
        Dictionary<(int, int, int), List<Word>> target,
        double minimumConfidence,
        Selection selection = Selection.All,
        SyntaxPrior? prior = null)
    {
        var drafts = new List<AlignedDraft>(300_000);
        var proposed = 0;
        var collapsed = 0;
        var below = 0;

        foreach (var (sourceWords, targetWords, raw) in Parse(path, addresses, source, target))
        {
            proposed += raw.Count;
            List<Candidate> verse = prior is null
                ? raw
                : prior.Rescore(raw, [.. targetWords.Select(word => word.Id)]);

            var crowded = verse
                .GroupBy(pair => pair.Target)
                .Where(group => group.Count() >= CollapsedCluster && group.All(p => p.Translation < minimumConfidence))
                .Select(group => group.Key)
                .ToHashSet();

            var standing = new List<(int Source, int Target, double Confidence, double Position)>(verse.Count);
            foreach (var pair in verse)
            {
                if (crowded.Contains(pair.Target))
                {
                    collapsed++;
                    continue;
                }

                if (pair.Translation < minimumConfidence)
                {
                    below++;
                    continue;
                }

                standing.Add((pair.Source, pair.Target, pair.Translation, pair.Alignment));
            }

            foreach (var (from, to, confidence, position) in Selections.Apply(
                         selection, standing, [.. targetWords.Select(word => word.Text)]))
            {
                drafts.Add(new AlignedDraft(
                    sourceWords[from].Id, targetWords[to].Id, confidence, position));
            }
        }

        return (drafts, proposed, collapsed, below);
    }

    private static double Score(string[] parts, int index) =>
        parts.Length > index && double.TryParse(parts[index], NumberStyles.Float, CultureInfo.InvariantCulture,
            out var value)
            ? value
            : 0;

    private async Task Store(
        int fromTextId,
        int toTextId,
        string modelType,
        bool syntax,
        List<AlignedDraft> drafts,
        CancellationToken cancellationToken)
    {
        if (drafts.Count == 0)
        {
            return;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();

        var firstId = await ReserveLinkIds(connection, drafts.Count, cancellationToken);
        var renders = EnumSpelling.Of(LinkRelation.Renders);
        var method = EnumSpelling.Of(LinkMethod.Aligner);
        var fromSide = EnumSpelling.Of(LinkSide.From);
        var toSide = EnumSpelling.Of(LinkSide.To);

        // The model reports how likely the word pairing is and how likely the position is. The
        // schema has one confidence, so the pairing is what it holds and the position rides along
        // in the source, where it stays readable rather than being averaged away.
        await using (var writer = await connection.BeginBinaryImportAsync(LinkImport, cancellationToken))
        {
            for (var i = 0; i < drafts.Count; i++)
            {
                await writer.StartRowAsync(cancellationToken);
                await writer.WriteAsync(firstId + i, NpgsqlDbType.Bigint, cancellationToken);
                await writer.WriteAsync(fromTextId, NpgsqlDbType.Integer, cancellationToken);
                await writer.WriteAsync(toTextId, NpgsqlDbType.Integer, cancellationToken);
                await writer.WriteAsync(renders, NpgsqlDbType.Text, cancellationToken);
                await writer.WriteAsync(method, NpgsqlDbType.Text, cancellationToken);
                await writer.WriteAsync(drafts[i].Translation, NpgsqlDbType.Double, cancellationToken);
                await writer.WriteAsync(
                    $"SIL.Machine {modelType}, symmetrised och" +
                    (syntax ? ", rescored on ETCBC phrase and clause structure" : string.Empty) +
                    $", position {drafts[i].Position:F4}",
                    NpgsqlDbType.Text, cancellationToken);
            }

            await writer.CompleteAsync(cancellationToken);
        }

        await using (var writer = await connection.BeginBinaryImportAsync(LinkWordImport, cancellationToken))
        {
            for (var i = 0; i < drafts.Count; i++)
            {
                await Row(writer, firstId + i, drafts[i].SourceWordId, fromSide, cancellationToken);
                await Row(writer, firstId + i, drafts[i].TargetWordId, toSide, cancellationToken);
            }

            await writer.CompleteAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task Row(
        NpgsqlBinaryImporter writer,
        long linkId,
        long wordId,
        string side,
        CancellationToken cancellationToken)
    {
        await writer.StartRowAsync(cancellationToken);
        await writer.WriteAsync(linkId, NpgsqlDbType.Bigint, cancellationToken);
        await writer.WriteAsync(wordId, NpgsqlDbType.Bigint, cancellationToken);
        await writer.WriteAsync(side, NpgsqlDbType.Text, cancellationToken);
    }

    private static async Task<long> ReserveLinkIds(
        NpgsqlConnection connection,
        int count,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT setval(pg_get_serial_sequence('link', 'id'), " +
            "coalesce((SELECT max(id) FROM link), 0) + @count) - @count + 1", connection);
        command.Parameters.AddWithValue("count", count);
        return (long)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private static async Task Machine(string[] arguments, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo("machine") { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)
                            ?? throw new InvalidOperationException("machine did not start.");

        // Both pipes are drained at once. The tool writes a progress bar to standard output, and
        // reading one stream to the end before the other lets that fill its buffer and block the
        // child for ever — which looks exactly like the tool hanging.
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        await Task.WhenAll(errorTask, outputTask);
        await process.WaitForExitAsync(cancellationToken);
        var error = await errorTask;

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"`machine {string.Join(' ', arguments)}` failed with exit code {process.ExitCode}. " +
                $"It is SIL's alignment tool; install it with `dotnet tool install -g SIL.Machine.Tool`. {error}");
        }
    }

    /// <summary>
    /// One verse per line, one token per word, and the tool splits the line on whitespace to get
    /// them back. So a token that is empty or holds a space is not a token the tool will count, and
    /// the indices it returns for that verse are then somebody else's words from that point on.
    /// That is checked here rather than trusted: a shifted alignment is wrong in a way that looks
    /// exactly like a right one.
    /// </summary>
    private static void Write(
        string path,
        List<(int, int, int)> addresses,
        Dictionary<(int, int, int), List<Word>> words)
    {
        var lines = new List<string>(addresses.Count);

        foreach (var address in addresses)
        {
            var verse = words[address];
            var (book, chapter, number) = address;
            lines.Add(AlignmentTokens.Line(verse.Select(w => w.Text), $"{book} {chapter}:{number}"));
        }

        File.WriteAllLines(path, lines);
    }

    private async Task<Database.Entities.Text> Text(string slug, CancellationToken cancellationToken) =>
        await db.Texts.SingleOrDefaultAsync(t => t.Slug == slug, cancellationToken)
        ?? throw new InvalidOperationException($"There is no text \"{slug}\".");

    private async Task<Dictionary<(int, int, int), List<Word>>> Words(
        string slug,
        Func<WordForms, string> form,
        CancellationToken cancellationToken)
    {
        var rows = await db.VerseReferences
            .Where(r => r.IsPrimary && r.Verse!.Text!.Slug == slug)
            .SelectMany(r => r.Verse!.Words.Select(w => new
            {
                r.CanonicalBook,
                r.CanonicalChapter,
                r.CanonicalVerse,
                w.Position,
                w.Id,
                w.Surface,
                w.Lemma,
                w.StrongNumber,
                Language = w.Text!.Language,
                Consonantal = w.Morphology == null ? null : w.Morphology.RootElement.GetProperty("consonantal")
                    .GetString(),
            }))
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(r => (r.CanonicalBook, r.CanonicalChapter, r.CanonicalVerse))
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(r => r.Position)
                    .Select(r => new Word(
                        r.Id,
                        AlignmentTokens.One(form(
                            new WordForms(
                                r.Surface, r.Lemma, r.Consonantal, r.StrongNumber, r.Language, r.Position)))))
                    .ToList());
    }

    /// <summary>
    /// The word as the text writes it, except where the language inflects so heavily that writing
    /// it that way tells the model nothing. Greek and Hebrew carry a lemma of their own; Russian and
    /// Ukrainian carry none, so one is computed.
    /// </summary>
    private static string Written(WordForms word) => word.Surface.ToLowerInvariant();

    /// <summary>
    /// Whatever form of this word pools the most evidence. Both sides have to be reduced together
    /// or neither: reducing one alone leaves its word facing several forms of the other and splits
    /// the evidence it was meant to gather.
    /// </summary>
    private static string Reduce(WordForms word) => word.Language switch
    {
        "rus" or "ukr" => SlavicStemmer.Stem(word.Surface, IsName(word)),
        "eng" => EnglishStemmer.Stem(word.Surface),
        "grc" => GreekStemmer.Stem(word.Surface),
        _ => word.Surface.ToLowerInvariant(),
    };

    /// <summary>
    /// A capitalised word that does not open its verse. Every language here capitalises its proper
    /// names and nothing else mid-sentence, so this is where the names are — and it is worth knowing
    /// because a name inflects as a noun and never as a verb. The first word is excluded because a
    /// verb opening a verse is capitalised too, and <em>Сказав</em> must stem where
    /// <em>сказав</em> does.
    /// </summary>
    private static bool IsName(WordForms word) =>
        word.Position > 1 && word.Surface.Length > 0 && char.IsUpper(word.Surface[0]);

    /// <summary>
    /// The form a model can learn from. BHSA writes full vowel pointing, so the same word appears as
    /// many different strings and a lexicon built on twenty-three thousand verses never sees any of
    /// them often enough; using the consonants instead raised precision by a quarter.
    /// </summary>
    private static string Comparable(WordForms word) =>
        word.Language is "rus" or "ukr" or "eng" ? Reduce(word)
        // A Greek witness with a lemma keeps it, and a word without one is reduced like any other
        // heavily inflected language rather than counted as eight words for one. Brenton had none
        // at all until GLAUx; it now has one on 97.1% of its words, so this is per word rather than
        // per text and the remaining 3% still fall to the stemmer.
        : word.Language is "grc" && string.IsNullOrWhiteSpace(word.Lemma) ? Reduce(word)
        : !string.IsNullOrWhiteSpace(word.Consonantal) ? word.Consonantal
        : !string.IsNullOrWhiteSpace(word.Lemma) ? word.Lemma
        : !string.IsNullOrWhiteSpace(word.Surface) ? word.Surface.ToLowerInvariant()
        : word.Strong ?? string.Empty;

    /// <param name="Strong">
    /// The last resort, and the only form a zero morpheme has.
    /// </param>
    /// <param name="Language">
    /// What the text is written in, which decides whether a form has to be reduced before a model
    /// can learn anything from it.
    /// </param>
    /// <param name="Position">
    /// Where it stands in its verse, which is how a proper name is told from a verb that happens to
    /// open a sentence.
    /// </param>
    private sealed record WordForms(
        string Surface,
        string? Lemma,
        string? Consonantal,
        string? Strong,
        string? Language,
        int Position);

    private sealed record Word(long Id, string Text);

    private sealed record AlignedDraft(long SourceWordId, long TargetWordId, double Translation, double Position);
}
