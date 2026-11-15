using System.Text;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Links;
using Word = Essenthos.Core.Loading.Links.AlignmentPipeline.Word;
using WordForms = Essenthos.Core.Loading.Links.AlignmentPipeline.WordForms;

namespace Essenthos.Core.Mapping;

/// <summary>
/// What SIL's aligner is worth on a pair, measured from the store's files: the two texts' words as the aligner reads them
/// (<see cref="AlignedWords"/>), the answer stored for exactly those lines (trained where none is), the target's syntax
/// (<see cref="AlignerFiles.Syntax"/>), and the pair's final as the key (<see cref="MappingLinks"/>). It writes nothing
/// but the aligner's own workspace, and reads no database (PRB-1589): what <c>score</c>, <c>score-anchors</c> and
/// <c>syntax</c> measured on the live corpus, they measure on the store the build reads.
/// </summary>
internal sealed class AlignerMeasures(AlignerFiles aligner)
{
    private readonly MappingLinks _links = new(aligner.Texts);

    private AlignmentPipeline Pipeline => aligner.Pipeline;

    /// <summary>The two texts' words in the forms given, and the addresses both hold, as the pair's producer reads them.</summary>
    private (List<(int, int, int)> Addresses, Dictionary<(int, int, int), List<Word>> Source, Dictionary<(int, int, int), List<Word>> Target)
        Words(string fromSlug, string toSlug, Func<WordForms, string> sourceForm, Func<WordForms, string> targetForm) =>
        aligner.Words(fromSlug, toSlug, sourceForm, targetForm);

    /// <summary>The aligner's workspace of that name under the store's alignments.</summary>
    private string Workspace(string name) => Path.Combine(aligner.Root, name);

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
        var (addresses, source, target) = Words(fromSlug, toSlug,
            targetSurface ? AlignmentPipeline.Written : word => AlignmentPipeline.Reduce(word, suppletion),
            targetSurface ? AlignmentPipeline.Written : word => AlignmentPipeline.Comparable(word, suppletion));

        Directory.CreateDirectory(Workspace(workspace));
        var alignmentFile = await Pipeline.Align(
            fromSlug, toSlug, Workspace(workspace), modelType, addresses, source, target, cancellationToken);

        var (gold, structural) = Stated(fromSlug, toSlug, statedOnly);
        var content = gold.Where(pair => !structural.Contains(pair.To)).ToHashSet();
        var prior = aligner.Syntax(toSlug);
        var (fromLanguage, toLanguage) = (aligner.Texts[fromSlug].Language, aligner.Texts[toSlug].Language);

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
                        AlignmentPipeline.Read(alignmentFile, addresses, source, target, threshold, selection, read);
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
            var (stored, _, _, _) = AlignmentPipeline.Read(
                alignmentFile, addresses, source, target, AlignmentPipeline.DefaultMinimumConfidence, Selection.BestPerSource, prior,
                AlignmentPipeline.MarksNoNames(fromLanguage), AlignmentPipeline.BothMarkTheirNames(fromLanguage, toLanguage));
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
        var (addresses, source, target) = Words(fromSlug, toSlug,
            word => AlignmentPipeline.Reduce(word), word => AlignmentPipeline.Comparable(word));
        var (gold, _) = Stated(fromSlug, toSlug, statedOnly: true);
        var stated = StatedAnchors(fromSlug, toSlug);
        var (anchors, heldOut, candidates) = SplitAnchors(source, target, stated, gold, heldOutFold);

        if (anchors.Sum(entry => entry.Value.Count) == 0 || heldOut.Count == 0)
        {
            throw new InvalidOperationException(
                $"{fromSlug} and {toSlug} have no usable held-out stated anchors. The measurement needs " +
                "one-to-one source mappings spanning both the training and test verses.");
        }

        Directory.CreateDirectory(Workspace(workspace));
        var baseline = await Pipeline.Align(
            fromSlug, toSlug, Path.Combine(Workspace(workspace), "baseline"), modelType, addresses, source, target,
            cancellationToken);
        var constrained = await Pipeline.Align(
            fromSlug, toSlug, Path.Combine(Workspace(workspace), "anchored"), modelType, addresses, source, target,
            cancellationToken, anchors);
        var shared = await Pipeline.Align(
            fromSlug, toSlug, Path.Combine(Workspace(workspace), "shared-anchor-tokens"), modelType, addresses, source, target,
            cancellationToken, anchors, shareAnchorTokens: true);

        var prior = aligner.Syntax(toSlug);
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
                var (drafts, _, _, _) = AlignmentPipeline.Read(
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

    /// <summary>The word pairs of the pair's final whose link is stated by a source or by a Strong number.</summary>
    private HashSet<(long From, long To)> StatedAnchors(string fromSlug, string toSlug) =>
        Pairs(_links.Links(fromSlug, toSlug).Where(link => link.Method is LinkMethod.StatedBySource or LinkMethod.StrongNumber));

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
    private (HashSet<(long From, long To)> Gold, HashSet<long> Structural) Stated(string fromSlug, string toSlug, bool statedOnly)
    {
        var gold = Pairs(_links.Links(fromSlug, toSlug).Where(link => link.Method == LinkMethod.StatedBySource
            || (!statedOnly && link.Method is not (LinkMethod.Aligner or LinkMethod.RuleBased))));
        var target = aligner.Texts[toSlug];
        var structural = new HashSet<long>();
        for (var id = 0; id < target.Words.Count; id++)
        {
            if (target.Words[id].Strong is { } number && (number.StartsWith("H9", StringComparison.Ordinal) || number == "H853"))
            {
                structural.Add(id);
            }
        }

        return (gold, structural);
    }

    /// <summary>Every word pair the links name, each word of the one side with each of the other.</summary>
    private static HashSet<(long From, long To)> Pairs(IEnumerable<MappingLinks.FinalLink> links) =>
        [.. links.SelectMany(link => link.From.SelectMany(from => link.To.Select(to => (from, to))))];

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
        var (addresses, source, target) = Words(fromSlug, toSlug,
            word => AlignmentPipeline.Reduce(word), word => AlignmentPipeline.Comparable(word));

        Directory.CreateDirectory(Workspace(workspace));
        var alignmentFile = await Pipeline.Align(
            fromSlug, toSlug, Workspace(workspace), modelType, addresses, source, target, cancellationToken);

        var (gold, _) = Stated(fromSlug, toSlug, statedOnly);
        var prior = aligner.Syntax(toSlug);

        var cohesions = Enum.GetValues<Cohesion>();
        var seen = new int[Bands.Length + 1, cohesions.Length];
        var right = new int[Bands.Length + 1, cohesions.Length];

        foreach (var (sourceWords, targetWords, raw) in AlignmentPipeline.Parse(alignmentFile, addresses, source, target))
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

    private sealed record WordPosition((int Book, int Chapter, int Verse) Address, int Position);

    private sealed record AnchorCandidate((int Book, int Chapter, int Verse) Address, int Source, int Target);

}
