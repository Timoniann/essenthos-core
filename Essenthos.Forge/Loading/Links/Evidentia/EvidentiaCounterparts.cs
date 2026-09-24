namespace Essenthos.Core.Loading.Links.Evidentia;

/// <summary>
/// Words placed by evidence that agrees with itself rather than by a learned rendering: a grammatical
/// word on the one free word of its verse that is its own counterpart in the original (<em>you</em> on
/// אַתָּה, <em>but</em> on ἀλλά) where no other unplaced word of the verse claims it, and a word whose only
/// free candidate two lexical sources name.
/// </summary>
internal static class EvidentiaCounterparts
{
    private const double Confidence = 0.5;

    /// <summary>
    /// The words a counterpart is sought for, and their counterparts: אַתָּה, σύ and ὑμεῖς; ἀλλά and δέ.
    /// Measured the same way, <em>for</em> reached 91%, <em>with</em> 92%, <em>I</em> 82%, <em>and</em>
    /// 85%, <em>who</em> 73%, <em>from</em> 75%, <em>in</em> 69%, <em>to</em> 51% and <em>he</em> 42%.
    /// </summary>
    private static readonly Dictionary<string, string[]> Table = new(StringComparer.OrdinalIgnoreCase)
    {
        ["you"] = ["H859", "G4771", "G5210"],
        ["but"] = ["G235", "G1161"],
    };

    public static IReadOnlyList<EvidentiaProposal> Resolve(
        IReadOnlyList<EvidentiaAnalysis> source,
        IReadOnlyList<EvidentiaAnalysis> target,
        IReadOnlyList<EvidentiaProposal> placed)
    {
        var placedSources = placed.Select(proposal => proposal.Source.Token.Id).ToHashSet();
        var taken = placed.Select(proposal => proposal.Target.Token.Id).ToHashSet();
        var targetsByVerse = target
            .DistinctBy(word => word.Token.Id)
            .Where(word => !taken.Contains(word.Token.Id) && word.Token.StrongNumber is not null)
            .GroupBy(word => word.Token.Address)
            .ToDictionary(group => group.Key, group => group.ToList());
        var proposals = new List<EvidentiaProposal>();
        foreach (var verse in source
                     .DistinctBy(word => word.Token.Id)
                     .Where(word => !placedSources.Contains(word.Token.Id) && Table.ContainsKey(word.Token.Surface))
                     .GroupBy(word => word.Token.Address))
        {
            if (!targetsByVerse.TryGetValue(verse.Key, out var targets))
            {
                continue;
            }

            var claims = verse
                .SelectMany(word => targets
                    .Where(other => Table[word.Token.Surface].Contains(other.Token.StrongNumber!))
                    .Select(other => (Source: word, Target: other)))
                .ToList();
            foreach (var claim in claims)
            {
                if (claims.Count(other => other.Source.Token.Id == claim.Source.Token.Id) != 1
                    || claims.Count(other => other.Target.Token.Id == claim.Target.Token.Id) != 1)
                {
                    continue;
                }

                proposals.Add(new EvidentiaProposal(
                    claim.Source,
                    claim.Target,
                    EvidentiaProposalKind.UniqueCounterpart,
                    Confidence,
                    new EvidentiaDecisionTrace("review", "the one free counterpart in the verse, and no other word claims it", [])));
            }
        }

        return proposals;
    }

    /// <summary>
    /// An open-class word whose only free candidate in the verse is named both by a dictionary sense
    /// and by the original's own gloss: two lexical sources agreeing on the one word left.
    /// </summary>
    public static IReadOnlyList<EvidentiaProposal> DictionaryAndGloss(
        IReadOnlyList<EvidentiaCandidate> candidates,
        IReadOnlyList<EvidentiaProposal> placed)
    {
        var placedSources = placed.Select(proposal => proposal.Source.Token.Id).ToHashSet();
        var taken = placed.Select(proposal => proposal.Target.Token.Id).ToHashSet();
        var proposals = new List<EvidentiaProposal>();
        foreach (var word in candidates
                     .Where(candidate => !placedSources.Contains(candidate.Source.Token.Id)
                         && !taken.Contains(candidate.Target.Token.Id)
                         && !candidate.PairsAContentWordWithAFunctionWord
                         && !candidate.PlacesAnAuxiliaryWordOffItsKind
                         && EvidentiaMorphologyLabels.IsOpenClass(candidate.Source.PartOfSpeech, candidate.Source.Token.Language)
                         && candidate.Evidence.Any(evidence => evidence.Kind == EvidentiaEvidenceKind.ExactCanonicalAddress)
                         && candidate.Evidence.Any(evidence => evidence.Kind is EvidentiaEvidenceKind.KnownRendering
                             or EvidentiaEvidenceKind.DictionarySense or EvidentiaEvidenceKind.TargetGloss))
                     .GroupBy(candidate => candidate.Source.Token.Id))
        {
            var free = word.DistinctBy(candidate => candidate.Target.Token.Id).ToList();
            if (free.Count != 1
                || !free[0].Evidence.Any(evidence => evidence.Kind == EvidentiaEvidenceKind.DictionarySense)
                || !free[0].Evidence.Any(evidence => evidence.Kind == EvidentiaEvidenceKind.TargetGloss))
            {
                continue;
            }

            proposals.Add(new EvidentiaProposal(
                free[0].Source,
                free[0].Target,
                EvidentiaProposalKind.DictionaryAndGlossReview,
                EvidentiaDefaults.TargetGlossReviewConfidence,
                EvidentiaDecisionTrace.For(free[0], "review", "dictionary sense and gloss agree on the only free candidate")));
        }

        // Two words may have found the same target; neither is then the only answer.
        return [.. proposals.GroupBy(proposal => proposal.Target.Token.Id).Where(group => group.Count() == 1).Select(group => group.Single())];
    }
}
