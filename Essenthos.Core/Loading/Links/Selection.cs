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
    /// <param name="targets">
    /// What each target word is, so that a repetition can be recognised. A word written twice in a
    /// verse is the case a model cannot resolve on its own: it scores both occurrences the same,
    /// because they are the same word.
    /// </param>
    public static List<(int Source, int Target, double Confidence, double Position)> Apply(
        Selection selection,
        List<(int Source, int Target, double Confidence, double Position)> verse,
        IReadOnlyList<string>? targets = null) => selection switch
        {
            Selection.BestPerSource => PairRepeats(
                [.. verse.GroupBy(pair => pair.Source).SelectMany(Best)], targets),
            Selection.Competitive => Competitive(verse),
            _ => verse,
        };

    private static IEnumerable<(int Source, int Target, double Confidence, double Position)> Best(
        IGrouping<int, (int Source, int Target, double Confidence, double Position)> group)
    {
        var best = group.Max(pair => pair.Confidence);
        return group.Where(pair => pair.Confidence >= best);
    }

    /// <summary>
    /// Where one word is written several times in a verse, hands its occurrences out in order.
    ///
    /// Matthew 1:4 is the case: Ναασσών stands twice in the Greek and twice in the Synodal, and the
    /// model scores every combination 1.000 — correctly, because they are the same word and nothing
    /// in the lexicon can prefer one pairing. Left alone that becomes two source words on one
    /// target, which a reader sees as both lighting when either is touched.
    ///
    /// Order is what both texts agree on, and for a word repeated identically any pairing reads the
    /// same to a reader, so taking them in sequence costs nothing and removes the crowding. Where
    /// the counts differ the extra ones keep whatever they had, because nothing here can choose.
    /// </summary>
    private static List<(int Source, int Target, double Confidence, double Position)> PairRepeats(
        List<(int Source, int Target, double Confidence, double Position)> kept,
        IReadOnlyList<string>? targets)
    {
        if (targets is null)
        {
            return kept;
        }

        var byWord = kept
            .Where(pair => pair.Target < targets.Count)
            .GroupBy(pair => targets[pair.Target])
            .Where(word => word.Select(pair => pair.Target).Distinct().Count() > 1
                           || word.Select(pair => pair.Source).Distinct().Count() > 1);

        var assigned = new Dictionary<int, int>();
        foreach (var word in byWord)
        {
            var sources = word.Select(pair => pair.Source).Distinct().Order().ToList();
            var occurrences = word.Select(pair => pair.Target).Distinct().Order().ToList();

            for (var at = 0; at < Math.Min(sources.Count, occurrences.Count); at++)
            {
                assigned[sources[at]] = occurrences[at];
            }
        }

        var paired = new List<(int, int, double, double)>(kept.Count);
        var settled = new HashSet<int>();

        foreach (var pair in kept)
        {
            if (!assigned.TryGetValue(pair.Source, out var target))
            {
                paired.Add(pair);
                continue;
            }

            if (settled.Add(pair.Source))
            {
                paired.Add((pair.Source, target, pair.Confidence, pair.Position));
            }
        }

        return paired;
    }

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
