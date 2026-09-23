namespace Essenthos.Core.Loading.Links.Evidentia;

/// <summary>Promotes only reciprocal, exact-verse dictionary-sense candidates to the review queue.</summary>
internal sealed class EvidentiaDictionaryProposalResolver
{
    private const double ReviewConfidence = EvidentiaDefaults.DictionaryReviewConfidence;

    public EvidentiaResolution ResolveAdditional(IEnumerable<EvidentiaCandidate> candidates, IEnumerable<EvidentiaProposal> reserved)
    {
        var sources = reserved.Select(proposal => proposal.Source.Token.Id).ToHashSet();
        var targets = reserved.Select(proposal => proposal.Target.Token.Id).ToHashSet();
        var proposals = new List<EvidentiaProposal>();
        foreach (var verse in candidates.Where(IsExactDictionary).GroupBy(candidate => candidate.Source.Token.Address))
        {
            var reciprocal = verse.GroupBy(candidate => candidate.Target.Token.Id)
                .Where(group => group.Select(candidate => candidate.Source.Token.Id).Distinct().Count() == 1)
                .SelectMany(group => group).GroupBy(candidate => candidate.Source.Token.Id)
                .Where(group => group.Select(candidate => candidate.Target.Token.Id).Distinct().Count() == 1)
                .Select(group => group.First());
            foreach (var candidate in reciprocal)
            {
                if (!sources.Add(candidate.Source.Token.Id) || !targets.Add(candidate.Target.Token.Id)) continue;
                proposals.Add(new EvidentiaProposal(candidate.Source, candidate.Target,
                    EvidentiaProposalKind.UniqueDictionarySenseReview, ReviewConfidence,
                    EvidentiaDecisionTrace.For(candidate, "review", "reciprocal exact-verse dictionary sense after stronger reservations")));
            }
        }
        return new EvidentiaResolution(proposals, 0);
    }

    /// <summary>
    /// The classes a definition names by their own meaning. A pronoun, an adverb, a preposition or a
    /// conjunction turns up in the wording of a great many definitions without being what they define,
    /// so a match on one says nothing about the verse; a word the parser did not classify is kept.
    /// </summary>
    private static readonly HashSet<string> OpenClasses = ["noun", "propn", "verb", "adj", "num"];

    private static bool IsOpenClass(EvidentiaAnalysis source) =>
        EvidentiaMorphologyLabels.PartOfSpeech(source.PartOfSpeech, source.Token.Language) is not { } partOfSpeech
        || OpenClasses.Contains(partOfSpeech);

    private static bool IsExactDictionary(EvidentiaCandidate candidate) =>
        IsOpenClass(candidate.Source)
        && !candidate.PairsAContentWordWithAFunctionWord
        && !candidate.PlacesAnAuxiliaryWordOffItsKind
        && candidate.Evidence.Any(e => e.Kind == EvidentiaEvidenceKind.ExactCanonicalAddress)
        && candidate.Evidence.Any(e => e.Kind == EvidentiaEvidenceKind.DictionarySense);
}
