using System.Globalization;

namespace Essenthos.Core.Loading.Links.Evidentia;

/// <summary>
/// The second pass: a word the first pass left unplaced goes on the one free word of its verse that the
/// text's own placements, counted over the whole text, say its form renders. <em>livestock</em> reached
/// בְּהֵמָה forty times where a gloss in context named it; in the verse where two nouns shared that gloss
/// the first pass held it back, and the forty are the evidence it lacked there.
///
/// The lexeme must stand free in the verse exactly once and be claimed by exactly one unplaced word. On
/// the counts alone the pair must have been placed <see cref="MinimumPlacements"/> times and be where
/// <see cref="MinimumShare"/> of the form's placements went, which few pairs are: a form the first pass
/// placed that consistently it has usually placed here as well. Where the statistical aligner names the
/// same pair one earlier placement is enough, and where it links the word elsewhere nothing is placed.
/// </summary>
internal static class EvidentiaSecondPass
{
    public const int MinimumPlacements = EvidentiaDefaults.SecondPassPlacements;

    public const double MinimumShare = EvidentiaDefaults.SecondPassShare;

    /// <summary>A review-tier confidence: the pair is the text's habit, not something this verse showed.</summary>
    public const double Confidence = EvidentiaDefaults.TargetGlossReviewConfidence;

    public static IReadOnlyList<EvidentiaProposal> Resolve(
        IReadOnlyList<EvidentiaAnalysis> source,
        IReadOnlyList<EvidentiaAnalysis> target,
        IReadOnlyList<EvidentiaProposal> reserved,
        EvidentiaConfirmedIndex confirmed,
        EvidentiaVerseFrame frame,
        EvidentiaAlignerPairs? aligner = null)
    {
        var placed = reserved.Select(proposal => proposal.Source.Token.Id).ToHashSet();
        var taken = reserved.Select(proposal => proposal.Target.Token.Id).ToHashSet();
        var freeByVerse = EvidentiaAuxiliaryWords.Mark([.. target.DistinctBy(analysis => analysis.Token.Id)])
            .Where(analysis => analysis.Token.StrongNumber is not null && !taken.Contains(analysis.Token.Id))
            .GroupBy(analysis => analysis.Token.Address)
            .ToDictionary(verse => verse.Key, verse => verse.OrderBy(analysis => analysis.Token.Position).ToList());
        var proposals = new List<EvidentiaProposal>();
        foreach (var verse in EvidentiaAuxiliaryWords.Mark([.. source.DistinctBy(analysis => analysis.Token.Id)])
                     .Where(analysis => analysis.IsContentWord && !placed.Contains(analysis.Token.Id))
                     .GroupBy(analysis => analysis.Token.Address))
        {
            if (!freeByVerse.TryGetValue(verse.Key, out var free))
            {
                continue;
            }

            var claims = verse
                .Select(word => ClaimOf(word, free, confirmed))
                .OfType<Claim>()
                .GroupBy(claim => claim.StrongNumber, StringComparer.Ordinal);
            foreach (var lexeme in claims)
            {
                var occurrences = free.Where(word => word.Token.StrongNumber == lexeme.Key).ToList();
                if (lexeme.Count() != 1 || occurrences.Count != 1)
                {
                    continue;
                }

                var claim = lexeme.Single();
                var occurrence = occurrences[0];
                var agreed = aligner?.Agrees(claim.Word.Token.Id, occurrence.Token.Id) == true;
                var standsAlone = claim.Rendering.Placed >= MinimumPlacements && claim.Rendering.Share >= MinimumShare
                    && aligner?.Contradicts(claim.Word.Token.Id, occurrence.Token.Id) != true;
                var proposal = new EvidentiaProposal(
                    claim.Word,
                    occurrence,
                    EvidentiaProposalKind.ConfirmedRendering,
                    Confidence,
                    new EvidentiaDecisionTrace("review", Rationale(claim.Rendering, agreed), []));
                if ((agreed || standsAlone)
                    && Admissible(claim.Word, occurrence)
                    && frame.Distance(proposal) < EvidentiaVerseFrame.MaximumDistance)
                {
                    proposals.Add(proposal);
                }
            }
        }

        return proposals;
    }

    /// <summary>The lexeme free in the verse that the form was placed on most often; none where two tie.</summary>
    private static Claim? ClaimOf(
        EvidentiaAnalysis word,
        IReadOnlyList<EvidentiaAnalysis> free,
        EvidentiaConfirmedIndex confirmed)
    {
        var standing = confirmed.Of(word)
            .Where(rendering => free.Any(occurrence => occurrence.Token.StrongNumber == rendering.StrongNumber))
            .OrderByDescending(rendering => rendering.Rendering.Placed)
            .Take(2)
            .ToList();
        return standing.Count == 0 || (standing.Count == 2 && standing[0].Rendering.Placed == standing[1].Rendering.Placed)
            ? null
            : new Claim(word, standing[0].StrongNumber, standing[0].Rendering);
    }

    private static bool Admissible(EvidentiaAnalysis word, EvidentiaAnalysis occurrence)
    {
        var pair = new EvidentiaCandidate(word, occurrence, []);
        return !pair.PairsAContentWordWithAFunctionWord
            && !pair.PlacesAnAuxiliaryWordOffItsKind
            && !EvidentiaTenseAgreement.Disagrees(word, occurrence);
    }

    private static string Rationale(ConfirmedRendering rendering, bool agreed) => string.Create(
        CultureInfo.InvariantCulture,
        $"the text puts the form on this lexeme in {rendering.Placed} of its {rendering.FormPlaced} placements ({rendering.Safe} in the safe tier), and it stands free once in the verse") + (agreed ? "; the statistical aligner names the same pair" : string.Empty);

    private sealed record Claim(EvidentiaAnalysis Word, string StrongNumber, ConfirmedRendering Rendering);
}
