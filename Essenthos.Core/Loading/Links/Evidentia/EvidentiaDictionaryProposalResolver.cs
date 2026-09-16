namespace Essenthos.Core.Loading.Links.Evidentia;

/// <summary>Promotes only reciprocal, exact-verse dictionary-sense candidates to the review queue.</summary>
internal sealed class EvidentiaDictionaryProposalResolver
{
    private const double ReviewConfidence = 0.40;

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

    private static bool IsExactDictionary(EvidentiaCandidate candidate) =>
        candidate.Evidence.Any(e => e.Kind == EvidentiaEvidenceKind.ExactCanonicalAddress)
        && candidate.Evidence.Any(e => e.Kind == EvidentiaEvidenceKind.DictionarySense);
}
