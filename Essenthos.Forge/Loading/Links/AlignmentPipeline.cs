using Microsoft.EntityFrameworkCore.Storage;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Essenthos.Core.BetaMasaheft;
using Essenthos.Core.Corpus;
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
    /// Measured with <c>score KJV BHSA</c> against the 625,826 correspondences the mapping file
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

    /// <param name="minimumConfidence">
    /// The threshold, or null for the one measured for the source's language, and
    /// <see cref="DefaultMinimumConfidence"/> where none was.
    /// </param>
    /// <param name="outsideSlug">
    /// A text whose verses the source was not translated alongside the target, or null. The model
    /// still trains on every verse the pair shares, and only the source's words standing where this
    /// text has no verse are written: the King James's Apocrypha was translated from the Greek and
    /// the rest of its Old Testament from the Hebrew, so against the Septuagint it renders the one
    /// and only resembles the other. Lettered verses are read too, because every address written is
    /// one the frame gives a Greek addition by the versification data's rule for it.
    /// </param>
    public async Task<AlignmentOutcome> Run(
        string fromSlug,
        string toSlug,
        string workspace,
        double? minimumConfidence,
        string modelType,
        bool replace = false,
        string? outsideSlug = null,
        CancellationToken cancellationToken = default)
    {
        var from = await Text(fromSlug, cancellationToken);
        var to = await Text(toSlug, cancellationToken);

        if (!replace && await db.Links.AnyAsync(
                l => l.FromTextId == from.Id && l.ToTextId == to.Id && l.Method == LinkMethod.Aligner,
                cancellationToken))
        {
            logger.LogInformation(
                "{From} and {To} are already aligned; nothing to do. Add --replace to align them again", fromSlug, toSlug);
            return new AlignmentOutcome(fromSlug, toSlug, 0, 0, 0, 0, 0, TimeSpan.Zero);
        }

        var started = Stopwatch.StartNew();
        Directory.CreateDirectory(workspace);

        var measured = Measured.GetValueOrDefault(from.Language ?? string.Empty);
        var threshold = minimumConfidence ?? measured?.Minimum ?? DefaultMinimumConfidence;
        var pool = Pool(fromSlug, toSlug);
        var lettered = outsideSlug is not null || await PrintTheSameVerses(fromSlug, toSlug, cancellationToken);
        var source = await Words(fromSlug, word => Reduce(word), cancellationToken, lettered: lettered);
        var target = await Words(
            toSlug, pool is null ? word => Comparable(word) : Pooled, cancellationToken, primaryOnly: pool is not null,
            lettered: lettered);
        var refused = await VerseLinkLoader.Refused(db, fromSlug, toSlug, cancellationToken);
        source = Unmet(source, refused);
        target = Unmet(target, refused);
        var addresses = Shared(fromSlug, toSlug, source, target);
        if (addresses.Count == 0)
        {
            throw new InvalidOperationException(
                $"\"{fromSlug}\" and \"{toSlug}\" share no verse in the canonical frame. Both texts have to be " +
                "loaded and placed before they can be aligned.");
        }

        var alignmentFile = pool is null
            ? await Align(fromSlug, toSlug, workspace, modelType, addresses, source, target, cancellationToken)
            : await AlignPooled(fromSlug, pool, workspace, modelType, addresses, source, target, cancellationToken);

        var prior = await Syntax(to.Id, cancellationToken);

        var (drafts, proposed, collapsed, below) = Read(
            alignmentFile, addresses, source, target, threshold, Selection.BestPerSource, prior,
            MarksNoNames(from.Language), BothMarkTheirNames(from.Language, to.Language));
        if (outsideSlug is not null)
        {
            var beyond = await Beyond(from.Id, (await Text(outsideSlug, cancellationToken)).Id, cancellationToken);
            drafts = [.. drafts.Where(draft => beyond.Contains(draft.SourceWordId))];
        }

        var note = pool is null ? null : $"one model over {string.Join(", ", pool)}";
        if (measured is not null && threshold == measured.Minimum)
        {
            note = note is null ? measured.Note : $"{note}; {measured.Note}";
        }

        await Store(from.Id, to.Id, modelType, prior.Known, drafts, replace, cancellationToken, note);

        var outcome = new AlignmentOutcome(
            fromSlug, toSlug, addresses.Count, proposed, collapsed, below, drafts.Count, started.Elapsed);
        logger.LogInformation("Aligned {Outcome}", outcome);
        return outcome;
    }

    /// <summary>
    /// The words of a text standing, by their verse's own address, where another text has no verse
    /// at any address.
    /// </summary>
    private async Task<HashSet<long>> Beyond(int textId, int outsideTextId, CancellationToken cancellationToken)
    {
        await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var command = new NpgsqlCommand(
                """
                SELECT w.id
                FROM word w
                JOIN verse_reference r ON r.verse_id = w.verse_id AND r.is_primary
                WHERE w.text_id = @text
                  AND NOT EXISTS (
                      SELECT 1
                      FROM verse_reference o
                      JOIN verse v ON v.id = o.verse_id AND v.text_id = @outside
                      WHERE (o.canonical_book, o.canonical_chapter, o.canonical_verse)
                          = (r.canonical_book, r.canonical_chapter, r.canonical_verse))
                """, (NpgsqlConnection)db.Database.GetDbConnection());
            command.Parameters.AddWithValue("text", textId);
            command.Parameters.AddWithValue("outside", outsideTextId);

            var words = new HashSet<long>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                words.Add(reader.GetInt64(0));
            }

            return words;
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    /// <summary>
    /// The target text's clause and phrase structure. The connection is opened because SyntaxPrior
    /// reads it with Npgsql directly, and closed here because three callers opened one and none of
    /// them closed it.
    /// </summary>
    private async Task<SyntaxPrior> Syntax(int textId, CancellationToken cancellationToken)
    {
        await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            return await SyntaxPrior.Read(
                (NpgsqlConnection)db.Database.GetDbConnection(), textId, cancellationToken);
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    /// <summary>
    /// Trains the model and runs it, unless the workspace already holds the answer. Training the
    /// twenty-three thousand verse pairs takes minutes, and the threshold is a question about how to
    /// read the output rather than how to produce it, so a measurement sweep reuses one run.
    ///
    /// <para>
    /// An answer is reused only for the verses it was made from. The output says nothing about which
    /// verse a line is, so it is read against the verses the corpus shares now, line by line; one
    /// made while a text lacked the Psalms put each verse's pairs on the verse 2,461 lines earlier
    /// from Psalm 1:1 to the end, and left the last 2,461 with none. So the inputs are written out
    /// again and compared with the ones the answer was trained on, and anything that differs trains
    /// afresh.
    /// </para>
    /// </summary>
    private async Task<string> Align(
        string fromSlug,
        string toSlug,
        string workspace,
        string modelType,
        List<(int, int, int)> addresses,
        Dictionary<(int, int, int), List<Word>> source,
        Dictionary<(int, int, int), List<Word>> target,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<(int Book, int Chapter, int Verse), List<(int Source, int Target)>>? anchors = null,
        bool shareAnchorTokens = false)
    {
        var sourceFile = Path.Combine(workspace, "source.txt");
        var targetFile = Path.Combine(workspace, "target.txt");
        var anchorFile = Path.Combine(workspace, "anchors.txt");
        var alignmentFile = Path.Combine(workspace, "alignment", "pharaoh.txt");
        var modelPrefix = Path.Combine(workspace, "model", $"{fromSlug}-{toSlug}");

        var shared = shareAnchorTokens && anchors is not null
            ? SharedAnchorTokens(source, target, anchors)
            : null;
        var inputs = new List<(string Path, string[] Lines)>
        {
            (sourceFile, Lines(addresses, source, shared?.Source)),
            (targetFile, Lines(addresses, target, shared?.Target)),
        };
        if (anchors is not null)
        {
            inputs.Add((anchorFile, AnchorLines(addresses, anchors)));
        }

        if (File.Exists(alignmentFile))
        {
            if (Unchanged(inputs))
            {
                logger.LogInformation("Reusing the alignment already in {Workspace}", workspace);
                return alignmentFile;
            }

            logger.LogWarning(
                "The alignment in {Workspace} was made from other verses than {From} and {To} share now; training again",
                workspace, fromSlug, toSlug);
            Directory.Delete(Path.GetDirectoryName(alignmentFile)!, recursive: true);
            if (Directory.Exists(Path.GetDirectoryName(modelPrefix)))
            {
                Directory.Delete(Path.GetDirectoryName(modelPrefix)!, recursive: true);
            }
        }

        Directory.CreateDirectory(workspace);
        foreach (var (path, lines) in inputs)
        {
            File.WriteAllLines(path, lines);
        }
        Directory.CreateDirectory(Path.GetDirectoryName(alignmentFile)!);
        Directory.CreateDirectory(Path.GetDirectoryName(modelPrefix)!);

        logger.LogInformation("Training {Model} over {Verses} verse pairs", modelType, addresses.Count);
        var train = new List<string> { "train", "alignment-model", "-mt", modelType };
        if (anchors is not null)
        {
            train.Add("-a");
            train.Add(anchorFile);
        }

        train.Add(modelPrefix);
        train.Add(sourceFile);
        train.Add(targetFile);
        await Machine([.. train], cancellationToken);
        await Machine(
            ["align", "-mt", modelType, "-sh", "och", "-s", modelPrefix, sourceFile, targetFile, alignmentFile],
            cancellationToken);

        return alignmentFile;
    }

    /// <summary>
    /// The texts a source is aligned against with one model, where it has such a set, and null where
    /// each pair trains its own. The Ge'ez was translated from the Greek in both testaments, so its
    /// Old Testament against the two Septuagints and its New against the two Greek New Testaments
    /// are one language pair four times over, and a word the Octateuch teaches is known in the
    /// Gospels.
    /// </summary>
    private static IReadOnlyList<string>? Pool(string fromSlug, string toSlug) =>
        fromSlug == GeezTextSource.Slug && GeezTextSource.AlignedWith.Contains(toSlug)
            ? GeezTextSource.AlignedWith
            : null;

    /// <summary>
    /// The addresses both texts hold words at, in order, less the ones the source says not to align
    /// against this target.
    /// </summary>
    private static List<(int, int, int)> Shared(
        string fromSlug,
        string toSlug,
        Dictionary<(int, int, int), List<Word>> source,
        Dictionary<(int, int, int), List<Word>> target) =>
        [
            .. source.Keys.Intersect(target.Keys)
                .Where(address => fromSlug != GeezTextSource.Slug
                                  || GeezTextSource.Aligns(toSlug, address.Item1, address.Item2, address.Item3))
                .OrderBy(address => address),
        ];

    /// <summary>
    /// A Greek word as every text of a pool can spell it. Nestle and Brenton carry lemmas and Swete
    /// and the Byzantine text carry none, and a model that met λόγος in one half of its data and
    /// λογ in the other would split what it learned of one word between two; so the whole pool reads
    /// the stem.
    /// </summary>
    private static string Pooled(WordForms word) => GreekStemmer.Stem(word.Surface);

    /// <summary>
    /// Aligns one pair of a pool with the model the whole pool trains, training it first unless the
    /// pool's inputs are the ones it was trained on. The pair's own answer is reused on the same
    /// terms as <see cref="Align"/>'s, and also only while the model it came from is still the one
    /// standing.
    /// </summary>
    private async Task<string> AlignPooled(
        string fromSlug,
        IReadOnlyList<string> pool,
        string workspace,
        string modelType,
        List<(int, int, int)> addresses,
        Dictionary<(int, int, int), List<Word>> source,
        Dictionary<(int, int, int), List<Word>> target,
        CancellationToken cancellationToken)
    {
        var (modelPrefix, stamp) = await PoolModel(fromSlug, pool, modelType, source, cancellationToken);

        var sourceFile = Path.Combine(workspace, "source.txt");
        var targetFile = Path.Combine(workspace, "target.txt");
        var stampFile = Path.Combine(workspace, "model.txt");
        var alignmentFile = Path.Combine(workspace, "alignment", "pharaoh.txt");
        var inputs = new List<(string Path, string[] Lines)>
        {
            (sourceFile, Lines(addresses, source)),
            (targetFile, Lines(addresses, target)),
            (stampFile, [stamp]),
        };

        if (File.Exists(alignmentFile) && Unchanged(inputs))
        {
            logger.LogInformation("Reusing the alignment already in {Workspace}", workspace);
            return alignmentFile;
        }

        Directory.CreateDirectory(workspace);
        foreach (var (path, lines) in inputs)
        {
            File.WriteAllLines(path, lines);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(alignmentFile)!);
        logger.LogInformation("Aligning {Verses} verse pairs with the model {Pool} trained", addresses.Count,
            string.Join(", ", pool));
        await Machine(
            ["align", "-mt", modelType, "-sh", "och", "-s", modelPrefix, sourceFile, targetFile, alignmentFile],
            cancellationToken);
        return alignmentFile;
    }

    /// <summary>
    /// The model a pool trains over every verse its source shares with any text of it, and the
    /// stamp that names this training of it.
    /// </summary>
    private async Task<(string Prefix, string Stamp)> PoolModel(
        string fromSlug,
        IReadOnlyList<string> pool,
        string modelType,
        Dictionary<(int, int, int), List<Word>> source,
        CancellationToken cancellationToken)
    {
        var workspace = Path.Combine(Path.GetTempPath(), "essenthos-align", $"{fromSlug}-{string.Join('-', pool)}");
        var sourceFile = Path.Combine(workspace, "source.txt");
        var targetFile = Path.Combine(workspace, "target.txt");
        var stampFile = Path.Combine(workspace, "trained.txt");
        var modelPrefix = Path.Combine(workspace, "model", fromSlug);

        var sourceLines = new List<string>();
        var targetLines = new List<string>();
        foreach (var partner in pool)
        {
            var target = await Words(partner, Pooled, cancellationToken, primaryOnly: true);
            var addresses = Shared(fromSlug, partner, source, target);
            sourceLines.AddRange(Lines(addresses, source));
            targetLines.AddRange(Lines(addresses, target));
        }

        var inputs = new List<(string Path, string[] Lines)>
        {
            (sourceFile, [.. sourceLines]),
            (targetFile, [.. targetLines]),
        };

        if (File.Exists(stampFile) && Unchanged(inputs))
        {
            logger.LogInformation("Reusing the model already in {Workspace}", workspace);
            return (modelPrefix, File.ReadAllText(stampFile).Trim());
        }

        if (Directory.Exists(workspace))
        {
            Directory.Delete(workspace, recursive: true);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(modelPrefix)!);
        foreach (var (path, lines) in inputs)
        {
            File.WriteAllLines(path, lines);
        }

        logger.LogInformation("Training {Model} over {Verses} verse pairs of {From} against {Pool}",
            modelType, sourceLines.Count, fromSlug, string.Join(", ", pool));
        await Machine(["train", "alignment-model", "-mt", modelType, modelPrefix, sourceFile, targetFile],
            cancellationToken);

        var stamp = Guid.NewGuid().ToString("N");
        await File.WriteAllTextAsync(stampFile, stamp, cancellationToken);
        return (modelPrefix, stamp);
    }

    /// <summary>
    /// The threshold measured for a source language that has no stated correspondences to measure
    /// against, and what the link's source says about how it was measured.
    /// </summary>
    private sealed record MeasuredThreshold(double Minimum, string Note);

    private static readonly Dictionary<string, MeasuredThreshold> Measured = new()
    {
        // Hand-scored by Claude on a random sample of 200 of the Ge'ez-Greek pairs, 40 per band of
        // confidence, against Brenton and Nestle (2026-09-25), with the three books the Ge'ez divides
        // within chapters left out: right 66% from 0.10, 68% from 0.25, 89% from 0.40, 95% from 0.60
        // and 97% from 0.80. There is no stated Ge'ez-Greek correspondence to score against.
        ["gez"] = new(0.4, "threshold 0.40 from a hand-scored sample, about 90% right above it"),
    };

    /// <summary>A script with no capital letters, whose words never say that they are names.</summary>
    private static bool MarksNoNames(string? language) => language is "gez";

    /// <summary>
    /// The Hebrew against a Greek edition, which both mark every name they print — BHSA by its part of
    /// speech, the printed Greek by the capital — so that a word unmarked on either side is not a name.
    /// Two Greek texts are not: a manuscript transcription writes its names as it writes every word.
    /// </summary>
    internal static bool BothMarkTheirNames(string? one, string? other) =>
        (one, other) is ("hbo", "grc") or ("grc", "hbo");

    /// <summary>
    /// What a text reads as its placement where the frame's is not the one to align by: the Ge'ez
    /// books the church divides its own way stand at their own numbers in the frame, which are the
    /// Greek's numbers for other passages.
    /// </summary>
    private static (IReadOnlySet<int> Books,
        IReadOnlyDictionary<(int Book, int Chapter, int Verse), IReadOnlyList<(int Book, int Chapter, int Verse)>> Addresses)?
        VerseMap(string slug) =>
        slug == GeezTextSource.Slug ? (GeezVerseMap.Books, GeezVerseMap.Addresses) : null;

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
        IReadOnlySet<int>? books = null,
        CancellationToken cancellationToken = default)
    {
        // Only the source is read as written. The target's own reduction is not a hedge — BHSA's
        // consonantal text and Nestle's lemmas are the forms those texts themselves carry, and both
        // were measured as plainly better than the pointing and the inflection they replace.
        // A trial over a few books trains a model of its own rather than reading the pool's.
        var pool = asWritten || books is not null ? null : Pool(fromSlug, toSlug);
        var lettered = await PrintTheSameVerses(fromSlug, toSlug, cancellationToken);
        var source = await Words(
            fromSlug, asWritten ? Written : word => Reduce(word), cancellationToken, books, lettered: lettered);
        var target = await Words(
            toSlug, pool is null ? word => Comparable(word) : Pooled, cancellationToken, books, pool is not null,
            lettered);
        var refused = await VerseLinkLoader.Refused(db, fromSlug, toSlug, cancellationToken);
        source = Unmet(source, refused);
        target = Unmet(target, refused);
        var addresses = Shared(fromSlug, toSlug, source, target);

        Directory.CreateDirectory(workspace);
        var alignmentFile = pool is null
            ? await Align(fromSlug, toSlug, workspace, modelType, addresses, source, target, cancellationToken)
            : await AlignPooled(fromSlug, pool, workspace, modelType, addresses, source, target, cancellationToken);

        var prior = await Syntax((await Text(toSlug, cancellationToken)).Id, cancellationToken);
        var from = await Text(fromSlug, cancellationToken);
        var to = await Text(toSlug, cancellationToken);

        var (drafts, _, _, _) = Read(
            alignmentFile, addresses, source, target, floor, selection, prior, MarksNoNames(from.Language),
            BothMarkTheirNames(from.Language, to.Language));
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
    /// <param name="pairsFile">
    /// Where to write the pairs an alignment run would store, one per line (source word, target
    /// word, translation and position probability), so another method can be scored against the
    /// same answer key word for word, or given the aligner as its fallback.
    /// </param>
    public async Task<string> Measure(
        string fromSlug,
        string toSlug,
        string workspace,
        IReadOnlyList<double> thresholds,
        string modelType,
        bool targetSurface = false,
        bool statedOnly = false,
        bool suppletion = false,
        string? pairsFile = null,
        CancellationToken cancellationToken = default)
    {
        var from = await Text(fromSlug, cancellationToken);
        var to = await Text(toSlug, cancellationToken);
        var lettered = await PrintTheSameVerses(fromSlug, toSlug, cancellationToken);
        var source = await Words(
            fromSlug, targetSurface ? Written : word => Reduce(word, suppletion), cancellationToken, lettered: lettered);
        var target = await Words(
            toSlug, targetSurface ? Written : word => Comparable(word, suppletion), cancellationToken, lettered: lettered);
        var addresses = source.Keys.Intersect(target.Keys).OrderBy(a => a).ToList();

        Directory.CreateDirectory(workspace);
        var alignmentFile = await Align(
            fromSlug, toSlug, workspace, modelType, addresses, source, target, cancellationToken);

        var (gold, structural) = await Stated(from.Id, to.Id, statedOnly, cancellationToken);
        var content = gold.Where(pair => !structural.Contains(pair.To)).ToHashSet();
        var prior = await SyntaxPrior.Read(
            (NpgsqlConnection)db.Database.GetDbConnection(), to.Id, cancellationToken);

        // A file that states correspondences for nine books of sixty-six marks every proposal made
        // in the other fifty-seven wrong, and the precision column then reports the file's coverage
        // rather than the model's accuracy — 4% where the model is not 4% wrong. Restricting it to
        // the source words the file actually speaks about is the only question a partial gold can
        // answer, and where the file covers everything the two columns are the same number.
        var answered = gold.Select(pair => pair.From).ToHashSet();

        var report = new StringBuilder()
            .AppendLine($"{fromSlug} against {toSlug} as " +
                        (targetSurface ? "written" : suppletion ? "reduced, suppletion on" : "reduced") +
                        $", scored on {gold.Count} " + (statedOnly ? "stated" : "stated and lexical") +
                        $" pairs over {answered.Count} source words")
            .AppendLine(
                "  rule            min  syntax     kept  precision  recall   content precision" +
                "   where the source answers");

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
                    var decided = Alignment.Score(
                        proposed.Where(pair => answered.Contains(pair.SourceWordId)), gold);

                    report.AppendLine(
                        $"  {selection,-14}  {threshold:F2}  {(read is null ? "off" : "on"),-6}  " +
                        $"{drafts.Count,7}  {all.Precision,9:P1}  " +
                        $"{all.Recall,6:P1}  {narrow.Precision,9:P1} of {narrow.Proposed}" +
                        $"  {decided.Precision,9:P1}, {decided.Hit} of {decided.Proposed}");
                }
            }
        }

        if (pairsFile is not null)
        {
            var (stored, _, _, _) = Read(
                alignmentFile, addresses, source, target, DefaultMinimumConfidence, Selection.BestPerSource, prior,
                MarksNoNames(from.Language), BothMarkTheirNames(from.Language, to.Language));
            await File.WriteAllLinesAsync(
                pairsFile,
                stored.Select(draft => string.Join('\t', draft.SourceWordId, draft.TargetWordId,
                    draft.Translation.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture),
                    draft.Position.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture))),
                cancellationToken);
            report.AppendLine($"{stored.Count:N0} pairs as an alignment run would store them written to {pairsFile}");
        }

        return report.ToString();
    }

    /// <summary>
    /// Measures partial-alignment training without giving the score its answers. Four fifths of
    /// the usable stated and Strong-number correspondences are supplied to the trainer; a
    /// deterministic fifth of the verses is held out and source-stated pairs there are the only
    /// gold used in the report. The model still sees the two texts of every verse, which is ordinary
    /// semi-supervised alignment, but it receives no fixed pair from a held-out verse.
    /// </summary>
    public async Task<string> MeasureAnchors(
        string fromSlug,
        string toSlug,
        string workspace,
        IReadOnlyList<double> thresholds,
        string modelType,
        int heldOutFold = 0,
        CancellationToken cancellationToken = default)
    {
        var from = await Text(fromSlug, cancellationToken);
        var to = await Text(toSlug, cancellationToken);
        var lettered = await PrintTheSameVerses(fromSlug, toSlug, cancellationToken);
        var source = await Words(fromSlug, word => Reduce(word), cancellationToken, lettered: lettered);
        var target = await Words(toSlug, word => Comparable(word), cancellationToken, lettered: lettered);
        var addresses = source.Keys.Intersect(target.Keys).OrderBy(address => address).ToList();
        var (gold, _) = await Stated(from.Id, to.Id, statedOnly: true, cancellationToken);
        var stated = await StatedAnchors(from.Id, to.Id, cancellationToken);
        var (anchors, heldOut, candidates) = SplitAnchors(source, target, stated, gold, heldOutFold);

        if (anchors.Sum(entry => entry.Value.Count) == 0 || heldOut.Count == 0)
        {
            throw new InvalidOperationException(
                $"{fromSlug} and {toSlug} have no usable held-out stated anchors. The measurement needs " +
                "one-to-one source mappings spanning both the training and test verses.");
        }

        Directory.CreateDirectory(workspace);
        var baseline = await Align(
            fromSlug, toSlug, Path.Combine(workspace, "baseline"), modelType, addresses, source, target,
            cancellationToken);
        var constrained = await Align(
            fromSlug, toSlug, Path.Combine(workspace, "anchored"), modelType, addresses, source, target,
            cancellationToken, anchors);
        var shared = await Align(
            fromSlug, toSlug, Path.Combine(workspace, "shared-anchor-tokens"), modelType, addresses, source, target,
            cancellationToken, anchors, shareAnchorTokens: true);

        var prior = await Syntax(to.Id, cancellationToken);
        var report = new StringBuilder()
            .AppendLine($"{fromSlug} into {toSlug}, held-out fold {heldOutFold} of partial-alignment training")
            .AppendLine(
                $"  {candidates} unambiguous stated-or-Strong pairs; {anchors.Sum(entry => entry.Value.Count)} in training, " +
                $"{heldOut.Count} stated pairs in the held-out fifth of verses")
            .AppendLine("  model          min   all kept  tested      hit  precision   recall     AER");

        var answered = heldOut.Select(pair => pair.From).ToHashSet();

        foreach (var threshold in thresholds)
        {
            foreach (var (label, path) in new[]
                     {
                         ("baseline", baseline),
                         ("partial", constrained),
                         ("shared-token", shared),
                     })
            {
                var (drafts, _, _, _) = Read(
                    path, addresses, source, target, threshold, Selection.BestPerSource, prior);
                var tested = drafts.Where(draft => answered.Contains(draft.SourceWordId)).ToList();
                var score = Alignment.Score(
                    tested.Select(draft => (draft.SourceWordId, draft.TargetWordId)), heldOut);
                report.AppendLine(
                    $"  {label,-12}{threshold,6:F2}{drafts.Count,11}{tested.Count,8}{score.Hit,9}{score.Precision,11:P3}  " +
                    $"{score.Recall,7:P3}  {score.AlignmentErrorRate,6:F3}");
            }
        }

        return report.ToString();
    }

    private static (
        Dictionary<(int Book, int Chapter, int Verse), List<(int Source, int Target)>> Training,
        HashSet<(long From, long To)> HeldOut,
        int Candidates)
        SplitAnchors(
            Dictionary<(int Book, int Chapter, int Verse), List<Word>> source,
        Dictionary<(int Book, int Chapter, int Verse), List<Word>> target,
        HashSet<(long From, long To)> stated,
        HashSet<(long From, long To)> gold,
        int heldOutFold)
    {
        var sourceAt = Positions(source);
        var targetAt = Positions(target);
        var candidates = new List<AnchorCandidate>(stated.Count);

        foreach (var (from, to) in stated)
        {
            if (sourceAt.TryGetValue(from, out var lefts)
                && targetAt.TryGetValue(to, out var rights)
                && lefts.SelectMany(left => rights.Where(right => right.Address == left.Address)
                    .Select(right => new AnchorCandidate(left.Address, left.Position, right.Position)))
                    .FirstOrDefault() is { } candidate)
            {
                candidates.Add(candidate);
            }
        }

        // A fixed source token with two fixed targets is not an anchor but a contradiction. The
        // source can state a phrase as a correspondence, yet partial EM needs a single position
        // on each side; hold those true-but-nonatomic claims out of its input rather than making a
        // choice the source did not make.
        var unambiguous = candidates
            .GroupBy(candidate => candidate.Address)
            .SelectMany(group => group
                .Where(candidate => group.Count(other => other.Source == candidate.Source) == 1
                                    && group.Count(other => other.Target == candidate.Target) == 1))
            .ToList();
        var training = unambiguous
            .Where(candidate => !HeldOut(candidate.Address, heldOutFold))
            .GroupBy(candidate => candidate.Address)
            .ToDictionary(
                group => group.Key,
                group => group.Select(candidate => (candidate.Source, candidate.Target)).ToList());
        var heldOut = gold
            .Where(pair => sourceAt.TryGetValue(pair.From, out var lefts)
                           && targetAt.TryGetValue(pair.To, out var rights)
                && lefts.Any(left => HeldOut(left.Address, heldOutFold)
                                                && rights.Any(right => right.Address == left.Address)))
            .ToHashSet();

        return (training, heldOut, unambiguous.Count);
    }

    private static Dictionary<long, List<WordPosition>> Positions(
        Dictionary<(int Book, int Chapter, int Verse), List<Word>> words) => words
        .SelectMany(entry => entry.Value.Select((word, position) => new
        {
            word.Id,
            Address = entry.Key,
            Position = position,
        }))
        .GroupBy(entry => entry.Id)
        .ToDictionary(
            group => group.Key,
            group => group.Select(entry => new WordPosition(entry.Address, entry.Position)).ToList());

    /// <summary>A stable one-in-five verse split, not <see cref="HashCode"/>, which is salted per process.</summary>
    private static bool HeldOut((int Book, int Chapter, int Verse) address, int fold) =>
        (address.Book * 17 + address.Chapter * 13 + address.Verse) % 5 == fold;

    private async Task<HashSet<(long From, long To)>> StatedAnchors(
        int fromTextId,
        int toTextId,
        CancellationToken cancellationToken)
    {
        await db.Database.OpenConnectionAsync(cancellationToken);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        var anchors = new HashSet<(long, long)>();
        await using var command = new NpgsqlCommand(
            """
            SELECT f.word_id, t.word_id
            FROM link l
            JOIN link_word f ON f.link_id = l.id AND f.side = 'from'
            JOIN link_word t ON t.link_id = l.id AND t.side = 'to'
            WHERE l.from_text_id = @from AND l.to_text_id = @to
              AND l.method IN ('stated-by-source', 'strong-number')
            """,
            connection);
        command.Parameters.AddWithValue("from", fromTextId);
        command.Parameters.AddWithValue("to", toTextId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            anchors.Add((reader.GetInt64(0), reader.GetInt64(1)));
        }

        return anchors;
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
    /// Neither holds EVIDENTIA's rule-based links, which were never part of the wider one.
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
              AND (l.method = 'stated-by-source' OR (l.method NOT IN ('aligner', 'rule-based') AND NOT @stated))
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
                throw Misaligned(path, addresses.Count);
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

        if (line != addresses.Count)
        {
            throw Misaligned(path, addresses.Count);
        }
    }

    private static InvalidOperationException Misaligned(string path, int verses) =>
        new($"{path} does not hold one line for each of the {verses} verses the two texts share, so its lines " +
            "cannot be matched to verses and nothing was written from it. Delete its workspace and run again.");

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
        var lettered = await PrintTheSameVerses(fromSlug, toSlug, cancellationToken);
        var source = await Words(fromSlug, word => Reduce(word), cancellationToken, lettered: lettered);
        var target = await Words(toSlug, word => Comparable(word), cancellationToken, lettered: lettered);
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
        SyntaxPrior? prior = null,
        bool namesUnmarked = false,
        bool refuseStrays = false)
    {
        var drafts = new List<AlignedDraft>(300_000);

        // Where a verse covers two canonical addresses on both sides at once, the same two words
        // are offered to the model twice and it answers twice. That is one claim about one pair of
        // words, and writing it as two links is the shape the corpus check calls two facts about
        // the same words — so the louder answer stands and the other is dropped here rather than
        // reaching the database.
        var at = new Dictionary<(long From, long To), int>(300_000);
        var proposed = 0;
        var collapsed = 0;
        var below = 0;
        var unwritten = 0;

        foreach (var (sourceWords, targetWords, raw) in Parse(path, addresses, source, target))
        {
            proposed += raw.Count;
            IReadOnlyList<string?> targetNames = [.. targetWords.Select(word => word.Name)];
            List<Candidate> verse = NameLists.Settle(
                prior is null ? raw : prior.Rescore(raw, [.. targetWords.Select(word => word.Id)]),
                namesUnmarked
                    ? NameLists.Unmarked([.. sourceWords.Select(word => word.Letters)], targetNames)
                    : [.. sourceWords.Select(word => word.Name)],
                targetNames,
                [.. targetWords.Select(word => word.Letters)],
                refuseStrays);

            var crowded = verse
                .GroupBy(pair => pair.Target)
                .Where(group => group.Count() >= CollapsedCluster && group.All(p => p.Translation < minimumConfidence))
                .Select(group => group.Key)
                .ToHashSet();

            var standing = new List<(int Source, int Target, double Confidence, double Position)>(verse.Count);
            foreach (var pair in verse)
            {
                // A word that writes no token has nothing for a model to learn from, and every such
                // word in a text is the same string to it — so they pool into one lexical item that
                // pairs with whatever happens to stand near it. In a translation these are the
                // quotation mark and the parenthesis that open a verse, and the alignment said a
                // Hebrew word was rendered by an opening bracket. A Hebrew word that has assimilated
                // into the letter before it is a real word and keeps its links; it writes its Strong
                // number rather than nothing, so it is not one of these.
                if (sourceWords[pair.Source].Text == AlignmentTokens.Nothing
                    || targetWords[pair.Target].Text == AlignmentTokens.Nothing)
                {
                    unwritten++;
                    continue;
                }

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

                if (!Joined(sourceWords[pair.Source], targetWords[pair.Target]))
                {
                    continue;
                }

                standing.Add((pair.Source, pair.Target, pair.Translation, pair.Alignment));
            }

            foreach (var (from, to, confidence, position) in Selections.Apply(
                         selection, standing, [.. targetWords.Select(word => word.Text)]))
            {
                var draft = new AlignedDraft(
                    sourceWords[from].Id, targetWords[to].Id, confidence, position);
                var pair = (draft.SourceWordId, draft.TargetWordId);

                if (!at.TryGetValue(pair, out var already))
                {
                    at[pair] = drafts.Count;
                    drafts.Add(draft);
                }
                else if (drafts[already].Translation < confidence)
                {
                    drafts[already] = draft;
                }
            }
        }

        return (drafts, proposed, collapsed + unwritten, below);
    }

    /// <summary>
    /// Whether two words met at an address may be paired there. A verse covering an address beyond
    /// its own is joined to the verse the other text stands there, and not to another verse that only
    /// covers it too: Brenton's Sirach 30:26 closes the standard's 33:16 and his 36:16 opens it, so
    /// the English close met the Greek opening at 33:16, and its "winepress" was paired with
    /// ἠγρύπνησα across two verses nothing joins.
    /// </summary>
    private static bool Joined(Word source, Word target) =>
        source.Aside is not { } from || target.Aside is not { } to || from == to;

    /// <summary>
    /// The words read at an address, less those read there only because their verse covers it, where
    /// the verse stands in a chapter the pair's verse links leave out: nothing joins that verse to the
    /// one standing at the address. The King James's Tobit 7:15 stands at 7:13 and covers 7:14, where
    /// the Synodal's 7:14 stands, and the two editions number Tobit 7 otherwise, so the verse links
    /// join neither verse and the aligner paired their words across them.
    /// </summary>
    internal static Dictionary<(int, int, int), List<Word>> Unmet(
        Dictionary<(int, int, int), List<Word>> words,
        IReadOnlySet<(int Book, int Chapter)> refused)
    {
        if (refused.Count == 0)
        {
            return words;
        }

        bool Met(Word word) =>
            word.Aside is not { } standing
            || TwinPassages.Joined(standing) is var (book, chapter, _) && !refused.Contains((book, chapter));

        return words
            .Select(address => (address.Key, Words: address.Value.Where(Met).ToList()))
            .Where(address => address.Words.Count > 0)
            .ToDictionary(address => address.Key, address => address.Words);
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
        bool replace,
        CancellationToken cancellationToken,
        string? note = null)
    {
        if (drafts.Count == 0)
        {
            return;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();

        // The earlier run's links go in the same transaction the new ones arrive in, so the pair is
        // never read with neither or with both. Their words and claims go with them, and so do the
        // claims the run left on links somebody else heads: an answer the aligner gave that landed
        // on a stated link's words is a claim on that link, and the rerun gives it again or not.
        if (replace)
        {
            await using var delete = new NpgsqlCommand(
                """
                DELETE FROM link WHERE from_text_id = @from AND to_text_id = @to AND method = 'aligner';
                DELETE FROM link_claim c
                USING link l
                WHERE c.link_id = l.id AND l.from_text_id = @from AND l.to_text_id = @to
                  AND l.method <> 'aligner' AND c.method = 'aligner';
                """,
                connection);
            delete.Parameters.AddWithValue("from", fromTextId);
            delete.Parameters.AddWithValue("to", toTextId);
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }

        // The model reports how likely the word pairing is and how likely the position is. The
        // schema has one confidence, so the pairing is what it holds and the position rides along
        // in the source, where it stays readable rather than being averaged away.
        var rejected = await RejectedRenderings.Locate(connection, cancellationToken);
        drafts.RemoveAll(draft => rejected.Contains((draft.SourceWordId, draft.TargetWordId))
            || rejected.Contains((draft.TargetWordId, draft.SourceWordId)));
        await LinkWriter.Write(
            connection,
            (NpgsqlTransaction)transaction.GetDbTransaction(),
            [
                .. drafts.Select(draft => new NewLink(
                    fromTextId,
                    toTextId,
                    LinkRelation.Renders,
                    LinkMethod.Aligner,
                    Routes.Written(draft.Translation),
                    $"SIL.Machine {modelType}, symmetrised och" +
                    (syntax ? ", rescored on ETCBC phrase and clause structure" : string.Empty) +
                    (double.IsNaN(draft.Position)
                        ? ", the names of the verse paired by spelling and order"
                        : $", position {draft.Position:F4}") +
                    (note is null ? string.Empty : $"; {note}"),
                    null,
                    [draft.SourceWordId],
                    [draft.TargetWordId])),
            ],
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
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
    private static string[] Lines(
        List<(int, int, int)> addresses,
        Dictionary<(int, int, int), List<Word>> words,
        IReadOnlyDictionary<((int Book, int Chapter, int Verse) Address, int Position), string>? shared = null)
    {
        var lines = new string[addresses.Count];

        for (var i = 0; i < addresses.Count; i++)
        {
            var address = addresses[i];
            var verse = words[address];
            var (book, chapter, number) = address;
            lines[i] = AlignmentTokens.Line(
                verse.Select((word, position) => shared is not null
                    && shared.TryGetValue((address, position), out var token)
                        ? token
                        : word.Text),
                $"{book} {chapter}:{number}");
        }

        return lines;
    }

    /// <summary>
    /// Whether every input file in a workspace holds exactly the lines it would be written with now,
    /// which is the only thing that says the answer beside them is about these verses.
    /// </summary>
    internal static bool Unchanged(IEnumerable<(string Path, string[] Lines)> inputs) =>
        inputs.All(input => File.Exists(input.Path) && File.ReadLines(input.Path).SequenceEqual(input.Lines));

    /// <summary>
    /// Gives both ends of a known correspondence the target's Strong number as a temporary shared
    /// vocabulary item. It is written only into a training workspace: the corpus still keeps the
    /// texts' actual words and only source-backed links retain their original provenance.
    /// </summary>
    private static SharedTokens SharedAnchorTokens(
        Dictionary<(int Book, int Chapter, int Verse), List<Word>> source,
        Dictionary<(int Book, int Chapter, int Verse), List<Word>> target,
        IReadOnlyDictionary<(int Book, int Chapter, int Verse), List<(int Source, int Target)>> anchors)
    {
        var left = new Dictionary<((int, int, int), int), string>();
        var right = new Dictionary<((int, int, int), int), string>();

        foreach (var (address, pairs) in anchors)
        {
            if (!source.TryGetValue(address, out var sourceVerse)
                || !target.TryGetValue(address, out var targetVerse))
            {
                continue;
            }

            foreach (var (from, to) in pairs)
            {
                if (from >= sourceVerse.Count || to >= targetVerse.Count
                    || string.IsNullOrWhiteSpace(targetVerse[to].StrongNumber))
                {
                    continue;
                }

                var token = $"strong-{targetVerse[to].StrongNumber}";
                left[(address, from)] = token;
                right[(address, to)] = token;
            }
        }

        return new SharedTokens(left, right);
    }

    private static string[] AnchorLines(
        List<(int Book, int Chapter, int Verse)> addresses,
        IReadOnlyDictionary<(int Book, int Chapter, int Verse), List<(int Source, int Target)>> anchors) =>
        [
            .. addresses.Select(address => anchors.TryGetValue(address, out var pairs)
                ? string.Join(' ', pairs.Select(pair => $"{pair.Source}-{pair.Target}"))
                : string.Empty),
        ];

    private async Task<Database.Entities.Text> Text(string slug, CancellationToken cancellationToken) =>
        await db.Texts.SingleOrDefaultAsync(t => t.Slug == slug, cancellationToken)
        ?? throw new InvalidOperationException($"There is no text \"{slug}\".");

    /// <summary>
    /// Every word of a text by the canonical address it stands at — by every address it stands at,
    /// not by its primary one alone.
    ///
    /// <para>
    /// A verse covering two addresses contributes its words to each, because a model bucketing by
    /// the primary address alone cannot see half of what such a verse holds. The Synodal's Psalm 3:1
    /// prints the superscription and the body together where the Hebrew numbers them 3:1 and 3:2;
    /// bucketed at 3:1 it is offered the body of the Hebrew psalm and nothing else, and its nine
    /// title words have nothing in front of them to match. The title formulae repeat across sixty
    /// psalms, so the evidence is there as soon as the words are put where they belong.
    /// </para>
    ///
    /// <para>
    /// It costs the same verse being read twice on both sides of a pair where it genuinely spans
    /// two addresses, and that is few: Brenton carries 129 such addresses another text also
    /// occupies, BHSA three and Nestle two, against Brenton's 22,775 verses. The King James, the
    /// Berean and the two Slavic texts carry a hundred each for the Greek additions to Esther, and
    /// no text here holds a verse at any of those addresses, so none of them is ever bucketed
    /// twice.
    /// </para>
    /// </summary>
    /// <param name="books">
    /// The canonical books to read, or all of them. A trial on a few books trains on those books
    /// alone, which is weaker than the whole text and never stronger.
    /// </param>
    /// <param name="lettered">
    /// False to leave out the verses the text prints with a letter, which <see cref="PrintTheSameVerses"/>
    /// explains.
    /// </param>
    /// <param name="primaryOnly">
    /// Every word at the one address its verse stands at primarily. A pool's Greek is read so, as its
    /// source is: the Ge'ez and the Greek it was made from stand at the same primary rows, and the
    /// further row a verse of either covers is where the frame parks the Hebrew's counterpart.
    /// </param>
    private async Task<Dictionary<(int, int, int), List<Word>>> Words(
        string slug,
        Func<WordForms, string> form,
        CancellationToken cancellationToken,
        IReadOnlySet<int>? books = null,
        bool primaryOnly = false,
        bool lettered = true)
    {
        var within = books?.ToList();
        var rows = await db.VerseReferences
            .Where(r => r.Verse!.Text!.Slug == slug)
            .Where(r => within == null || within.Contains(r.CanonicalBook))
            .Where(r => lettered || r.Verse!.Label == "")
            .SelectMany(r => r.Verse!.Words.Select(w => new
            {
                r.CanonicalBook,
                r.CanonicalChapter,
                r.CanonicalVerse,
                r.IsPrimary,
                r.VerseId,
                OwnBook = r.Verse.Book!.CanonicalOrdinal,
                r.Verse.ChapterNumber,
                r.Verse.Number,
                r.Verse.Sequence,
                w.Position,
                w.Id,
                w.Surface,
                w.Lemma,
                w.StrongNumber,
                Language = w.Text!.Language,
                Consonantal = w.Morphology == null ? null : w.Morphology.RootElement.GetProperty("consonantal")
                    .GetString(),
                Pos = w.Morphology == null ? null : w.Morphology.RootElement.GetProperty("pos").GetString(),
                Form = w.Morphology == null ? null : w.Morphology.RootElement.GetProperty("form").GetString(),
                Robinson = w.Morphology == null ? null : w.Morphology.RootElement.GetProperty("robinson").GetString(),
            }))
            .ToListAsync(cancellationToken);

        var uncapitalised = rows
            .Where(r => r.Position > 1 && r.Surface.Length > 0 && char.IsLower(r.Surface[0]))
            .Select(r => r.Surface.ToLowerInvariant())
            .ToHashSet();

        // A text with a map of its own is read where it stands primarily, and a mapped verse where the
        // map reads it and nowhere else; one the map has no line for stands nowhere, since its own
        // number is some other passage's address. The Ge'ez numbers as the Greek it was made from
        // does, and the further rows the frame gives its verses are where it parks the Hebrew's
        // counterpart, which it may give the Greek one row apart.
        var map = VerseMap(slug);
        var standing = new Dictionary<int, (int, int, int)>();
        foreach (var row in rows.Where(row => row.IsPrimary))
        {
            standing.TryAdd(row.VerseId, (row.CanonicalBook, row.CanonicalChapter, row.CanonicalVerse));
        }

        var placed = map is not { } mapped
            ? rows.Where(r => r.IsPrimary || !primaryOnly)
                .Select(r => (Address: (r.CanonicalBook, r.CanonicalChapter, r.CanonicalVerse), Row: r))
            : rows.Where(r => r.IsPrimary).SelectMany(r => !mapped.Books.Contains(r.OwnBook)
                ? [(Address: (r.CanonicalBook, r.CanonicalChapter, r.CanonicalVerse), Row: r)]
                : mapped.Addresses.TryGetValue((r.OwnBook, r.ChapterNumber, r.Number), out var to)
                    ? to.Select(address => (Address: address, Row: r))
                    : []);

        // Where several verses meet at one address each is read whole, in the order the edition
        // writes them. Ordered by position alone, the twenty-five verses Swete prints at 3 Kingdoms
        // 12:24 were read as the first word of each, then the second of each, and a model learns
        // where a word stands from exactly that order.
        return placed
            .GroupBy(p => p.Address)
            .ToDictionary(
                group => group.Key,
                group => group.Select(p => p.Row)
                    .OrderBy(r => r.OwnBook)
                    .ThenBy(r => r.ChapterNumber)
                    .ThenBy(r => r.Sequence)
                    .ThenBy(r => r.VerseId)
                    .ThenBy(r => r.Position)
                    .Select(r =>
                    {
                        var forms = new WordForms(
                            r.Surface, r.Lemma, r.Consonantal, r.StrongNumber, r.Language, r.Position);
                        var letters = NameLists.Skeleton(r.Consonantal ?? r.Surface, r.Language);
                        return new Word(
                            r.Id,
                            AlignmentTokens.One(form(forms)),
                            r.StrongNumber,
                            letters,
                            IsNamed(forms, r.Pos, r.Form ?? r.Robinson, uncapitalised) && letters.Length > 0
                                ? letters
                                : null,
                            r.IsPrimary ? null : standing.GetValueOrDefault(r.VerseId));
                    })
                    .ToList());
    }

    /// <summary>
    /// Whether the verses one text prints with a letter may be paired with the other text at all.
    ///
    /// A lettered verse — Esther 1:1a to 1:1s, 3 Kingdoms 12:24a to 12:24z — is text the address
    /// system cannot name, and the frame stands it at the numbered verse beside it. Paired by that
    /// address with a text numbered as the Hebrew, it is handed to the aligner against a verse that
    /// does not contain it, and the aligner links what it is given: Addition D's overseer came out
    /// as the Hebrew's "she put on" at 0.965. So a lettered verse is paired only between two texts
    /// numbered in one tradition — the two Greek editions, Brenton's English and the Ge'ez made from
    /// the Greek; the Clementine and the Douay — which print the same material in the same places.
    /// Against any other it stands unpaired, which is what it is.
    /// </summary>
    private async Task<bool> PrintTheSameVerses(string fromSlug, string toSlug, CancellationToken cancellationToken) =>
        await db.Texts
            .Where(t => t.Slug == fromSlug || t.Slug == toSlug)
            .Select(t => t.Versification)
            .Distinct()
            .CountAsync(cancellationToken) == 1;

    /// <summary>Every word of a text by address as it is written, with the consonants of its names.</summary>
    internal async Task<Dictionary<(int, int, int), List<Word>>> Named(
        string slug,
        string partner,
        IReadOnlySet<int>? books,
        CancellationToken cancellationToken) =>
        await Words(slug, Written, cancellationToken, books,
            lettered: await PrintTheSameVerses(slug, partner, cancellationToken));

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
    private static string Reduce(WordForms word, bool suppletion = false) => word.Language switch
    {
        "rus" or "ukr" => SlavicStemmer.Stem(word.Surface, IsName(word), suppletion),
        "eng" => EnglishStemmer.Stem(word.Surface),
        "grc" => GreekStemmer.Stem(word.Surface),
        "deu" => GermanStemmer.Stem(word.Surface),
        "spa" => SpanishStemmer.Stem(word.Surface),
        "fra" => FrenchStemmer.Stem(word.Surface),
        "por" => PortugueseStemmer.Stem(word.Surface),
        "gez" => GeezStemmer.Stem(word.Surface),
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
    /// Whether a word is a proper name, for <see cref="NameLists"/>. BHSA and the Greek editions mark
    /// their names; the translations capitalise them, and capitalise other words too. A word the same
    /// text also writes in lower case mid-verse is not a name — God and god, Бога and бога — and one
    /// in capitals throughout is LORD, which the translations do not transliterate.
    /// </summary>
    /// <param name="uncapitalised">Every word the text writes in lower case after its verse's first.</param>
    private static bool IsNamed(WordForms word, string? pos, string? form, IReadOnlySet<string> uncapitalised) =>
        pos == "nmpr"
        || form == IndeclinableName
        || (word.Language is "grc" && !string.IsNullOrEmpty(word.Lemma)
            ? char.IsUpper(word.Lemma[0])
            : word.Language is not "hbo" && IsName(word) && word.Surface.Any(char.IsLower)
              && !uncapitalised.Contains(word.Surface.ToLowerInvariant()));

    /// <summary>The Robinson code the Greek editions give a name that does not decline.</summary>
    private const string IndeclinableName = "N-PRI";

    /// <summary>
    /// The form a model can learn from. BHSA writes full vowel pointing, so the same word appears as
    /// many different strings and a lexicon built on twenty-three thousand verses never sees any of
    /// them often enough; using the consonants instead raised precision by a quarter.
    /// </summary>
    private static string Comparable(WordForms word, bool suppletion = false) =>
        word.Language is "rus" or "ukr" or "eng" or "deu" or "spa" or "fra" or "por" or "gez" ? Reduce(word, suppletion)
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

    /// <param name="Letters">The consonants as <see cref="NameLists"/> compares them.</param>
    /// <param name="Name">The same, where the word is a proper name, and null where it is not.</param>
    /// <param name="Aside">
    /// Where the word's verse stands, when the address it was read at is only a further one the verse
    /// covers; null at the verse's own address.
    /// </param>
    internal sealed record Word(
        long Id,
        string Text,
        string? StrongNumber,
        string Letters = "",
        string? Name = null,
        (int Book, int Chapter, int Verse)? Aside = null);

    private sealed record WordPosition((int Book, int Chapter, int Verse) Address, int Position);

    private sealed record AnchorCandidate((int Book, int Chapter, int Verse) Address, int Source, int Target);

    private sealed record SharedTokens(
        IReadOnlyDictionary<((int Book, int Chapter, int Verse) Address, int Position), string> Source,
        IReadOnlyDictionary<((int Book, int Chapter, int Verse) Address, int Position), string> Target);

    private sealed record AlignedDraft(long SourceWordId, long TargetWordId, double Translation, double Position);
}
