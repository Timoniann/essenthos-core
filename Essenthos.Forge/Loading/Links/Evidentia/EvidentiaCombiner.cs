using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Essenthos.Core.Loading.Links.Evidentia;

/// <summary>
/// One edge a combiner weighs: a word of the translation the lexical tiers decide, and a word of the original
/// its verse offers it, with the signals read off the candidate graph, the pivot and the tiers' own placement.
/// </summary>
internal sealed record EvidentiaCombinerEdge(
    EvidentiaAnalysis Source,
    EvidentiaAnalysis Target,
    EvidentiaCandidate? Candidate,
    EvidentiaProposal? Current,
    double[] Features)
{
    public bool IsCurrent => Current is { } current && current.Target.Token.Id == Target.Token.Id;
}

/// <summary>An edge as <c>--combiner-export</c> writes it, with what the answer key says of it, for a fit.</summary>
internal sealed record EvidentiaCombinerRow(
    int Book,
    int Chapter,
    int Verse,
    long SourceWordId,
    long TargetWordId,
    string Group,
    bool Covered,
    bool Label,
    bool Current,
    string? CurrentKind,
    double[] Features);

/// <summary>
/// The signals a combiner reads for every edge, and the words it owns: content words the lexical tiers placed or
/// left unplaced. A grammatical word is not its business, and a word no edge reaches has nothing to weigh.
/// </summary>
internal static class EvidentiaCombinerFeatures
{
    public static readonly IReadOnlyList<string> EvidenceKinds =
        ["DictionarySense", "KnownRendering", "TargetGloss", "Morphology", "MatchingNormalisedForm", "Syntax"];

    public static readonly IReadOnlyList<string> PartsOfSpeech = ["NOUN", "PROPN", "VERB", "ADJ", "ADV", "NUM"];

    public static readonly IReadOnlyList<string> PlacementKinds =
    [
        nameof(EvidentiaProposalKind.GlobalReviewKnownRendering),
        nameof(EvidentiaProposalKind.UniqueDictionarySenseReview),
        nameof(EvidentiaProposalKind.UniqueTargetGlossReview),
        nameof(EvidentiaProposalKind.ResidualKnownRendering),
        nameof(EvidentiaProposalKind.AnchoredGapReview),
        nameof(EvidentiaProposalKind.RepeatedRenderingInOrder),
        nameof(EvidentiaProposalKind.UniqueContextGlossReview),
        nameof(EvidentiaProposalKind.DictionaryAndGlossReview),
    ];

    private static readonly HashSet<EvidentiaEvidenceKind> Proposing =
    [
        EvidentiaEvidenceKind.KnownRendering, EvidentiaEvidenceKind.DictionarySense, EvidentiaEvidenceKind.TargetGloss,
    ];

    /// <summary>The names of the features, in the order <see cref="Edges"/> writes them, for the given pivot texts.</summary>
    public static IReadOnlyList<string> Names(IReadOnlyList<string> pivots) =>
    [
        "score", "share", "sqrt_share", "lead", "top", "inverse_candidates", "inverse_claimants", "diagonal", "diagonal_squared",
        "pivot_only", "prefix_target", "evidence_kinds", "pivots_naming", "two_pivots", "pivot_names_another", "pivot_names_word",
        "current", "current_safe",
        .. EvidenceKinds.Select(kind => $"evidence_{kind}"),
        .. PartsOfSpeech.Select(part => $"pos_{part}"),
        .. pivots.Select(pivot => $"pivot_{pivot}"),
        .. PlacementKinds.Select(kind => $"current_{kind}"),
        "pivots_naming_lexical", "pivot_names_another_current",
        "pos_counterparts", "pos_not_counterparts", "number_agrees", "number_disagrees",
        "between_anchors", "anchor_distance", "no_anchor", "head_near", "head_far",
    ];

    /// <summary>
    /// Which group of a fitted model reads an edge: a Hebrew or Aramaic original is fitted apart from a Greek one,
    /// where the model was fitted so.
    /// </summary>
    public static string Group(EvidentiaAnalysis target) =>
        target.Token.Language.Equals("grc", StringComparison.OrdinalIgnoreCase) ? "greek" : "hebrew";

    public static bool Owns(EvidentiaAnalysis word) => !EvidentiaStateScore.IsGrammatical(word);

