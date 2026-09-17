namespace Essenthos.Core.Loading.Links;

/// <param name="Proposed">Word pairs the method under test claims correspond.</param>
/// <param name="Gold">Word pairs a source states correspond.</param>
/// <param name="Hit">Proposed pairs a source also states.</param>
/// <param name="AlignmentErrorRate">
/// The measure this field settled on, so a number here can be compared with a published one. It is
/// one minus the harmonic mean of precision and recall, which is to say: lower is better, and zero
/// would mean the method reproduced the source exactly.
/// </param>
internal sealed record AlignmentScore(
    int Proposed,
    int Gold,
    int Hit,
    double Precision,
    double Recall,
    double AlignmentErrorRate)
{
    public override string ToString() =>
        $"{Hit} of {Proposed} proposed pairs are stated by the source, out of {Gold} stated in all — " +
        $"precision {Precision:P1}, recall {Recall:P1}, AER {AlignmentErrorRate:F3}";
}

/// <summary>
/// Scores a proposed word alignment against one a source states.
///
/// This is what makes a method decidable rather than plausible. 279,627 English-to-Hebrew
/// correspondences in this corpus are stated by a file, so any aligner can be run over the same
/// pair of texts and told how much of that it recovers and how much it invents. A method that
/// cannot be scored is a method nobody can argue about.
///
/// The gold pairs are cross-script — English against Hebrew, English against Greek — which is
/// exactly the case where matching strings is useless and a statistical method has to earn its
/// place. That makes this a hard test rather than a flattering one, and the right one.
/// </summary>
internal static class Alignment
{
    public static AlignmentScore Score(
        IEnumerable<(long From, long To)> proposed,
        IEnumerable<(long From, long To)> gold)
    {
        var stated = gold as HashSet<(long, long)> ?? [.. gold];
        var claimed = proposed as HashSet<(long, long)> ?? [.. proposed];

        var hit = claimed.Count(stated.Contains);
        var precision = claimed.Count == 0 ? 0 : (double)hit / claimed.Count;
        var recall = stated.Count == 0 ? 0 : (double)hit / stated.Count;
        var harmonic = precision + recall == 0 ? 0 : 2 * precision * recall / (precision + recall);

        return new AlignmentScore(claimed.Count, stated.Count, hit, precision, recall, 1 - harmonic);
    }
}
