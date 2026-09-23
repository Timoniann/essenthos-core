namespace Essenthos.Core.Loading.Links;

/// <summary>
/// How a pair was reached. A pair may be reached more than one way, so these combine.
/// </summary>
[Flags]
internal enum Route
{
    None = 0,

    /// <summary>Aligned against the target directly, on the words as the text writes them.</summary>
    Written = 1,

    /// <summary>Aligned against the target directly, on the words reduced to their stems.</summary>
    Reduced = 2,

    /// <summary>Aligned through a third text, whose own links to the target are stated.</summary>
    Composed = 4,

    /// <summary>
    /// Aligned through a second middle text, another translation whose links to the target a
    /// different source states — so an answer it shares with the first is found twice over evidence
    /// neither route shares.
    /// </summary>
    ComposedAgain = 8,
}

internal sealed record RoutedLink(long From, long To, double Confidence, Route Route);

/// <summary>
/// Three ways of asking the same question, and what to believe when they answer.
///
/// Two of them differ in what they align: Russian against Hebrew is one hard hop, and Russian
/// against the King James is an easy one over a text whose own links to the Hebrew are stated
/// rather than guessed. The third differs in what it reads: the same alignment run again over words
/// reduced to their stems.
///
/// That last one is not a refinement of the first, which is why it is a route and not a
/// replacement. Reducing the forms pools the evidence for a word seen in a dozen shapes, and it
/// costs the rare word its sharpness: <em>безвидна</em> occurs twice in the Bible and matched תֹהוּ at
/// 0.98 as written, and once every form of <em>быть</em> became one frequent stem it had to compete
/// with that stem for the same Hebrew word and fell to 0.15. Both readings are right about
/// different words, and choosing between them means being wrong about half of them.
///
/// Where routes agree the pair has been found more than once over evidence that differs, and is
/// combined as independent — one minus the product of the doubts. The two direct readings are the
/// exception, and are merged as one family (<see cref="MergeFamilies"/>). None are fully independent:
/// all three read the same two verses, and a mistake in how the verse is divided misleads them
/// alike. So the result is capped below certainty, and a pair no source has stated can never come
/// to read as one that has been.
/// </summary>
internal static class Routes
{
    /// <summary>
    /// The most a pair reached by inference may claim, however many routes found it. Short of 1 on
    /// purpose: the routes share their texts, so their errors are correlated, and no amount of
    /// agreement between guesses turns them into a citation.
    /// </summary>
    public const double Ceiling = 0.98;

    /// <summary>
    /// The direct readings merged as one family and the composed one as another. The written and
    /// the reduced reading are one model over two spellings of the same words: where the language
    /// barely inflects, or the stemmer barely changes a word, they are the same answer twice, and
    /// counting their agreement as two voices makes one guess look corroborated. So a pair the two
    /// both reach keeps the better of their confidences, and only the route through the middle text,
    /// which reads evidence the direct model never sees, raises it.
    /// </summary>
    /// <param name="composed">One set of pairs per middle text, in the order the texts were named.</param>
    public static IReadOnlyList<RoutedLink> MergeFamilies(
        IEnumerable<(long From, long To, double Confidence)> written,
        IEnumerable<(long From, long To, double Confidence)> reduced,
        IReadOnlyList<IEnumerable<(long From, long To, double Confidence)>> composed)
    {
        if (composed.Count > Middles.Length)
        {
            throw new ArgumentException(
                $"A composition goes through at most {Middles.Length} middle texts; {composed.Count} were named.",
                nameof(composed));
        }

        var merged = new Dictionary<(long, long), RoutedLink>();

        foreach (var (route, pairs) in new[] { (Route.Written, written), (Route.Reduced, reduced) })
        {
            foreach (var (from, to, confidence) in pairs)
            {
                merged[(from, to)] = merged.TryGetValue((from, to), out var standing)
                    ? new RoutedLink(from, to, Math.Max(standing.Confidence, confidence), standing.Route | route)
                    : new RoutedLink(from, to, confidence, route);
            }
        }

        for (var middle = 0; middle < composed.Count; middle++)
        {
            foreach (var (from, to, confidence) in composed[middle])
            {
                Keep(merged, from, to, confidence, Middles[middle]);
            }
        }

        return [.. merged.Values];
    }

    /// <summary>The route each middle text's answers travel, in the order the texts are named.</summary>
    public static readonly Route[] Middles = [Route.Composed, Route.ComposedAgain];

    /// <summary>
    /// How many readings found a pair that share no evidence: the two direct ones are one model over
    /// two spellings of the same words, and each middle text is a route of its own.
    /// </summary>
    public static int Families(Route route) =>
        ((route & (Route.Written | Route.Reduced)) != 0 ? 1 : 0)
        + Middles.Count(middle => route.HasFlag(middle));

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

        // One route reaching a pair twice is one claim found twice over the same evidence, and the
        // better reading of it is the stronger one, not a stronger one than either.
        merged[key] = standing.Route.HasFlag(route)
            ? standing with { Confidence = Math.Max(standing.Confidence, confidence) }
            : new RoutedLink(from, to, Agreeing(standing.Confidence, confidence), standing.Route | route);
    }

    /// <summary>
    /// One minus the product of the doubts, held below certainty. Two routes each half sure make a
    /// pair worth three quarters of a reader's trust, and never all of it.
    /// </summary>
    public static double Agreeing(double first, double second) =>
        Math.Min(Ceiling, 1 - ((1 - first) * (1 - second)));

    /// <summary>What to write in the link's source, so a reader can see which readings agreed.</summary>
    /// <param name="vias">The middle texts, in the order <see cref="Middles"/> numbers them.</param>
    public static string Describe(Route route, params string[] vias)
    {
        var found = new List<string>(3);
        if (route.HasFlag(Route.Written))
        {
            found.Add("as written");
        }

        if (route.HasFlag(Route.Reduced))
        {
            found.Add("as stems");
        }

        for (var middle = 0; middle < Math.Min(vias.Length, Middles.Length); middle++)
        {
            if (route.HasFlag(Middles[middle]))
            {
                found.Add($"through {vias[middle]}");
            }
        }

        return $"SIL.Machine, aligned {string.Join(" and ", found)}";
    }
}