    /// <summary>
    /// Every edge of every word the combiner owns: each candidate in the word's own verse, every word a pivot puts
    /// it on there, and the tiers' own placement wherever it stands. A word of the original another word holds by a
    /// placement the combiner does not own is not offered.
    /// </summary>
    public static IReadOnlyList<EvidentiaCombinerEdge> Edges(
        IReadOnlyList<EvidentiaAnalysis> source,
        IReadOnlyList<EvidentiaAnalysis> target,
        IReadOnlyList<EvidentiaCandidate> candidates,
        IReadOnlyList<EvidentiaProposal> lexical,
        IReadOnlySet<(long From, long To)> safe,
        EvidentiaPivot pivot,
        IReadOnlyList<string> pivots)
    {
        var words = source.DistinctBy(word => word.Token.Id).ToList();
        var owned = words.Where(Owns).Select(word => word.Token.Id).ToHashSet();
        var current = lexical.GroupBy(proposal => proposal.Source.Token.Id).ToDictionary(group => group.Key, group => group.First());
        var held = lexical.Where(proposal => !owned.Contains(proposal.Source.Token.Id))
            .Select(proposal => proposal.Target.Token.Id).ToHashSet();
        var targets = target.DistinctBy(word => word.Token.Id).ToDictionary(word => word.Token.Id);
        var targetRank = Ranks(targets.Values);
        var sourceRank = Ranks(words);
        var byWord = candidates.GroupBy(candidate => candidate.Source.Token.Id).ToDictionary(group => group.Key, group => group.ToList());
        var named = new HashSet<string>(pivots, StringComparer.Ordinal);
        var targetIndex = Indexes(targets.Values);
        var renderedAt = lexical
            .Where(proposal => targetIndex.ContainsKey(proposal.Target.Token.Id))
            .GroupBy(proposal => proposal.Source.Token.Id)
            .ToDictionary(group => group.Key, group => (group.First().Target.Token.Address, Index: targetIndex[group.First().Target.Token.Id]));
        var verses = words.GroupBy(word => word.Token.Address)
            .ToDictionary(verse => verse.Key, verse => verse.OrderBy(word => word.Token.Position).ThenBy(word => word.Token.Id).ToList());

        var offered = new List<(EvidentiaAnalysis Word, Dictionary<long, EvidentiaCandidate> Candidates,
            Dictionary<string, HashSet<long>> Pivot, List<long> Targets)>();
        var claimants = new Dictionary<long, int>();
        foreach (var word in words.Where(word => owned.Contains(word.Token.Id))
                     .OrderBy(word => word.Token.Address.Verse).ThenBy(word => word.Token.Position).ThenBy(word => word.Token.Id))
        {
            var own = (byWord.GetValueOrDefault(word.Token.Id) ?? [])
                .Where(candidate => candidate.Target.Token.Address == word.Token.Address && targets.ContainsKey(candidate.Target.Token.Id))
                .GroupBy(candidate => candidate.Target.Token.Id)
                .ToDictionary(group => group.Key, group => group.MaxBy(candidate => candidate.Score)!);
            var said = pivot.Of(word.Token.Id)
                .Where(pair => named.Contains(pair.Key))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            var reached = new SortedSet<long>(own.Keys);
            reached.UnionWith(said.Values.SelectMany(ids => ids)
                .Where(id => targets.TryGetValue(id, out var other) && other.Token.Address == word.Token.Address));
            reached.ExceptWith(held);
            if (current.TryGetValue(word.Token.Id, out var placed) && targets.ContainsKey(placed.Target.Token.Id))
            {
                reached.Add(placed.Target.Token.Id);
            }

            if (reached.Count == 0)
            {
                continue;
            }

            offered.Add((word, own, said, [.. reached]));
            foreach (var id in reached)
            {
                claimants[id] = claimants.GetValueOrDefault(id) + 1;
            }
        }

        var edges = new List<EvidentiaCombinerEdge>();
        foreach (var (word, own, said, reached) in offered)
        {
            var placed = current.GetValueOrDefault(word.Token.Id);
            var isSafe = placed is not null && safe.Contains((word.Token.Id, placed.Target.Token.Id));
            var best = own.Values.Select(candidate => candidate.Score).DefaultIfEmpty(0).Max();
            var hasPivot = said.Count > 0 ? 1.0 : 0.0;
            foreach (var id in reached)
            {
                var to = targets[id];
                var candidate = own.GetValueOrDefault(id);
                var score = candidate?.Score ?? 0;
                var kinds = candidate?.Evidence.Select(evidence => evidence.Kind).ToHashSet() ?? [];
                var share = candidate?.Evidence
                    .Where(evidence => evidence.Kind == EvidentiaEvidenceKind.KnownRendering)
                    .Select(evidence => evidence.Support?.Share)
                    .FirstOrDefault() ?? 0;
                var second = own.Where(pair => pair.Key != id).Select(pair => pair.Value.Score).DefaultIfEmpty(0).Max();
                var naming = said.Values.Count(ids => ids.Contains(id));
                var another = said.Count > 0 && naming == 0 ? 1.0 : 0.0;
                var isCurrent = placed is not null && placed.Target.Token.Id == id ? 1.0 : 0.0;
                var diagonal = Math.Abs(targetRank[id] - sourceRank[word.Token.Id]);
                List<double> features =
                [
                    score, share, Math.Sqrt(share), score - second, candidate is not null && score >= best ? 1 : 0,
                    1.0 / Math.Max(1, own.Count), 1.0 / claimants[id], diagonal, diagonal * diagonal,
                    candidate is null ? 1 : 0, to.Token.StrongNumber?.StartsWith("H90", StringComparison.Ordinal) == true ? 1 : 0,
                    kinds.Count, naming, naming >= 2 ? 1 : 0, another, hasPivot,
                    isCurrent, isCurrent * (isSafe ? 1 : 0),
                    .. EvidenceKinds.Select(kind => kinds.Any(found => found.ToString() == kind) ? 1.0 : 0.0),
                    .. PartsOfSpeech.Select(part => word.Token.PartOfSpeech == part ? 1.0 : 0.0),
                    .. pivots.Select(text => said.TryGetValue(text, out var ids) && ids.Contains(id) ? 1.0 : 0.0),
                    .. PlacementKinds.Select(kind => isCurrent * (placed?.Kind.ToString() == kind ? 1 : 0)),
                    naming * (kinds.Overlaps(Proposing) ? 1 : 0), another * isCurrent,
                    .. Classes(word, to), .. Numbers(word, to),
                    .. Anchors(word, to, verses[word.Token.Address], renderedAt, targetIndex),
                ];
                edges.Add(new EvidentiaCombinerEdge(word, to, candidate, placed, [.. features]));
            }
        }

        return edges;
    }

