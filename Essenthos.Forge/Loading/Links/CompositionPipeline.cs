using System.Diagnostics;
using System.Globalization;
using System.Text;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Mapping;
using Essenthos.Core.Products;
namespace Essenthos.Core.Loading.Links;

/// <param name="Reached">Pairs the composed routes propose, before merging.</param>
/// <param name="Agreed">Pairs readings that share no evidence both reached, which is the measure of whether any works.</param>
/// <param name="Added">Pairs only the middle texts reached — the coverage composing buys.</param>
/// <param name="Written">Pairs written as links of their own, because nothing else names those words.</param>
/// <param name="Corroborated">
/// Pairs a source has already stated, which become a second claim on the link that states them
/// rather than a second link beside it.
/// </param>
/// <param name="Admitted">Which answers were written, per combination of readings, and from what confidence.</param>
internal sealed record CompositionOutcome(
    string From,
    string Via,
    string To,
    int Direct,
    int Reached,
    int Agreed,
    int Added,
    int Written,
    int Corroborated,
    string Admitted,
    TimeSpan Elapsed,
    int? Verses = null)
{
    public override string ToString() =>
        (Verses is { } verses ? $"In the {verses} verses where {Via} gained links since: " : string.Empty) +
        $"{From} to {To} through {Via}: {Written} links from {Direct} direct and {Reached} composed — " +
            $"{Agreed} reached by more than one reading, {Added} only through {Via}, " +
            $"{Corroborated} agreeing with a link a source already states; {Admitted}";

}

/// <summary>
/// The second route to the same Hebrew word, through the English.
///
/// Russian against Hebrew is one hard hop. Russian against the King James is an easy one — two
/// modern languages, similar length, similar order — and the King James against BHSA is not a hop
/// at all, because a file states it word by word. So the same correspondence can be found a second
/// time over evidence the first route never touches, and the two answers can be compared.
///
/// The gain is in the words the direct route leaves bare. Genesis 1:4 in the Synodal had no Hebrew
/// for <em>отделил</em>, <em>от</em> or <em>он</em>; the first two reach וַיַּבְדֵּל and בֵּין through
/// <em>divided</em> and <em>from</em>, which the file states outright. The third has no Hebrew word
/// to reach, and still has none, which is the right answer and not a gap.
///
/// The merge is <see cref="Routes"/>. This class only fetches, joins and writes.
/// </summary>
internal sealed class CompositionPipeline(ProductTexts texts, AlignerFiles aligner, ILogger logger)
{
    /// <summary>
    /// How faint a proposal may be and still count as one route's half of an agreement.
    ///
    /// A pair written on one route's word alone has to clear the ordinary threshold. A pair two
    /// routes reach over different evidence does not: the Synodal's "отделил" against וַיַּבְדֵּל is 0.23
    /// aligned directly and 0.13 through the English, and either alone is noise. Both naming the
    /// same word out of the twenty in that verse is not, and the combination has to clear the
    /// ordinary threshold anyway -- so this decides only what is allowed to meet, never what is
    /// written.
    /// </summary>
    public const double AgreementFloor = 0.1;

