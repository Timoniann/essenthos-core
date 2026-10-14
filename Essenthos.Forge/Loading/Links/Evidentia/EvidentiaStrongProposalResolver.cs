using Essenthos.Core.Strong;

namespace Essenthos.Core.Loading.Links.Evidentia;

/// <summary>
/// Turns the Strong-number portion of the evidence graph into conservative, read-only proposals.
/// A Strong number identifies a lexeme, not an occurrence; therefore repeated occurrences are
/// proposed only when the two exact canonical verses contain the same count, in their written
/// order. Unequal counts remain unresolved for later dictionary, syntax or statistical evidence.
///
/// The count is per lexeme, so the grouping is on the normalised number rather than on the tag as
/// written. g0570, G0570 and G570 are one number, and the evidence source has already treated them
/// as one; grouping on the spelling would let each of two spellings see one occurrence, call itself
/// unique, and point two source words at the same target word at the confidence that uniqueness is
/// what earns.
///
/// The two confidences are StrongNumberMatch's own. They are the same two shapes it already
/// reasons about, and a second copy of the numbers would be a second ladder to keep in step. Its
/// Verse method is not called here because it reads a tagged edition's rows and writes links,
/// where this reads candidate edges and writes proposals; what is shared is the ladder, which is
/// the part that has to agree.
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
            .GroupBy(candidate => (
                candidate.Source.Token.Address,
                Number: StrongNumbers.Normalize(candidate.Source.Token.StrongNumber)))
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
            var confidence = kind == EvidentiaProposalKind.UniqueSharedStrong
                ? StrongNumberMatch.Unambiguous
                : StrongNumberMatch.PairedInOrder;
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

    /// <summary>A grammatical word placed by the proposal of the word it belongs to.</summary>
    AttachedWord,

    /// <summary>
    /// A learned rendering too rare to pass the review floor, placed because it is the only sense of the
    /// word in the verse and nothing else in the verse claims its one occurrence.
    /// </summary>
    ResidualKnownRendering,

    /// <summary>
    /// A word with some lexical evidence for the one free word of its class the original leaves
    /// between the renderings of its placed neighbours.
    /// </summary>
    AnchoredGapReview,

    /// <summary>A word the verse repeats, placed on as many free occurrences of its one lexeme, in order.</summary>
    RepeatedRenderingInOrder,

    /// <summary>A noun, name or adjective on the one free word of its verse whose gloss in context names it, and no other.</summary>
    UniqueContextGlossReview,

    /// <summary>An open-class word whose only free candidate a dictionary sense and the original's own gloss both name.</summary>
    DictionaryAndGlossReview,

    /// <summary><em>you</em> or <em>but</em> on the one free word of its verse that is its own counterpart.</summary>
    UniqueCounterpart,

    /// <summary>A word on the word of the original annotated as the same entity, in the order the verse names it.</summary>
    SharedEntityInOrder,

    /// <summary>A word on the one free lexeme of its verse that the text's own placements say its form renders.</summary>
    ConfirmedRendering,

    /// <summary>An unplaced word on the word the statistical aligner pairs it with, where a dictionary sense or gloss names the same pair.</summary>
    AlignerAndLexicalEvidence,
}

/// <param name="Head">For an attached word, the proposal of the word it belongs to.</param>
internal sealed record EvidentiaProposal(
    EvidentiaAnalysis Source,
    EvidentiaAnalysis Target,
    EvidentiaProposalKind Kind,
    double Confidence,
    EvidentiaDecisionTrace? Trace = null,
    EvidentiaProposal? Head = null);

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