    /// <summary>
    /// Where the edge stands against the placements around it: between the renderings of the nearest placed words before
    /// and after the word, how far from the nearer of them (in words, at most six, as a share of six), whether the verse
    /// has no such placement, and whether it stands within two words of the rendering of the word the parse hangs this one
    /// on, or more than four from it. Only placements in the word's own verse count.
    /// </summary>
    private static double[] Anchors(
        EvidentiaAnalysis word,
        EvidentiaAnalysis target,
        List<EvidentiaAnalysis> verse,
        Dictionary<long, (EvidentiaAddress Address, int Index)> renderedAt,
        Dictionary<long, int> targetIndex)
    {
        int? Rendering(EvidentiaAnalysis other) =>
            other.Token.Id != word.Token.Id && renderedAt.TryGetValue(other.Token.Id, out var at) && at.Address == target.Token.Address
                ? at.Index
                : null;

        var position = verse.IndexOf(word);
        var at = targetIndex[target.Token.Id];
        int? before = null, after = null;
        for (var index = position - 1; index >= 0 && before is null; index--)
        {
            before = Rendering(verse[index]);
        }

        for (var index = position + 1; index < verse.Count && after is null; index++)
        {
            after = Rendering(verse[index]);
        }

        var between = before is { } low && after is { } high && Math.Min(low, high) < at && at < Math.Max(low, high) ? 1.0 : 0.0;
        var nearest = new[] { before, after }.OfType<int>().Select(index => Math.Abs(index - at)).DefaultIfEmpty(-1).Min();
        var distance = nearest < 0 ? 0 : Math.Min(nearest, MaximumAnchorDistance) / (double)MaximumAnchorDistance;
        var head = word.Token.SyntacticHead is { } id ? verse.FirstOrDefault(other => other.Token.Id == id) : null;
        var fromHead = head is not null && Rendering(head) is { } headAt ? Math.Abs(headAt - at) : (int?)null;
        return
        [
            between, distance, nearest < 0 ? 1 : 0,
            fromHead is <= 2 ? 1 : 0, fromHead is > 4 ? 1 : 0,
        ];
    }