    private readonly MappingLinks _links = new(texts);
    /// <param name="viaSlugs">
    /// The middle texts, one to three. A second is a second route that shares no evidence with the
    /// first, so the two agreeing is worth as much as either agreeing with the direct alignment. A third is
    /// added to a composition through the first two, whose floors stand for what it does not find.
    /// </param>
    /// <param name="unmeasured">
    /// Write every answer from the threshold where nothing, in this pair or in another text of its
    /// language, states the target to measure against. Otherwise such a run is refused.
    /// </param>
    /// <param name="daughter">
    /// The source was translated from the middle texts and not from the target, so it reaches the
    /// target through them alone. No model aligns it to the target directly — the Ge'ez against
    /// BHSA would be a claim that it renders the Hebrew — and the middle texts' own links to the
    /// target count however they were made, since a daughter of the Septuagint reaches the Hebrew
    /// only as far as the Septuagint does.
    /// </param>
    /// <param name="minimumConfidence">
    /// The threshold, or null for the one measured for a daughter version of the source's language,
    /// and <see cref="AlignmentPipeline.DefaultMinimumConfidence"/> where none was.
    /// </param>
    public async Task<(CompositionOutcome Outcome, List<MappingRow> Rows)> Run(
        string fromSlug,
        IReadOnlyList<string> viaSlugs,
        string toSlug,
        double? minimumConfidence,
        double precision = Admission.DefaultPrecision,
        bool unmeasured = false,
        bool daughter = false,
        int agreeing = 1,
        bool refuseUnmeasured = false,
        CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.StartNew();
        var language = texts[fromSlug].Language;
        var measured = daughter
            ? MeasuredThrough.GetValueOrDefault(language)
            : unmeasured ? MeasuredUnstated.GetValueOrDefault(language) : null;
        var threshold = minimumConfidence ?? measured?.Minimum ?? AlignmentPipeline.DefaultMinimumConfidence;

        var proposed = await Propose(fromSlug, viaSlugs, toSlug, false, null, cancellationToken, daughter);
        var statements = Key(fromSlug, toSlug);
        var (admission, _) = await Admit(fromSlug, viaSlugs, toSlug, proposed, statements, threshold, precision, false, null,
            refuseUnmeasured, cancellationToken);
        if (admission.Floors.Count == 0 && !unmeasured)
        {
            throw new InvalidOperationException(
                $"Nothing states {toSlug} for {fromSlug}, or for any other text in its language, so there is " +
                "no measure of which of its answers are right. Run it with --dry-run to see what it would add, " +
                "and add --unmeasured to write every answer from the threshold regardless.");
        }

        var merged = Merge(proposed, admission, agreeing);
        var (rows, fresh, corroborated) = Rows(fromSlug, toSlug, [.. viaSlugs], merged,
            measured is not null && threshold == measured.Minimum && (daughter || admission.Floors.Count == 0) ? measured.Note : null);

        var outcome = new CompositionOutcome(
            fromSlug, string.Join(" and ", viaSlugs), toSlug,
            proposed.Written.Count + proposed.Reduced.Count,
            proposed.Composed.Sum(route => route.Count),
            merged.Count(link => Routes.Families(link.Route) > 1),
            merged.Count(link => (link.Route & (Route.Written | Route.Reduced)) == 0),
            fresh,
            corroborated,
            admission.Describe([.. viaSlugs]),
            started.Elapsed);

        logger.LogInformation("Composed {Outcome}", outcome);
        return (outcome, rows);
    }

    /// <summary>How the source of every link a composition or an alignment writes begins (<see cref="Routes.Describe"/>).</summary>
    internal const string Machine = "SIL.Machine";

    /// <summary>
    /// What a run would write and how far it would carry the text, without writing anything: the
    /// coverage before and after, book by book, and how often the merged answer names the word a
    /// source already states where one does. None of the three readings reads those statements, so
    /// they are a held-out answer key and not the model repeating what it was given.
    /// </summary>
    /// <param name="books">
    /// A trial on a few canonical books, trained on those books alone. The whole text trains on more
    /// and scores at least as well, so a trial's figures are a floor.
    /// </param>
    public async Task<string> Measure(
        string fromSlug,
        IReadOnlyList<string> viaSlugs,
        string toSlug,
        double minimumConfidence,
        IReadOnlySet<int>? books,
        double precision = Admission.DefaultPrecision,
        string? explain = null,
        bool daughter = false,
        int agreeing = 1,
        bool refuseUnmeasured = false,
        CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.StartNew();
        var proposed = await Propose(fromSlug, viaSlugs, toSlug, true, books, cancellationToken, daughter);
        var trained = started.Elapsed;
        var statements = Key(fromSlug, toSlug);
        var scope = CompositionTrial.Scope(texts, fromSlug, toSlug, statements, books);
        var every = Merge(proposed, Admission.Everything(minimumConfidence));
        var (admission, measured) = await Admit(fromSlug, viaSlugs, toSlug, proposed, statements, minimumConfidence, precision, true, books,
            refuseUnmeasured, cancellationToken);

        if (explain is not null)
        {
            await Explain(explain, fromSlug, viaSlugs, toSlug, proposed, admission, agreeing, books, cancellationToken);
        }

        var report = new StringBuilder()
            .AppendLine("where each combination of readings named the stated word, from each confidence up:")
            .AppendLine(measured);
        foreach (var (title, merged) in new[]
                 {
                     ("every answer from the ordinary threshold:", every),
                     ($"as a run writes it — {admission.Describe([.. viaSlugs])}" +
                      (agreeing > 1 ? $", and only what {agreeing} families of readings found:" : ":"),
                      Merge(proposed, admission, agreeing)),
                 })
        {
            report.AppendLine(title).Append(CompositionTrial.Score(scope, merged, [.. viaSlugs]).Describe(
                fromSlug, string.Join(" and ", viaSlugs), toSlug, minimumConfidence, books, trained, started.Elapsed));
        }

        return report.ToString();
    }
    /// <summary>
    /// Every answer each reading gave, one line per pair, for working out why a word was left
    /// without a link: whether nothing proposed anything for it, or a combination was refused, or
    /// its answer fell under the floor. The first hop to each middle text follows, since a route
    /// that reached a middle word the middle text leaves unlinked proposes nothing to the target.
    /// </summary>
    private async Task Explain(
        string path,
        string fromSlug,
        IReadOnlyList<string> viaSlugs,
        string toSlug,
        Proposed proposed,
        Admission admission,
        int agreeing,
        IReadOnlySet<int>? books,
        CancellationToken cancellationToken)
    {
        var chosen = Merge(proposed, admission, agreeing).Select(link => (link.From, link.To)).ToHashSet();
        var (from, to) = (texts[fromSlug], texts[toSlug]);
        await using var writer = new StreamWriter(path, false, new UTF8Encoding(false));
        await writer.WriteLineAsync("reading\tfrom\tto\tconfidence\tadmitted\tchosen");
        foreach (var link in proposed.Families())
        {
            await writer.WriteLineAsync(string.Join('\t',
                Admission.Name(link.Route, [.. viaSlugs]), from.Key(link.From).Own, to.Key(link.To).Own,
                link.Confidence.ToString("F4", CultureInfo.InvariantCulture),
                admission.Admits(link) ? 1 : 0, chosen.Contains((link.From, link.To)) ? 1 : 0));
        }

        foreach (var viaSlug in viaSlugs)
        {
            var via = texts[viaSlug];
            foreach (var (word, bridge, confidence) in await aligner.Proposals(
                         fromSlug, viaSlug, Workspace(fromSlug, viaSlug, true, books), AgreementFloor,
                         books: books, cancellationToken: cancellationToken))
            {
                await writer.WriteLineAsync(string.Join('\t',
                    $"to {viaSlug}", from.Key(word).Own, via.Key(bridge).Own, confidence.ToString("F4", CultureInfo.InvariantCulture), 0, 0));
            }
        }
    }

