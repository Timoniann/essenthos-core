using Essenthos.Core.Strong;

namespace Essenthos.Core.Loading.Links.Evidentia;

/// <summary>
/// A source-provided Strong number on both sides is a direct anchor, not a learned translation.
/// This service only exposes the evidence already carried by the two loaded words; it does not
/// create or persist a link.
/// </summary>
internal sealed class StrongNumberEvidenceSource : IEvidentiaEvidenceSource
{
    private const double AnchorScore = StrongNumberMatch.Unambiguous;

    public IEnumerable<EvidentiaEvidence> Find(EvidentiaAnalysis source, EvidentiaAnalysis target)
    {
        var from = StrongNumbers.Normalize(source.Token.StrongNumber);
        var to = StrongNumbers.Normalize(target.Token.StrongNumber);
        if (from is not null && from == to)
        {
            yield return new EvidentiaEvidence(
                EvidentiaEvidenceKind.SharedStrongNumber,
                AnchorScore,
                "source-strong-number");
        }
    }
}