    private const int MaximumAnchorDistance = 6;

    /// <summary>Each word's place in its verse, counted from 0.</summary>
    private static Dictionary<long, int> Indexes(IEnumerable<EvidentiaAnalysis> words)
    {
        var indexes = new Dictionary<long, int>();
        foreach (var verse in words.GroupBy(word => word.Token.Address))
        {
            var ordered = verse.OrderBy(word => word.Token.Position).ThenBy(word => word.Token.Id).ToList();
            for (var index = 0; index < ordered.Count; index++)
            {
                indexes[ordered[index].Token.Id] = index;
            }
        }

        return indexes;
    }

    /// <summary>Whether the two words are of classes that render one another, and whether both state a class that does not.</summary>
    private static double[] Classes(EvidentiaAnalysis word, EvidentiaAnalysis target)
    {
        if (EvidentiaMorphologyLabels.AreCounterparts(word, target))
        {
            return [1, 0];
        }

        var known = EvidentiaMorphologyLabels.HasCounterparts(word)
            && EvidentiaMorphologyLabels.PartOfSpeech(target.PartOfSpeech ?? target.Token.PartOfSpeech, target.Token.Language) is not null;
        return [0, known ? 1 : 0];
    }

    /// <summary>Whether both words state a grammatical number, and whether it is the same: a dual counts as a plural.</summary>
    private static double[] Numbers(EvidentiaAnalysis word, EvidentiaAnalysis target)
    {
        if (Number(word) is not { } one || Number(target) is not { } two)
        {
            return [0, 0];
        }

        return one == two ? [1, 0] : [0, 1];
    }

    private static string? Number(EvidentiaAnalysis word) =>
        word.Token.Morphology?.FirstOrDefault(pair => pair.Key.Equals("Number", StringComparison.OrdinalIgnoreCase)).Value is { } value
        && EvidentiaMorphologyLabels.Feature(value, word.Token.Language) is { } number
            ? number == "du" ? "pl" : number
            : null;

    /// <summary>Each word's place in its verse, from 0 at the first word to 1 at the last.</summary>
    private static Dictionary<long, double> Ranks(IEnumerable<EvidentiaAnalysis> words)
    {
        var ranks = new Dictionary<long, double>();
        foreach (var verse in words.GroupBy(word => word.Token.Address))
        {
            var ordered = verse.OrderBy(word => word.Token.Position).ThenBy(word => word.Token.Id).ToList();
            for (var index = 0; index < ordered.Count; index++)
            {
                ranks[ordered[index].Token.Id] = ordered.Count == 1 ? 0 : (double)index / (ordered.Count - 1);
            }
        }

        return ranks;
    }
}

/// <summary>What the combiner decided for a chapter's owned words.</summary>
/// <param name="Lexical">The lexical placements with the owned words' replaced by the combiner's.</param>
/// <param name="Safe">The owned words' placements in the safe band.</param>
/// <param name="Probabilities">The calibrated probability of each owned word's edge that stands.</param>
/// <param name="Added">Placements on an edge the tiers had not chosen.</param>
/// <param name="Withdrawn">The tiers' placements the combiner took back.</param>
/// <param name="Weighed">The words it weighed, whose tier it sets.</param>
internal sealed record EvidentiaCombinerDecision(
    IReadOnlyList<EvidentiaProposal> Lexical,
    IReadOnlySet<(long From, long To)> Safe,
    IReadOnlyDictionary<long, double> Probabilities,
    IReadOnlyList<EvidentiaProposal> Added,
    IReadOnlyList<EvidentiaProposal> Withdrawn,
    IReadOnlySet<long> Weighed);

/// <summary>
/// A fitted logistic model over the edges of a verse, read from the weights file versioned beside it, and the
/// one decision per verse it makes: edges are taken greedily by probability, one per word and one per word of
/// the original. A new placement needs the review band; the tiers' own placement stands down only to a better
/// edge or where the model puts it under the withdrawal line. The weights are fitted offline on the training
/// passages and calibrated on another, so nothing here reads an answer key.
/// </summary>
internal sealed class EvidentiaCombiner
{
    public const string ResourceName = "Essenthos.Core.Loading.Links.Evidentia.EvidentiaCombiner.json";