    /// <param name="Composed">One set of pairs per middle text.</param>
    /// <param name="Splits">Where each reading shared its word out evenly; none where null.</param>
    internal sealed record Proposed(
        IReadOnlyList<(long From, long To, double Confidence)> Written,
        IReadOnlyList<(long From, long To, double Confidence)> Reduced,
        IReadOnlyList<IReadOnlyList<(long From, long To, double Confidence)>> Composed,
        Splits? Splits = null)
    {
        /// <summary>The readings made into one set of routed pairs (<see cref="Routes.MergeFamilies"/>).</summary>
        public IReadOnlyList<RoutedLink> Families() => Routes.MergeFamilies(Written, Reduced, [.. Composed], Splits);

        /// <summary>The proposal through the first middle texts alone.</summary>
        public Proposed Through(int middles) => this with
        {
            Composed = [.. Composed.Take(middles)],
            Splits = Splits is null ? null : Splits with { Composed = [.. Splits.Composed.Take(middles)] },
        };
    }

    /// <param name="daughter">See <see cref="Run"/>.</param>
    private async Task<Proposed> Propose(
        string fromSlug,
        IReadOnlyList<string> viaSlugs,
        string toSlug,
        bool trial,
        IReadOnlySet<int>? books,
        CancellationToken cancellationToken,
        bool daughter = false)
    {
        var joins = VerseJoins.Of(texts.Store, texts[fromSlug], texts[toSlug]);

        IReadOnlyList<(long From, long To, double Confidence)> reduced = daughter
            ? []
            : await aligner.Proposals(
                fromSlug, toSlug, Workspace(fromSlug, toSlug, trial, books), AgreementFloor,
                books: books, cancellationToken: cancellationToken);
        IReadOnlyList<(long From, long To, double Confidence)> written = daughter
            ? []
            : await aligner.Proposals(
                fromSlug, toSlug, Workspace(fromSlug, toSlug, trial, books, written: true), AgreementFloor,
                asWritten: true, books: books, cancellationToken: cancellationToken);
        var source = texts[fromSlug];
        string Form(long word) => Folded(source.Words[(int)word].Surface, source.Language);
        var composed = new List<IReadOnlyList<(long From, long To, double Confidence)>>(viaSlugs.Count);
        var splits = new List<IReadOnlyDictionary<(long From, long To), long[]>>(viaSlugs.Count);
        foreach (var viaSlug in viaSlugs)
        {
            var first = await aligner.Proposals(
                fromSlug, viaSlug, Workspace(fromSlug, viaSlug, trial, books), AgreementFloor,
                books: books, cancellationToken: cancellationToken);
            var (pairs, split) = Compose(first, _links.Carried(viaSlug, toSlug, daughter), joins, Routes.Split(first, Form));
            composed.Add(pairs);
            splits.Add(split);
        }

        return new Proposed(written, reduced, composed, new Splits(Routes.Split(written, Form), Routes.Split(reduced, Form), splits));
    }
    /// <summary>A word as search folds it, lower-cased and without marks: Ἁβραὰμ and Ἁβραάμ are one word said twice.</summary>
    internal static string Folded(string surface, string language)
    {
        var text = new StringBuilder(surface.Length);
        foreach (var letter in Corpus.WordFolding.Fold(surface, language))
        {
            if (char.GetUnicodeCategory(letter) != UnicodeCategory.NonSpacingMark)
            {
                text.Append(char.ToLowerInvariant(letter));
            }
        }

        return text.ToString();
    }

