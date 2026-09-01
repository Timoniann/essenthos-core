namespace Essenthos.Core.Loading.Links;

/// <summary>How a pair was reached. It is written into the link's source, so a reader can see it.</summary>
internal enum Route
{
    /// <summary>The model aligned the two texts against each other.</summary>
    Direct,

    /// <summary>The model aligned through a third text, whose own links to the target are stated.</summary>
    Composed,

    /// <summary>Both, independently, on the same pair.</summary>
    Both,
}

internal sealed record RoutedLink(long From, long To, double Confidence, Route Route);

/// <summary>
/// Two ways of asking the same question, and what to believe when they answer.
///
/// Aligning Russian against Hebrew directly is the hard case: different families, different
/// scripts, different word counts, and one noisy model carrying the whole distance. Aligning Russian
/// against the King James is the easy one, and the King James against BHSA is not aligned at all —
/// it is stated by a file, word by word. So the same question can be asked twice over quite
/// different evidence, which is what makes the second route worth having rather than merely
/// another opinion.
///
/// It shows in the words the direct route misses. Genesis 1:4 gives Hebrew no word for the Russian
/// <em>он</em>, and the model rightly finds none; but <em>отделил</em> and <em>от</em> have plain
/// counterparts, and the composed route reaches them through <em>divided</em> → וַיַּבְדֵּל and
/// <em>from</em> → בֵּין, both of which the file states outright.
///
/// Where the two agree the pair has been found twice over different evidence, and is combined as
/// independent — one minus the product of the doubts. They are not fully independent: both read the
/// same two verses, and a mistake in how the verse is divided misleads them alike. So the result is
/// capped below certainty, and a pair no source has stated can never come to read as one that has
/// been.
/// </summary>
internal static class Routes
{
    /// <summary>
    /// The most a pair reached twice by inference may claim. It is short of 1 on purpose: the two
    /// routes share their texts, so their errors are correlated, and no amount of agreement between
    /// two guesses turns them into a citation.
    /// </summary>
    public const double Ceiling = 0.98;

    public static IReadOnlyList<RoutedLink> Merge(
        IEnumerable<(long From, long To, double Confidence)> direct,
        IEnumerable<(long From, long To, double Confidence)> composed)
    {
        var merged = new Dictionary<(long, long), RoutedLink>();

        foreach (var (from, to, confidence) in direct)
        {
            Keep(merged, from, to, confidence, Route.Direct);
        }

        foreach (var (from, to, confidence) in composed)
        {
            Keep(merged, from, to, confidence, Route.Composed);
        }

        return [.. merged.Values];
    }

    private static void Keep(
        Dictionary<(long, long), RoutedLink> merged,
        long from,
        long to,
        double confidence,
        Route route)
    {
        var key = (from, to);
        if (!merged.TryGetValue(key, out var standing))
        {
            merged[key] = new RoutedLink(from, to, confidence, route);
            return;
        }

        // The same route reaching a pair twice is one claim found twice over the same evidence, and
        // the better reading of it is the stronger one, not a stronger one than either.
        merged[key] = standing.Route == route
            ? standing with { Confidence = Math.Max(standing.Confidence, confidence) }
            : new RoutedLink(from, to, Agreeing(standing.Confidence, confidence), Route.Both);
    }

    /// <summary>
    /// One minus the product of the doubts, held below certainty. Two routes each half sure make a
    /// pair worth three quarters of a reader's trust, and never all of it.
    /// </summary>
    public static double Agreeing(double first, double second) =>
        Math.Min(Ceiling, 1 - ((1 - first) * (1 - second)));
}