    private static readonly Lazy<EvidentiaCombiner> Embedded = new(() =>
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
                           ?? throw new InvalidOperationException($"{ResourceName} is not embedded in the Forge assembly.");
        return Read(stream);
    });

    private readonly CombinerFile file;

    private EvidentiaCombiner(CombinerFile file)
    {
        this.file = file;
        var names = EvidentiaCombinerFeatures.Names(file.Pivots);
        if (!names.SequenceEqual(file.Features))
        {
            throw new InvalidOperationException(
                $"The combiner's weights were fitted on features [{string.Join(", ", file.Features)}] and this build computes " +
                $"[{string.Join(", ", names)}]. Fit it again on an export of this build.");
        }

        foreach (var (group, model) in file.Models)
        {
            if (model.Weights.Length != names.Count || model.Mean.Length != names.Count || model.Scale.Length != names.Count)
            {
                throw new InvalidOperationException($"The combiner's '{group}' model does not have one weight per feature.");
            }

            if (model.Calibration.Length == 0)
            {
                throw new InvalidOperationException($"The combiner's '{group}' model has no calibration table.");
            }
        }
    }

    /// <summary>The weights the Forge was built with.</summary>
    public static EvidentiaCombiner Default => Embedded.Value;

    public static EvidentiaCombiner Read(Stream stream) =>
        new(JsonSerializer.Deserialize<CombinerFile>(stream, JsonOptions)
            ?? throw new InvalidOperationException("The combiner's weights file is empty."));

    /// <summary>How a run names the weights it decided by.</summary>
    public string Name => $"EvidentiaCombiner {file.Version}";

    public string Version => file.Version;

    public IReadOnlyList<string> Pivots => file.Pivots;

    public double SafeBand => file.Bands.Safe;

    public double ReviewBand => file.Bands.Review;

    public double WithdrawBelow => file.Bands.Withdraw;

    /// <summary>The band an edge the tiers had not chosen needs to be placed; the review band where the file names none.</summary>
    public double NewBand => file.Bands.New ?? file.Bands.Review;

    public IReadOnlyList<CombinerCheck> Checks => file.Checks ?? [];

    public object Description => new
    {
        name = Name,
        version = file.Version,
        fittedOn = file.FittedOn,
        commit = file.Commit,
        groups = file.Models.Keys.Order(StringComparer.Ordinal).ToArray(),
        bands = file.Bands,
        pivots = file.Pivots,
    };

    /// <summary>
    /// The model's own probability of an edge, then that probability as the calibration passage observed it: for the
    /// tiers' own placement, as it observed the tiers' placements the model kept, which win at lower probabilities.
    /// </summary>
    public (double Raw, double Calibrated) Probability(string group, IReadOnlyList<double> features, bool current = false)
    {
        var model = file.Models.TryGetValue(group, out var own) ? own
            : file.Models.TryGetValue(AllGroups, out var shared) ? shared
            : throw new InvalidOperationException($"The combiner has no model for {group} originals and none shared.");
        var sum = model.Intercept;
        for (var index = 0; index < features.Count; index++)
        {
            sum += model.Weights[index] * (features[index] - model.Mean[index]) / model.Scale[index];
        }

        var raw = 1 / (1 + Math.Exp(-sum));
        var calibration = current && model.CalibrationKept is { Length: > 0 } kept ? kept : model.Calibration;
        foreach (var step in calibration)
        {
            if (raw <= step[0])
            {
                return (raw, step[1]);
            }
        }

        return (raw, calibration[^1][1]);
    }

    /// <summary>The features that pushed an edge up the most, by weight times standardised value.</summary>
    private IEnumerable<string> Strongest(string group, IReadOnlyList<double> features, int take)
    {
        var model = file.Models.TryGetValue(group, out var own) ? own : file.Models[AllGroups];
        return Enumerable.Range(0, features.Count)
            .Select(index => (Name: file.Features[index], Push: model.Weights[index] * (features[index] - model.Mean[index]) / model.Scale[index]))
            .Where(feature => feature.Push > 0)
            .OrderByDescending(feature => feature.Push)
            .Take(take)
            .Select(feature => feature.Name);
    }

    public EvidentiaCombinerDecision Decide(IReadOnlyList<EvidentiaCombinerEdge> edges, IReadOnlyList<EvidentiaProposal> lexical)
    {
        var weighed = edges
            .Select(edge => (Edge: edge, Group: EvidentiaCombinerFeatures.Group(edge.Target),
                Probability: Probability(EvidentiaCombinerFeatures.Group(edge.Target), edge.Features, edge.IsCurrent)))
            .Where(edge => (edge.Edge.IsCurrent ? edge.Probability.Calibrated >= WithdrawBelow : edge.Probability.Calibrated >= NewBand))
            .OrderByDescending(edge => edge.Probability.Raw)
            .ThenByDescending(edge => edge.Edge.IsCurrent)
            .ThenBy(edge => edge.Edge.Source.Token.Address.Verse)
            .ThenBy(edge => edge.Edge.Source.Token.Position)
            .ThenBy(edge => edge.Edge.Target.Token.Position)
            .ToList();

        var decided = new Dictionary<long, EvidentiaProposal>();
        var takenTargets = new HashSet<long>();
        var safe = new HashSet<(long, long)>();
        var probabilities = new Dictionary<long, double>();
        var added = new List<EvidentiaProposal>();
        foreach (var (edge, group, (_, calibrated)) in weighed)
        {
            var word = edge.Source.Token.Id;
            if (decided.ContainsKey(word) || !takenTargets.Add(edge.Target.Token.Id))
            {
                continue;
            }

            var band = calibrated >= SafeBand ? EvidentiaDecisionRecorder.SafeTier : "review";
            var said = string.Create(CultureInfo.InvariantCulture, $"{Name}: probability {calibrated:0.000}");
            EvidentiaProposal proposal;
            if (edge.IsCurrent)
            {
                var placed = edge.Current!;
                proposal = placed with
                {
                    Trace = placed.Trace is { } trace
                        ? trace with { Tier = band, Rationale = $"{trace.Rationale}; {said}" }
                        : new EvidentiaDecisionTrace(band, said, edge.Candidate?.Evidence ?? []),
                };
            }
            else
            {
                proposal = new EvidentiaProposal(
                    edge.Source,
                    edge.Target,
                    EvidentiaProposalKind.CombinedEvidence,
                    calibrated,
                    new EvidentiaDecisionTrace(
                        band,
                        $"combined evidence, {said}; strongest: {string.Join(", ", Strongest(group, edge.Features, 3))}",
                        edge.Candidate?.Evidence.OrderByDescending(evidence => evidence.Score).ToList() ?? []));
                added.Add(proposal);
            }

            decided[word] = proposal;
            probabilities[word] = calibrated;
            if (calibrated >= SafeBand)
            {
                safe.Add((word, edge.Target.Token.Id));
            }
        }

        var weighedWords = edges.Select(edge => edge.Source.Token.Id).ToHashSet();
        List<EvidentiaProposal> result = [];
        List<EvidentiaProposal> withdrawn = [];
        foreach (var proposal in lexical)
        {
            var word = proposal.Source.Token.Id;
            if (!weighedWords.Contains(word))
            {
                result.Add(proposal);
            }
            else if (!decided.TryGetValue(word, out var replaced) || replaced.Target.Token.Id != proposal.Target.Token.Id)
            {
                withdrawn.Add(proposal);
            }
        }

        result.AddRange(decided.Values.OrderBy(proposal => proposal.Source.Token.Address.Verse).ThenBy(proposal => proposal.Source.Token.Position));
        return new EvidentiaCombinerDecision(result, safe, probabilities, added, withdrawn, weighedWords);
    }

    /// <summary>The group a model fitted on Hebrew and Greek together is filed under.</summary>
    public const string AllGroups = "all";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    internal sealed record CombinerFile(
        string Version,
        JsonElement FittedOn,
        string Commit,
        IReadOnlyList<string> Pivots,
        IReadOnlyList<string> Features,
        CombinerBands Bands,
        IReadOnlyDictionary<string, CombinerModel> Models,
        IReadOnlyList<CombinerCheck>? Checks = null);

    internal sealed record CombinerBands(double Safe, double Review, double Withdraw, double? New = null);

    /// <param name="Calibration">Steps of the calibration: up to the raw probability of the first, the rate of the second.</param>
    /// <param name="CalibrationKept">The same for the tiers' own placements, where the fit calibrated them apart.</param>
    internal sealed record CombinerModel(
        double Intercept, double[] Weights, double[] Mean, double[] Scale, double[][] Calibration, double[][]? CalibrationKept = null);

    /// <summary>An exported edge and the probabilities the fit gave it, so a test can hold this code to the fit.</summary>
    internal sealed record CombinerCheck(
        string Group,
        double[] Features,
        double Raw,
        double Calibrated,
        [property: JsonPropertyName("where")] string? Where = null,
        bool Current = false);
}