    /// <summary>
    /// Which answers a run writes: measured on the pair's own statements where it has enough, and
    /// otherwise on the other texts in its language that state the target, each composed the same
    /// way; a floor the pair sets by hand (<see cref="HandFloors"/>) comes before either. Also returns
    /// what each combination scored from each confidence up, for a dry run to print.
    /// </summary>
    /// <param name="refuseUnmeasured">See <see cref="Admission.RefusingUnmeasured"/>.</param>
    private async Task<(Admission Admission, string Measured)> Admit(
        string fromSlug,
        IReadOnlyList<string> viaSlugs,
        string toSlug,
        Proposed proposed,
        IReadOnlyDictionary<long, HashSet<long>> statements,
        double minimumConfidence,
        double precision,
        bool trial,
        IReadOnlySet<int>? books,
        bool refuseUnmeasured,
        CancellationToken cancellationToken)
    {
        var pair = MappingProducts.Pair(fromSlug, toSlug);
        var hand = HandFloors.AsRoutes(pair, HandFloors.Read(texts.Store, pair), viaSlugs);

        // A daughter read through one middle text has that one way to the target; where a hand floor measured it, what another
        // text of its language would borrow is never read, and no proxy's hops are trained for it.
        if (proposed.Written.Count == 0 && proposed.Reduced.Count == 0 && viaSlugs.Count == 1 && hand.ContainsKey(Routes.Middles[0]))
        {
            return (Admission.Everything(minimumConfidence).With(hand).RefusingUnmeasured(refuseUnmeasured), "  by its hand floor alone");
        }

        var (admission, measured) = await Measured(fromSlug, viaSlugs, toSlug, proposed, statements, minimumConfidence, precision, trial, books,
            cancellationToken);

        // A third middle text is one added to a composition through the first two: what those find without it keeps the floors
        // that composition sets (Admission.Keeping, PRB-0863).
        if (viaSlugs.Count == Routes.Middles.Length)
        {
            var before = viaSlugs.Take(viaSlugs.Count - 1).ToList();
            var proposedWithout = proposed.Through(before.Count);
            var (without, measuredWithout) = await Measured(fromSlug, before, toSlug, proposedWithout, statements, minimumConfidence,
                precision, trial, books, cancellationToken);
            var standing = proposedWithout.Families()
                .Where(without.Admits).Select(link => (link.From, link.To)).ToHashSet();
            admission = admission.Keeping(without, Routes.Middles[^1], viaSlugs[^1], standing);
            measured += $"{Environment.NewLine}  through {string.Join(" and ", before)} alone, whose floors stand for what " +
                        $"{viaSlugs[^1]} does not find; {without.Describe([.. before])}{Environment.NewLine}{measuredWithout}";
        }

        return (admission.With(hand).RefusingUnmeasured(refuseUnmeasured), measured);
    }

