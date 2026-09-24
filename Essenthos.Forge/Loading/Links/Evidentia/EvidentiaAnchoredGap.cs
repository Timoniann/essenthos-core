namespace Essenthos.Core.Loading.Links.Evidentia;

/// <summary>
/// Places a word the lexical tiers left between two placed neighbours, where the original leaves it
/// one answer: the neighbours' renderings stand in the same verse in the same order, exactly one free
/// word of the original stands between them that is not a grammatical word, it is of the word's own
/// class, and the word has some lexical evidence for it - a learned rendering, a dictionary sense or
/// the witness's gloss, however weak. The order of the neighbours is what the verse contributes;
/// the evidence is what keeps a translator's addition from being placed on whatever the gap holds.
/// </summary>
internal static class EvidentiaAnchoredGap
{
    private const double Confidence = EvidentiaDefaults.DictionaryReviewConfidence;

    private static readonly HashSet<EvidentiaEvidenceKind> LexicalEvidence =
    [
        EvidentiaEvidenceKind.KnownRendering,
        EvidentiaEvidenceKind.DictionarySense,
        EvidentiaEvidenceKind.TargetGloss,
        EvidentiaEvidenceKind.MatchingNormalisedForm,
    ];

    /// <summary>The classes that may stand in for one another across the two languages.</summary>
    private static readonly Dictionary<string, string[]> Counterparts = new(StringComparer.Ordinal)
    {
        ["noun"] = ["noun", "propn"],
        ["propn"] = ["propn", "noun"],
        ["verb"] = ["verb"],
        ["adj"] = ["adj", "noun"],
        ["num"] = ["num", "noun", "adj"],
        ["adv"] = ["adv"],
    };

    public static EvidentiaResolution Resolve(
        IReadOnlyList<EvidentiaAnalysis> source,
        IReadOnlyList<EvidentiaAnalysis> target,
        IEnumerable<EvidentiaCandidate> candidates,
        IReadOnlyList<EvidentiaProposal> reserved)
    {
        var placedBySource = reserved
            .GroupBy(proposal => proposal.Source.Token.Id)
            .ToDictionary(group => group.Key, group => group.First().Target);
        var taken = reserved.Select(proposal => proposal.Target.Token.Id).ToHashSet();
        var supported = candidates
            .Where(candidate => candidate.Evidence.Any(evidence => evidence.Kind == EvidentiaEvidenceKind.ExactCanonicalAddress)
                && candidate.Evidence.Any(evidence => LexicalEvidence.Contains(evidence.Kind))
                && !candidate.PairsAContentWordWithAFunctionWord
                && !candidate.PlacesAnAuxiliaryWordOffItsKind)
            .GroupBy(candidate => (candidate.Source.Token.Id, candidate.Target.Token.Id))
            .ToDictionary(group => group.Key, group => group.First());
        var targetsByVerse = target
            .DistinctBy(analysis => analysis.Token.Id)
            .GroupBy(analysis => analysis.Token.Address)
            .ToDictionary(group => group.Key, group => group.OrderBy(analysis => analysis.Token.Position).ToList());
        var proposals = new List<EvidentiaProposal>();
        foreach (var verse in source.DistinctBy(analysis => analysis.Token.Id).GroupBy(analysis => analysis.Token.Address))
        {
            var words = verse.OrderBy(analysis => analysis.Token.Position).ToList();
            for (var index = 0; index < words.Count; index++)
            {
                var word = words[index];
                if (placedBySource.ContainsKey(word.Token.Id)
                    || Class(word) is not { } wordClass
                    || !Counterparts.TryGetValue(wordClass, out var counterparts)
                    || Anchor(words, index, -1, placedBySource) is not { } left
                    || Anchor(words, index, +1, placedBySource) is not { } right
                    || left.Token.Address != right.Token.Address
                    || !targetsByVerse.TryGetValue(left.Token.Address, out var targetVerse))
                {
                    continue;
                }

                var gap = targetVerse
                    .Where(other => other.Token.Position > left.Token.Position && other.Token.Position < right.Token.Position
                        && !taken.Contains(other.Token.Id)
                        && EvidentiaMorphologyLabels.IsFunctionWord(other.PartOfSpeech, other.Token.Language) != true)
                    .ToList();
                if (gap.Count != 1
                    || Class(gap[0]) is not { } gapClass || !counterparts.Contains(gapClass)
                    || !supported.TryGetValue((word.Token.Id, gap[0].Token.Id), out var candidate))
                {
                    continue;
                }

                taken.Add(gap[0].Token.Id);
                placedBySource[word.Token.Id] = gap[0];
                proposals.Add(new EvidentiaProposal(
                    candidate.Source,
                    candidate.Target,
                    EvidentiaProposalKind.AnchoredGapReview,
                    Confidence,
                    EvidentiaDecisionTrace.For(candidate, "review",
                        "the one free word of its class between the renderings of its placed neighbours")));
            }
        }

        return new EvidentiaResolution(proposals, 0);
    }

    /// <summary>
    /// The rendering of the nearest placed word on one side, past unplaced grammatical words only: an
    /// unplaced word with a meaning of its own between them is a second unknown, and the gap is then
    /// not one word wide.
    /// </summary>
    private static EvidentiaAnalysis? Anchor(
        IReadOnlyList<EvidentiaAnalysis> words,
        int index,
        int step,
        IReadOnlyDictionary<long, EvidentiaAnalysis> placedBySource)
    {
        for (var next = index + step; next >= 0 && next < words.Count; next += step)
        {
            if (placedBySource.TryGetValue(words[next].Token.Id, out var rendering))
            {
                return rendering;
            }

            if (Class(words[next]) is "noun" or "propn" or "verb" or "adj" or "adv" or "num" or "pron")
            {
                return null;
            }
        }

        return null;
    }

    private static string? Class(EvidentiaAnalysis word) =>
        EvidentiaMorphologyLabels.PartOfSpeech(word.PartOfSpeech ?? word.Token.PartOfSpeech, word.Token.Language);
}
