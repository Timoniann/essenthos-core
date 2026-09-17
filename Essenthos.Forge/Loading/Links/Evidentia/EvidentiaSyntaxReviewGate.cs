using Essenthos.Core.Loading.Links;

namespace Essenthos.Core.Loading.Links.Evidentia;

/// <summary>
/// Narrows review-only lexical hints with structure already stated by the target text. It needs
/// trusted lexical anchors to judge a candidate and cannot create a lexical candidate by itself.
/// </summary>
internal sealed class EvidentiaSyntaxReviewGate
{
    public IReadOnlySet<(long SourceId, long TargetId)> ClauseCohesiveTargetGlossCandidates(
        IEnumerable<EvidentiaCandidate> candidates,
        IEnumerable<EvidentiaProposal> anchors,
        SyntaxPrior syntax)
    {
        if (!syntax.Known)
        {
            return new HashSet<(long, long)>();
        }

        var anchorByAddress = anchors
            .GroupBy(proposal => proposal.Source.Token.Address)
            .ToDictionary(group => group.Key, group => group.ToList());
        var accepted = new HashSet<(long SourceId, long TargetId)>();
        foreach (var verse in candidates
                     .Where(IsExactTargetGloss)
                     .GroupBy(candidate => candidate.Source.Token.Address))
        {
            if (!anchorByAddress.TryGetValue(verse.Key, out var verseAnchors) || verseAnchors.Count == 0)
            {
                continue;
            }

            var targetIds = verse.Select(candidate => candidate.Target.Token.Id)
                .Concat(verseAnchors.Select(proposal => proposal.Target.Token.Id))
                .Distinct()
                .ToList();
            var targetIndex = targetIds.Select((id, index) => (id, index))
                .ToDictionary(item => item.id, item => item.index);
            var review = verse.ToList();
            var scored = verseAnchors.Select(proposal =>
                    (Source: proposal.Source.Token.Position,
                        Target: targetIndex[proposal.Target.Token.Id],
                        Confidence: proposal.Confidence,
                        Position: 0d))
                .Concat(review.Select(candidate =>
                    (Source: candidate.Source.Token.Position,
                        Target: targetIndex[candidate.Target.Token.Id],
                        Confidence: EvidentiaDefaults.TargetGlossReviewConfidence,
                        Position: 0d)))
                .ToList();
            var cohesion = syntax.Judge(scored, targetIds);
            for (var index = verseAnchors.Count; index < scored.Count; index++)
            {
                if (cohesion[index] is Cohesion.Clause or Cohesion.Phrase)
                {
                    var candidate = review[index - verseAnchors.Count];
                    accepted.Add((candidate.Source.Token.Id, candidate.Target.Token.Id));
                }
            }
        }

        return accepted;
    }

    private static bool IsExactTargetGloss(EvidentiaCandidate candidate) =>
        candidate.Evidence.Any(evidence => evidence.Kind == EvidentiaEvidenceKind.ExactCanonicalAddress)
        && candidate.Evidence.Any(evidence => evidence.Kind == EvidentiaEvidenceKind.TargetGloss);
}