    /// <summary>What <see cref="Admit"/> measures or borrows, before the pair's hand floors and the refusal of the unmeasured.</summary>
    private async Task<(Admission Admission, string Measured)> Measured(
        string fromSlug,
        IReadOnlyList<string> viaSlugs,
        string toSlug,
        Proposed proposed,
        IReadOnlyDictionary<long, HashSet<long>> statements,
        double minimumConfidence,
        double precision,
        bool trial,
        IReadOnlySet<int>? books,
        CancellationToken cancellationToken)
    {
        var every = Merge(proposed, Admission.Everything(minimumConfidence));
        var own = Admission.Measures(statements)
            ? Admission.Measure(every, statements, minimumConfidence, precision)
            : Admission.Everything(minimumConfidence);
        if (own.Floors.Count > 0)
        {
            return (own, $"  {fromSlug} to {toSlug}, on its own statements:{Environment.NewLine}" +
                                Admission.Curves(every, statements, [.. viaSlugs]));
        }

        var measured = new List<(string Text, Route Reads, IReadOnlyDictionary<Route, RouteFloor> Floors)>();
        var report = new StringBuilder();
        foreach (var proxy in _links.Proxies(fromSlug, toSlug))
        {
            var vias = viaSlugs.Where(via => via != proxy).ToList();
            var proxyProposed = await Propose(proxy, vias, toSlug, trial, books, cancellationToken);
            var proxyStatements = Key(proxy, toSlug);
            var proxyEvery = Merge(proxyProposed, Admission.Everything(minimumConfidence));
            var floors = Admission.Measure(proxyEvery, proxyStatements, minimumConfidence, precision);

            var middles = vias.Select(via => Routes.Middles[viaSlugs.ToList().IndexOf(via)]).ToArray();
            measured.Add((
                proxy,
                middles.Aggregate(Route.Written | Route.Reduced, (reads, middle) => reads | middle),
                floors.Floors.ToDictionary(entry => Translated(entry.Key, middles), entry => entry.Value)));
            report.AppendLine($"  {proxy} to {toSlug} through {string.Join(" and ", vias)}, on its statements; " +
                              floors.Describe([.. vias]))
                .AppendLine(Admission.Curves(proxyEvery, proxyStatements, [.. vias]));
        }

        return (Admission.Borrow(measured, [.. viaSlugs], minimumConfidence, precision), report.ToString());
    }
    /// <summary>
    /// A route as another composition numbers it, renumbered for this one: its middle texts are
    /// this pair's, named in another order or with one left out.
    /// </summary>
    /// <param name="middles">For each of the other composition's middle texts, the route it takes here.</param>
    internal static Route Translated(Route route, Route[] middles)
    {
        var translated = route & (Route.Written | Route.Reduced);
        for (var i = 0; i < middles.Length; i++)
        {
            if (route.HasFlag(Routes.Middles[i]))
            {
                translated |= middles[i];
            }
        }

        return translated;
    }

    /// <summary>
    /// The readings made into one answer per word.
    ///
    /// Each route already keeps its own best answer per word; the merge can put two different best
    /// answers back on one word, and one of them is again a runner-up. Which one survives is decided
    /// by how many readings found it before it is decided by confidence, and that order matters.
    /// Matthew 1:4 has the Synodal's second "Аминадав" scored 1.000 against δέ by the written
    /// reading alone, while the stems and the English both say Ἀμιναδάβ less loudly. Taking the
    /// loudest answer takes the wrong one — and agreement between readings that share no evidence is
    /// the whole reason there are three.
    /// </summary>
    /// <param name="admission">Which answers may be written at all, before one is chosen per word.</param>
    /// <param name="agreeing">
    /// How many families of readings must have found an answer for it to be written. More than one
    /// where the pair's only statements are a transfer from one of its middle texts: the Almeida's
    /// New Testament key was carried over from Clear's Reina-Valera alignment, so it scores the
    /// Reina-Valera's answers right whatever they are, and 28 of 44 answers only that route found were
    /// right by hand against 96 of 96 all three found.
    /// </param>
    internal static List<RoutedLink> Merge(Proposed proposed, Admission admission, int agreeing = 1) =>
        proposed.Families()
            .Where(admission.Admits)
            .Where(link => Routes.Families(link.Route) >= agreeing)
            .GroupBy(link => link.From)
            .SelectMany(group =>
            {
                var agreed = group.Max(link => Routes.Families(link.Route));
                var contenders = group.Where(link => Routes.Families(link.Route) == agreed).ToList();
                var best = contenders.Max(link => link.Confidence);
                return contenders.Where(link => link.Confidence >= best);
            })
            .ToList();

    /// <summary>
    /// Where a pair's model is kept. A trial trains models of its own, which a run must never find
    /// and reuse: a model is reused whenever its workspace exists, and a trial's may be over a few
    /// books, or over tokens an earlier build reduced differently.
    /// </summary>
    internal static string Workspace(
        string from, string to, bool trial = false, IReadOnlySet<int>? books = null, bool written = false) =>
        $"{from}-{to}" + (trial ? "-trial" : string.Empty) +
        (books is null ? string.Empty : $"-books-{string.Join('-', books.Order())}") +
        (written ? "-written" : string.Empty);

