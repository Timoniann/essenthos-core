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

    /// <summary>
    /// Aligned through a third middle text, added to a composition through two (PRB-0863): the Elberfelder through Luther,
    /// whose links to the Greek are his printed Strong numbers, beside the King James and the Berean. What the first two
    /// find without it is written as the composition through those two wrote it (<see cref="CompositionPipeline"/>).
    /// </summary>
    ComposedThird = 16,
}

internal sealed record RoutedLink(long From, long To, double Confidence, Route Route);

/// <summary>
/// Where each reading of a composition shared its word out evenly (<see cref="Routes.Split"/>): for each answer, the other words
/// of the verse given the same target at the same confidence. The model shares a word it cannot place onto its neighbours'
/// target, and the hop to a middle text, trained the same way on the same verse, shares it onto the same neighbours': in
/// Ephesians 6:16 of the New Ukrainian Translation «диявола» reached βέλη directly (with «стріли» and «розпечені», 0.333 each)
/// and through the King James's "darts" (with «стріли», 0.20 each). Two readings naming the same words so are one error found
/// twice, and <see cref="Routes.MergeFamilies"/> counts them once.
/// </summary>
/// <param name="Composed">One per middle text: the split of the hop to it, carried onto the target the composed answer names.</param>
internal sealed record Splits(
    IReadOnlyDictionary<(long From, long To), long[]> Written,
    IReadOnlyDictionary<(long From, long To), long[]> Reduced,
    IReadOnlyList<IReadOnlyDictionary<(long From, long To), long[]>> Composed)
{
    public static readonly IReadOnlyDictionary<(long From, long To), long[]> Empty = new Dictionary<(long From, long To), long[]>();

    public static readonly Splits None = new(Empty, Empty, []);
}

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
    /// The confidence an inferred pair is written with: its own score, held at <see cref="Ceiling"/>.
    /// A model scores a pair exactly 1 when it has seen the two words together and never apart, which
    /// is the thinnest evidence it has; written as it stands, it is the one number in the corpus that
    /// reads as certainty, on the one method with no claim to it.
    /// </summary>
    public static double Written(double confidence) => Math.Min(Ceiling, confidence);

    /// <summary>
    /// The direct readings merged as one family and the composed one as another. The written and
    /// the reduced reading are one model over two spellings of the same words: where the language
    /// barely inflects, or the stemmer barely changes a word, they are the same answer twice, and
    /// counting their agreement as two voices makes one guess look corroborated. So a pair the two
    /// both reach keeps the better of their confidences, and only the route through the middle text,
    /// which reads evidence the direct model never sees, raises it.
    /// </summary>
    /// <param name="composed">One set of pairs per middle text, in the order the texts were named.</param>
    /// <param name="splits">
    /// Where each reading shared its word out evenly among several words of the verse (<see cref="Splits"/>). A reading whose
    /// split names a word another reading of the same pair split it with is that reading's error made again, not a second
    /// finding, and adds nothing to it -- where a word it was split with reaches the target by a family of readings this one
    /// lacks, so the target has a surer claimant. Where the words it was split with reached the target no better, the split is
    /// the phrase rendering one word (οἴκου Φογώρ for בֵּית פְּעֹור, «друг друга» for ἀλλήλους), and both readings stand.
    /// </param>
    public static IReadOnlyList<RoutedLink> MergeFamilies(
        IEnumerable<(long From, long To, double Confidence)> written,
        IEnumerable<(long From, long To, double Confidence)> reduced,
        IReadOnlyList<IEnumerable<(long From, long To, double Confidence)>> composed,
        Splits? splits = null)
    {
        if (composed.Count > Middles.Length)
        {
            throw new ArgumentException(
                $"A composition goes through at most {Middles.Length} middle texts; {composed.Count} were named.",
                nameof(composed));
        }

        IReadOnlyCollection<(long, long, double)> asWritten = [.. written], asStems = [.. reduced];
        List<IReadOnlyCollection<(long, long, double)>> throughMiddles = [.. composed.Select(pairs => (IReadOnlyCollection<(long, long, double)>)[.. pairs])];
        var whole = Merged(asWritten, asStems, throughMiddles, Splits.None, []);
        if (splits is null)
        {
            return [.. whole.Values];
        }

        var shared = new Dictionary<(long, long), HashSet<long>>();
        var once = Merged(asWritten, asStems, throughMiddles, splits, shared);
        foreach (var (key, link) in once)
        {
            var standing = whole[key];
            if (link == standing)
            {
                continue;
            }

            var surer = shared[key].Any(word => whole.TryGetValue((word, key.Item2), out var other)
                                                && Lacks(standing.Route, other.Route));
            if (surer)
            {
                whole[key] = link;
            }
        }

        return [.. whole.Values];
    }

    /// <summary>Whether <paramref name="other"/> was found by a family of readings <paramref name="route"/> was not: the two direct readings are one.</summary>
    internal static bool Lacks(Route route, Route other)
    {
        const Route direct = Route.Written | Route.Reduced;
        return ((other & direct) != 0 && (route & direct) == 0) || Middles.Any(middle => other.HasFlag(middle) && !route.HasFlag(middle));
    }

    private static Dictionary<(long, long), RoutedLink> Merged(
        IEnumerable<(long From, long To, double Confidence)> written,
        IEnumerable<(long From, long To, double Confidence)> reduced,
        IReadOnlyList<IEnumerable<(long From, long To, double Confidence)>> composed,
        Splits splits,
        Dictionary<(long, long), HashSet<long>> shared)
    {
        var merged = new Dictionary<(long, long), RoutedLink>();

        foreach (var (route, pairs, split) in new[] { (Route.Written, written, splits.Written), (Route.Reduced, reduced, splits.Reduced) })
        {
            foreach (var (from, to, confidence) in pairs)
            {
                merged[(from, to)] = merged.TryGetValue((from, to), out var standing)
                    ? new RoutedLink(from, to, Math.Max(standing.Confidence, confidence), standing.Route | route)
                    : new RoutedLink(from, to, confidence, route);
                Share(shared, (from, to), split);
            }
        }

        for (var middle = 0; middle < composed.Count; middle++)
        {
            var split = middle < splits.Composed.Count ? splits.Composed[middle] : Splits.Empty;
            foreach (var (from, to, confidence) in composed[middle])
            {
                Keep(merged, shared, from, to, confidence, Middles[middle], split);
            }
        }

        return merged;
    }

    /// <summary>Adds a reading's split to what the pair's readings shared so far; whether it names a word one of them named.</summary>
    private static bool Share(Dictionary<(long, long), HashSet<long>> shared, (long, long) key, IReadOnlyDictionary<(long From, long To), long[]> split)
    {
        if (!split.TryGetValue(key, out var with))
        {
            return false;
        }

        if (!shared.TryGetValue(key, out var words))
        {
            shared[key] = words = [];
        }

        var again = words.Overlaps(with);
        words.UnionWith(with);
        return again;
    }

    /// <summary>The route each middle text's answers travel, in the order the texts are named.</summary>
    public static readonly Route[] Middles = [Route.Composed, Route.ComposedAgain, Route.ComposedThird];

    /// <summary>
    /// How many readings found a pair that share no evidence: the two direct ones are one model over
    /// two spellings of the same words, and each middle text is a route of its own.
    /// </summary>
    public static int Families(Route route) =>
        ((route & (Route.Written | Route.Reduced)) != 0 ? 1 : 0)
        + Middles.Count(middle => route.HasFlag(middle));

    private static void Keep(
        Dictionary<(long, long), RoutedLink> merged,
        Dictionary<(long, long), HashSet<long>> shared,
        long from,
        long to,
        double confidence,
        Route route,
        IReadOnlyDictionary<(long From, long To), long[]> split)
    {
        var key = (from, to);
        var again = Share(shared, key, split);
        if (!merged.TryGetValue(key, out var standing))
        {
            merged[key] = new RoutedLink(from, to, confidence, route);
            return;
        }

        // One route reaching a pair twice is one claim found twice over the same evidence, and the
        // better reading of it is the stronger one, not a stronger one than either. So is a route that
        // split its word among the same words of the verse as a reading already counted.
        merged[key] = standing.Route.HasFlag(route) || again
            ? standing with { Confidence = Math.Max(standing.Confidence, confidence) }
            : new RoutedLink(from, to, Agreeing(standing.Confidence, confidence), standing.Route | route);
    }

    /// <summary>
    /// How near two answers' confidences must be, as a share of the larger, to be one even split. A model that cannot tell
    /// which of several words renders one target gives each the same score to five or more figures (диявола, стріли and
    /// розпечені 0.33317 each on βέλη in Ephesians 6:16); answers from evidence of their own seldom agree in the third.
    /// </summary>
    public const double EvenSplit = 1e-3;

    /// <summary>
    /// For each answer of one reading that shares its target with other words of the verse at the same confidence, those
    /// other words (<see cref="EvenSplit"/>). A word written the same way is not one: two occurrences of one word are the
    /// same item to the model, and which of them is meant is a question of order, not of what the words mean.
    /// </summary>
    /// <param name="form">A source word's form, to tell another word from another occurrence of the same one.</param>
    public static Dictionary<(long From, long To), long[]> Split(
        IEnumerable<(long From, long To, double Confidence)> answers, Func<long, string> form)
    {
        var split = new Dictionary<(long From, long To), long[]>();
        foreach (var target in answers.GroupBy(answer => answer.To).Where(group => group.Skip(1).Any()))
        {
            var sharing = target.ToList();
            foreach (var (from, to, confidence) in sharing)
            {
                var with = sharing
                    .Where(other => other.From != from && form(other.From) != form(from)
                                    && Math.Abs(other.Confidence - confidence) <= EvenSplit * Math.Max(other.Confidence, confidence))
                    .Select(other => other.From)
                    .ToArray();
                if (with.Length > 0)
                {
                    split[(from, to)] = with;
                }
            }
        }

        return split;
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
        var found = new List<string>(2 + Middles.Length);
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

        return $"{CompositionPipeline.Machine}, aligned {string.Join(" and ", found)}";
    }
}
