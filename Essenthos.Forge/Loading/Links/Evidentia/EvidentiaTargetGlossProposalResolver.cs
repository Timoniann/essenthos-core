namespace Essenthos.Core.Loading.Links.Evidentia;

/// <summary>
/// Returns only exact-verse target-gloss candidates with one possible target occurrence after the
/// stronger global rendering proposals have reserved their words. A BHSA gloss is dictionary
/// evidence, not a translation assertion, so every result stays in the review tier.
/// </summary>
internal sealed class EvidentiaTargetGlossProposalResolver
{
    private const double ReviewConfidence = EvidentiaDefaults.TargetGlossReviewConfidence;

    public EvidentiaResolution ResolveAdditional(
        IEnumerable<EvidentiaCandidate> candidates,
        IEnumerable<EvidentiaProposal> reserved,
        IReadOnlySet<(long SourceId, long TargetId)>? allowed = null)
    {
        var reservedSources = reserved.Select(proposal => proposal.Source.Token.Id).ToHashSet();
        var reservedTargets = reserved.Select(proposal => proposal.Target.Token.Id).ToHashSet();
        var proposals = new List<EvidentiaProposal>();
        foreach (var verse in candidates
                     .Where(candidate => IsExactTargetGloss(candidate)
                         && !reservedSources.Contains(candidate.Source.Token.Id)
                         && !reservedTargets.Contains(candidate.Target.Token.Id)
                         && (allowed is null || allowed.Contains((candidate.Source.Token.Id, candidate.Target.Token.Id))))
                     .GroupBy(candidate => candidate.Source.Token.Address))
        {
            // A dictionary gloss such as "serve" can be shared by several Hebrew words in one
            // verse. Source uniqueness alone would then select whichever occurrence happened to
            // come first. Keep this review tier only where the relation is unique in both
            // directions before reservations are applied.
            var reciprocal = verse
                .GroupBy(candidate => candidate.Target.Token.Id)
                .Where(group => group.Select(candidate => candidate.Source.Token.Id).Distinct().Count() == 1)
                .SelectMany(group => group)
                .ToList();
            var choices = reciprocal
                .GroupBy(candidate => candidate.Source.Token.Id)
                .Select(group => group
                    .DistinctBy(candidate => candidate.Target.Token.Id)
                    .ToList())
                .Where(group => group.Count == 1)
                .Select(group => group.Single())
                .OrderBy(candidate => candidate.Source.Token.Position)
                .ThenBy(candidate => candidate.Target.Token.Position);

            foreach (var candidate in choices)
            {
                if (!reservedSources.Add(candidate.Source.Token.Id)
                    || !reservedTargets.Add(candidate.Target.Token.Id))
                {
                    continue;
                }

                proposals.Add(new EvidentiaProposal(
                    candidate.Source,
                    candidate.Target,
                    EvidentiaProposalKind.UniqueTargetGlossReview,
                    ReviewConfidence,
                    EvidentiaDecisionTrace.For(candidate, "review",
                        "unique exact-verse target gloss after stronger reservations")));
            }
        }

        return new EvidentiaResolution(proposals, 0);
    }

    // A gloss on a Hebrew article is a grammatical note, not a claim that some translated content
    // word renders that occurrence, so the witness's own class keeps it out of the queue.
    private static bool IsExactTargetGloss(EvidentiaCandidate candidate) =>
        !candidate.PairsAContentWordWithAFunctionWord
        && !candidate.PlacesAnAuxiliaryWordOffItsKind
        && candidate.Evidence.Any(evidence => evidence.Kind == EvidentiaEvidenceKind.ExactCanonicalAddress)
        && candidate.Evidence.Any(evidence => evidence.Kind == EvidentiaEvidenceKind.TargetGloss);

}

/// <summary>
/// The placements the witness's own word gloss licenses: the unique target gloss, the dictionary sense
/// the gloss confirms, and the one free word between placed neighbours where the gloss is what names it.
/// The gloss is a publisher's rendering - ETCBC's on BHSA, the Berean Interlinear's on Nestle 1904 - so
/// measured against a key from the same publisher these placements read the key's own words, and are
/// scored apart from the rest.
/// </summary>
internal static class EvidentiaGlossLicence
{
    public static bool ReadsTheWitnessGloss(EvidentiaProposal proposal) => proposal.Kind switch
    {
        EvidentiaProposalKind.UniqueTargetGlossReview or EvidentiaProposalKind.DictionaryAndGlossReview => true,
        EvidentiaProposalKind.AnchoredGapReview =>
            proposal.Trace?.Evidence.Any(evidence => evidence.Kind == EvidentiaEvidenceKind.TargetGloss) == true,
        _ => false,
    };
}