    /// <summary>
    /// The two hops joined at the middle text's word. One link of the middle text may name several
    /// words on each side -- "it was good" is three English words against one Hebrew one -- so the
    /// join is over words rather than links, and several paths may reach the same pair. Those are
    /// one claim found several times over the same evidence, so the pair takes the best path and
    /// not the sum of them.
    ///
    /// A pair whose verses the frame does not join is not proposed at all: the middle text may be
    /// stated across a verse boundary it draws where these two texts do not.
    /// </summary>
    internal static List<(long From, long To, double Confidence)> Compose(
        IReadOnlyList<(long From, long To, double Confidence)> first,
        ILookup<long, (long To, double Confidence)> second,
        VerseJoins joins) =>
        Compose(first, second, joins, Splits.Empty).Pairs;

    /// <param name="split">The hop's even splits (<see cref="Routes.Split"/>); each composed pair takes the split of its best path.</param>
    internal static (List<(long From, long To, double Confidence)> Pairs, Dictionary<(long From, long To), long[]> Split) Compose(
        IReadOnlyList<(long From, long To, double Confidence)> first,
        ILookup<long, (long To, double Confidence)> second,
        VerseJoins joins,
        IReadOnlyDictionary<(long From, long To), long[]> split)
    {
        var best = new Dictionary<(long, long), (double Confidence, long Bridge)>(400_000);

        foreach (var (from, bridge, carried) in first)
        {
            foreach (var (to, stated) in second[bridge])
            {
                if (!joins.Joins(from, to))
                {
                    continue;
                }

                var key = (from, to);
                var confidence = carried * stated;
                if (!best.TryGetValue(key, out var standing) || confidence > standing.Confidence)
                {
                    best[key] = (confidence, bridge);
                }
            }
        }

        var carriedSplit = new Dictionary<(long From, long To), long[]>();
        foreach (var ((from, to), (_, bridge)) in best)
        {
            if (split.TryGetValue((from, bridge), out var with))
            {
                carriedSplit[(from, to)] = with;
            }
        }

        return ([.. best.Select(entry => (entry.Key.Item1, entry.Key.Item2, entry.Value.Confidence))], carriedSplit);
    }

    /// <summary>
    /// The threshold measured for a daughter version composed through the text it was translated
    /// from, or for a language nothing states the target in, and what the link's source says about it.
    /// </summary>
    internal sealed record MeasuredComposition(double Minimum, string Note);

    // Hand-scored by Claude on 60 of the Ge'ez-BHSA pairs composed through Brenton, 20 per band
    // (2026-09-25): right 60% from 0.10, 75% from 0.25 and 90% from 0.40.
    //
    // Since the Ge'ez-Greek hop keeps a pair below 0.80 only where both directions of the model name it
    // (AlignerFiles.Proposals, PRB-1743, PRB-1746), 5,472 of 109,461 composed pairs are gone. Hand-read
    // 2026-11-16, seeded and stratified: of the kept pairs 59 of 60 right (15 per band, all bands), about
    // 98% weighted; of the gone ones 10 of 20 from 0.40 to 0.50, 16 of 20 from 0.50 to 0.60 and 19 of 20
    // from 0.60 to 0.80, about 68% weighted. The whole file went from about 97% to about 98% right.
    private static readonly Dictionary<string, MeasuredComposition> MeasuredThrough = new()
    {
        ["gez"] = new(0.4, "threshold 0.40 from a hand-scored sample, about 90% right above it"),
    };

    // Hand-scored by Claude on 154 of the Almeida's Old Testament pairs composed through the
    // Reina-Valera and the King James, drawn in proportion to the readings that found them
    // (2026-09-28): 146 right, 95%; 13 of 16 where only the direct model spoke.
    private static readonly Dictionary<string, MeasuredComposition> MeasuredUnstated = new()
    {
        ["por"] = new(AlignmentPipeline.DefaultMinimumConfidence,
            "every answer from 0.25 with nothing stated to measure against; about 95% right on a hand-scored sample"),
    };

