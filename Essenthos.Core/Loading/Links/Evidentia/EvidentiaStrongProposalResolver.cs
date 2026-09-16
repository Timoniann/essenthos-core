namespace Essenthos.Core.Loading.Links.Evidentia;

/// <summary>
/// Turns the Strong-number portion of the evidence graph into conservative, read-only proposals.
/// A Strong number identifies a lexeme, not an occurrence; therefore repeated occurrences are
/// proposed only when the two exact canonical verses contain the same count, in their written
/// order. Unequal counts remain unresolved for later dictionary, syntax or statistical evidence.
/// </summary>
internal sealed class EvidentiaStrongProposalResolver
{
    public EvidentiaResolution Resolve(IEnumerable<EvidentiaCandidate> candidates)
    {
        var direct = candidates
            .Where(candidate => candidate.Evidence.Any(evidence =>
                evidence.Kind == EvidentiaEvidenceKind.SharedStrongNumber))
            .Where(candidate => candidate.Evidence.Any(evidence =>
                evidence.Kind == EvidentiaEvidenceKind.ExactCanonicalAddress))
            .GroupBy(candidate => (candidate.Source.Token.Address, candidate.Source.Token.StrongNumber))
            .ToList();

        var proposals = new List<EvidentiaProposal>();
        var unresolved = 0;
        foreach (var group in direct)
        {
            var sources = group.Select(candidate => candidate.Source)
                .DistinctBy(analysis => analysis.Token.Id)
                .OrderBy(analysis => analysis.Token.Position)
                .ToList();
            var targets = group.Select(candidate => candidate.Target)
                .DistinctBy(analysis => analysis.Token.Id)
                .OrderBy(analysis => analysis.Token.Position)
                .ToList();

            if (sources.Count != targets.Count)
            {
                unresolved += sources.Count;
                continue;
            }

            var kind = sources.Count == 1
                ? EvidentiaProposalKind.UniqueSharedStrong
                : EvidentiaProposalKind.SharedStrongInOrder;
            var confidence = kind == EvidentiaProposalKind.UniqueSharedStrong ? 0.90 : 0.70;
            for (var index = 0; index < sources.Count; index++)
            {
                var candidate = group.First(candidate => candidate.Source.Token.Id == sources[index].Token.Id
                    && candidate.Target.Token.Id == targets[index].Token.Id);
                proposals.Add(new EvidentiaProposal(sources[index], targets[index], kind, confidence,
                    EvidentiaDecisionTrace.For(candidate, "safe", "exact shared Strong number; equal occurrence count")));
            }
        }

        return new EvidentiaResolution(proposals, unresolved);
    }
}

internal enum EvidentiaProposalKind
{
    UniqueSharedStrong,
    SharedStrongInOrder,
    StableKnownRendering,
    ReviewKnownRendering,
    GlobalStableKnownRendering,
    GlobalReviewKnownRendering,
    UniqueTargetGlossReview,
    UniqueDictionarySenseReview,
    GlobalAssignmentReview,
}

internal sealed record EvidentiaProposal(
    EvidentiaAnalysis Source,
    EvidentiaAnalysis Target,
    EvidentiaProposalKind Kind,
    double Confidence,
    EvidentiaDecisionTrace? Trace = null);

internal sealed record EvidentiaDecisionTrace(
    string Tier,
    string Rationale,
    IReadOnlyList<EvidentiaEvidence> Evidence)
{
    public static EvidentiaDecisionTrace For(EvidentiaCandidate candidate, string tier, string rationale) =>
        new(tier, rationale, candidate.Evidence.OrderByDescending(evidence => evidence.Score).ToList());
}

internal sealed record EvidentiaResolution(
    IReadOnlyList<EvidentiaProposal> Proposals,
    int UnresolvedSourceWords);
