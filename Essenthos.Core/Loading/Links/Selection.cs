namespace Essenthos.Core.Loading.Links;

/// <summary>Which of a verse's proposed pairs to keep.</summary>
internal enum Selection
{
    /// <summary>Everything above the threshold.</summary>
    All,

    /// <summary>
    /// The model's best answer for each source word, and not its runners-up. Exact ties are all
    /// kept: Russian <em>отделяет</em> renders מַבְדִּיל and בֵּין at the same 0.57, and a model with no
    /// preference between two words is reporting a real one-to-many rather than hesitating.
    /// </summary>
    BestPerSource,

    /// <summary>
    /// Melamed's competitive linking: take the strongest pair whose words are both still free,
    /// repeat. Every word ends in at most one pair, so no two source words can share a target.
    /// </summary>
    Competitive,
}

/// <summary>
/// What to do when a word is offered more than one counterpart.
///
/// The model does not answer "which Hebrew word is this" — it scores every pair it can, and a
/// permissive symmetrisation keeps several. Some of those are a real one-to-many correspondence:
/// Russian <em>отделяет</em> renders מַבְדִּיל and בֵּין together, and dropping either would be a loss.
/// Others are the model's second guess kept alongside its first, and those do more damage than
/// their number suggests, because the reader highlights by shared word: one spurious pair on
/// <em>над</em> makes it light up whenever <em>носился</em> does, and the two read as one phrase
/// that the corpus thinks is a unit.
///
/// Which of the three rules is right is a question with an answer, so it is measured rather than
/// argued — <c>score kjv bhsa</c> runs all three against the correspondences the file states.
/// </summary>
internal static class Selections
{
    public static List<(int Source, int Target, double Confidence, double Position)> Apply(
        Selection selection,
        List<(int Source, int Target, double Confidence, double Position)> verse) => selection switch
        {
            Selection.BestPerSource =>
            [
                .. verse
                    .GroupBy(pair => pair.Source)
                    .SelectMany(group =>
                    {
                        var best = group.Max(pair => pair.Confidence);
                        return group.Where(pair => pair.Confidence >= best);
                    }),
            ],
            Selection.Competitive => Competitive(verse),
            _ => verse,
        };

    private static List<(int Source, int Target, double Confidence, double Position)> Competitive(
        List<(int Source, int Target, double Confidence, double Position)> verse)
    {
        var kept = new List<(int, int, double, double)>(verse.Count);
        var sources = new HashSet<int>();
        var targets = new HashSet<int>();

        foreach (var pair in verse.OrderByDescending(pair => pair.Confidence))
        {
            // Both words have to still be free. Marking one as used while rejecting the pair would
            // spend a word on a pair that was never kept.
            if (sources.Contains(pair.Source) || targets.Contains(pair.Target))
            {
                continue;
            }

            sources.Add(pair.Source);
            targets.Add(pair.Target);
            kept.Add(pair);
        }

        return kept;
    }
}