    /// <summary>
    /// How often a composed link running through a middle link of this size turns out to be right,
    /// as a share of how often one running through a stated one-to-one pair does.
    ///
    /// A link naming one English word and one Hebrew word states that pair, and carrying a
    /// correspondence through it adds almost no doubt. A link naming a phrase states that the
    /// phrase renders the word, and which of its words a foreign one arrives at is a question the
    /// file does not answer -- so composing through it is worth less than the whole of the claim.
    ///
    /// **How much less is measured, not assumed.** This was 1/n, which is what a pure n-way choice
    /// would cost, and it was between two and three times too harsh. Against the 2,647 links the
    /// Ukrainian interlinear states, the path UBIO -&gt; KJV -&gt; original agrees with the interlinear:
    ///
    /// <code>
    /// English words on the middle link   proposals   agrees
    ///                                1        1364    89.1 %
    ///                                2         622    80.4 %
    ///                                3         455    71.0 %
    ///                                4         126    69.8 %
    ///                           5 or more       80    56.3 %
    /// </code>
    ///
    /// So a three-word phrase is worth 0.80 of a stated pair and not 0.33, and the comment that
    /// stood here -- *those running through a phrase are mostly not right* -- was wrong: at every
    /// size measured they are mostly right. What 1/n cost was Genesis 1:3, where the King James
    /// states that *Let there be* renders יְהִי and the Russian **будет** aligns to *be* at 0.55.
    /// A third of that is 0.18, under the floor, so the word reached nothing at all while **да**
    /// beside it reached the Hebrew by the direct route.
    ///
    /// The sample is the only stated word-level correspondence a Slavic text has, so it is small
    /// and it leans towards the words an interlinear chooses to annotate, which are content words.
    /// Beyond five the counts are in the tens and the buckets are pooled rather than believed
    /// separately. Re-measure it when another stated mapping arrives, by scoring the composed
    /// proposals against the stated pairs, bucketed by the number of English words on the middle
    /// link.
    /// </summary>
    private static readonly double[] Confirmed = [0.891, 0.804, 0.710, 0.698, 0.563];

    internal static double SpecificityOf(long englishWords) =>
        Confirmed[(int)Math.Clamp(englishWords, 1, Confirmed.Length) - 1] / Confirmed[0];

    /// <summary>
    /// The merged answers as rows of the pair's <c>composed</c> method file.
    ///
    /// <para>
    /// A pair a source has already stated is not written as a link of its own. The routes reach words the interlinear
    /// also annotates — the same word of the Ukrainian against the same word of the Hebrew — and a second link saying that
    /// is not a second fact: it makes a word pair nobody doubts read as contended, and it double-counts it in every measure.
    /// So the guess is a row of the stated link's shape, which the merge folds into that link as a claim, and the stated
    /// link keeps the source's identity because a guess can be rebuilt by running the aligner again and a statement cannot
    /// be rebuilt at all.
    /// </para>
    ///
    /// <para>
    /// A pair onto a word of the target that something other than the aligner says the translation does not render is not
    /// written either. That is the answer standing on the word, as an empty answer on a source word is; and a guess put
    /// back would show the word as unrendered and rendered at once. Nor is a pair the held editions' wording rules out
    /// (<see cref="AlignerFiles.Rejected"/>).
    /// </para>
    /// </summary>
    internal (List<MappingRow> Rows, int Fresh, int Corroborated) Rows(
        string fromSlug, string toSlug, string[] viaSlugs, IReadOnlyList<RoutedLink> merged, string? note)
    {
        var stated = _links.Stated(fromSlug, toSlug);
        var claimed = _links.Answered(fromSlug, toSlug);
        var unrendered = _links.Unrendered(fromSlug, toSlug);
        var rejected = aligner.Rejected(fromSlug, toSlug);
        var (fresh, agreeing) = Split(
            [.. merged.Where(link => !unrendered.Contains(link.To) && !rejected.Contains((link.From, link.To)))],
            stated, claimed, viaSlugs);
        var agreed = stated.ToDictionary(pair => pair.Value, pair => pair.Key);
        var (from, to) = (texts[fromSlug], texts[toSlug]);
        List<MappingRow> rows =
        [
            .. fresh.Select(link => Row(from, to, link.From, link.To, Routes.Written(link.Confidence),
                Routes.Describe(link.Route, viaSlugs) + (note is null ? string.Empty : $"; {note}"), null)),
            .. agreeing.Select(a => Row(from, to, agreed[a.Link].From, agreed[a.Link].To, a.Confidence, a.Source, null)),
        ];
        return (rows, fresh.Count, agreeing.Count);
    }

