namespace Essenthos.Core.Loading.Links.Evidentia;

/// <summary>
/// Places a word the verse repeats where the original repeats its rendering as often: <em>expanse</em>
/// three times in Genesis 1:7 and רָקִיעַ three times, in the order both write them. The review tiers
/// that read a dictionary sense or a gloss ask for one word on each side, and a repeated word has
/// several; the count on both sides and the order are what the verse adds. Every occurrence must be
/// the same word with evidence for that one lexeme only, and the original must leave exactly as many
/// occurrences of it free.
/// </summary>
internal static class EvidentiaRepeatedRendering
{
    private const double Confidence = EvidentiaDefaults.DictionaryReviewConfidence;

    private const int LeastRepetition = 2;

    private static readonly HashSet<EvidentiaEvidenceKind> LexicalEvidence =
    [
        EvidentiaEvidenceKind.KnownRendering,
        EvidentiaEvidenceKind.DictionarySense,
        EvidentiaEvidenceKind.TargetGloss,
        EvidentiaEvidenceKind.MatchingNormalisedForm,
    ];

    public static EvidentiaResolution Resolve(
        IReadOnlyList<EvidentiaAnalysis> target,
        IEnumerable<EvidentiaCandidate> candidates,
        IReadOnlyList<EvidentiaProposal> reserved)
    {
        var placed = reserved.Select(proposal => proposal.Source.Token.Id).ToHashSet();
        var taken = reserved.Select(proposal => proposal.Target.Token.Id).ToHashSet();
        var targetsByVerse = target
            .DistinctBy(analysis => analysis.Token.Id)
            .GroupBy(analysis => analysis.Token.Address)
            .ToDictionary(group => group.Key, group => group.OrderBy(analysis => analysis.Token.Position).ToList());
        var proposals = new List<EvidentiaProposal>();
        foreach (var verse in candidates
                     .Where(candidate => candidate.Target.Token.StrongNumber is not null
                         && !placed.Contains(candidate.Source.Token.Id)
                         && !taken.Contains(candidate.Target.Token.Id)
                         && candidate.Evidence.Any(evidence => evidence.Kind == EvidentiaEvidenceKind.ExactCanonicalAddress)
                         && candidate.Evidence.Any(evidence => LexicalEvidence.Contains(evidence.Kind))
                         && !candidate.PairsAContentWordWithAFunctionWord
                         && !candidate.PlacesAnAuxiliaryWordOffItsKind
                         && EvidentiaMorphologyLabels.AreCounterparts(candidate.Source, candidate.Target))
                     .GroupBy(candidate => candidate.Source.Token.Address))
        {
            if (!targetsByVerse.TryGetValue(verse.Key, out var targetVerse))
            {
                continue;
            }

            var bySense = verse
                .GroupBy(candidate => candidate.Source.Token.Id)
                .Where(word => word.Select(candidate => candidate.Target.Token.StrongNumber).Distinct().Count() == 1)
                .GroupBy(word => word.First().Target.Token.StrongNumber!);
            foreach (var sense in bySense)
            {
                var words = sense.Select(word => word.First()).OrderBy(candidate => candidate.Source.Token.Position).ToList();
                var occurrences = targetVerse
                    .Where(word => word.Token.StrongNumber == sense.Key && !taken.Contains(word.Token.Id))
                    .ToList();
                if (words.Count < LeastRepetition || words.Count != occurrences.Count
                    || words.Select(candidate => candidate.Source.Lemma ?? candidate.Source.Normalised).Distinct().Count() != 1)
                {
                    continue;
                }

                var pairs = words.Zip(occurrences, (word, occurrence) => sense
                        .Single(group => group.Key == word.Source.Token.Id)
                        .FirstOrDefault(edge => edge.Target.Token.Id == occurrence.Token.Id))
                    .ToList();
                if (pairs.Any(candidate => candidate is null))
                {
                    continue;
                }

                foreach (var candidate in pairs.OfType<EvidentiaCandidate>())
                {
                    taken.Add(candidate.Target.Token.Id);
                    proposals.Add(new EvidentiaProposal(
                        candidate.Source,
                        candidate.Target,
                        EvidentiaProposalKind.RepeatedRenderingInOrder,
                        Confidence,
                        EvidentiaDecisionTrace.For(candidate, "review",
                            "a word the verse repeats, on the occurrences of its one lexeme in the same order")));
                }
            }
        }

        return new EvidentiaResolution(proposals, 0);
    }
}