    private static MappingRow Row(ProductText from, ProductText to, long fromWord, long toWord, double confidence, string source, string? evidence) =>
        new(from.Key(fromWord).Own.ToString(), to.Key(toWord).Own.ToString(), "renders", MappingMethods.Composed, confidence, source, evidence,
            null, null);
    /// <summary>
    /// The merged pairs divided into the ones that become links of their own and the ones that
    /// become a second claim on a link a source already wrote.
    ///
    /// Each corroboration keeps the confidence and the source of the pair that produced it: the
    /// source names which readings found it, and that is the only thing a claim from this run says
    /// which the stated link does not say for itself.
    ///
    /// <para>
    /// A pair on a word some other method has already answered for, naming a different target
    /// word, is neither: it is dropped. The answer that stands on that word — a printed Strong
    /// number, a hand-made alignment — is the better-founded one, and a guess written beside it
    /// would make the word read as contended between the two. Over Luther's Old Testament and the
    /// Reina-Valera's, the aligner names another word on 10–12% of the stated words it reaches.
    /// </para>
    /// </summary>
    /// <param name="claimed">
    /// Every source word an answer stands on (<see cref="MappingLinks.Answered"/>), whatever its
    /// shape: a phrase a source states answers for each of its words as much as a pair does.
    /// </param>
    internal static (
        List<RoutedLink> Fresh,
        List<(long Link, double Confidence, string Source)> Agreeing) Split(
        IReadOnlyList<RoutedLink> merged,
        IReadOnlyDictionary<(long From, long To), long> stated,
        IReadOnlySet<long> claimed,
        params string[] viaSlugs)
    {
        var fresh = new List<RoutedLink>(merged.Count);
        var agreeing = new List<(long Link, double Confidence, string Source)>();

        foreach (var link in merged)
        {
            if (stated.TryGetValue((link.From, link.To), out var already))
            {
                agreeing.Add((already, Routes.Written(link.Confidence), Routes.Describe(link.Route, viaSlugs)));
            }
            else if (!claimed.Contains(link.From))
            {
                fresh.Add(link);
            }
        }

        return (fresh, agreeing);
    }

    /// <summary>
    /// What a run is measured against: the statements, each read as naming the whole written word
    /// of the target it names rather than the one word of it the corpus divides out.
    ///
    /// <para>
    /// Luther's printed Strong numbers and Clear Bible's alignments name the Hebrew as it is written,
    /// and reach this corpus on the host word. BHSA writes the conjunction, the article and the
    /// prepositions ב, כ, ל and מ as words of their own, so a statement about וַיֹּאמֶר lands on the
    /// verb, and the answer that gives the ו to <em>und</em> or <em>y</em> is scored as naming the
    /// wrong word. Of their 242,646 and 259,726 statements into BHSA, 2,401 and 770 name such a word,
    /// where the Berean's name 115,890 of 403,359; without this, a word rendering one of them could
    /// almost never be measured right.
    /// </para>
    ///
    /// <para>
    /// Only the words written joined to the next one are added, never the host, so an answer naming
    /// the noun is not made right by a statement about its article.
    /// </para>
    /// </summary>
    internal Dictionary<long, HashSet<long>> Key(string fromSlug, string toSlug)
    {
        var statements = _links.Statements(fromSlug, toSlug, rules: false);
        var together = WrittenTogether(Spelling(texts[toSlug]));
        foreach (var targets in statements.Values)
        {
            foreach (var target in targets.ToArray())
            {
                if (together.TryGetValue(target, out var joined))
                {
                    targets.UnionWith(joined);
                }
            }
        }

        return statements;
    }
    /// <summary>
    /// For each word of a written word the corpus divides, the parts of it written joined to the
    /// next: its proclitics.
    /// </summary>
    /// <param name="words">Every word of the text in reading order: its verse and what follows it.</param>
    internal static Dictionary<long, long[]> WrittenTogether(
        IReadOnlyList<(long Id, int Verse, string Trailer)> words)
    {
        var together = new Dictionary<long, long[]>();
        var start = 0;
        for (var i = 0; i < words.Count; i++)
        {
            var last = i + 1 == words.Count || words[i + 1].Verse != words[i].Verse || words[i].Trailer.Length > 0;
            if (!last)
            {
                continue;
            }

            if (i > start)
            {
                var joined = words.Skip(start).Take(i - start).Select(word => word.Id).ToArray();
                for (var at = start; at <= i; at++)
                {
                    together[words[at].Id] = joined;
                }
            }

            start = i + 1;
        }

        return together;
    }

    /// <summary>Every word of a text in reading order, by its place in the text: its verse and what follows it.</summary>
    internal static List<(long Id, int Verse, string Trailer)> Spelling(ProductText text)
    {
        var words = new List<(long, int, string)>(text.Words.Count);
        for (var verse = 0; verse < text.Verses.Count; verse++)
        {
            words.AddRange(text.InVerse(text.Verses[verse]).Select(id => ((long)id, verse, text.Words[id].Trailer)));
        }

        return words;
    }
}
