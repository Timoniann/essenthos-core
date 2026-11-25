using System.Globalization;

namespace Essenthos.Core.Loading.Links;

/// <param name="Confidence">The confidence from which answers found this way are written.</param>
/// <param name="Scored">The answers on stated words the floor was measured over.</param>
/// <param name="Precision">How often those at or above the floor named the stated word.</param>
/// <param name="Basis">
/// Where it was measured, when not on the pair being written: the text whose statements it was read
/// off, or the combinations it was taken from.
/// </param>
internal sealed record RouteFloor(double Confidence, int Scored, double Precision, string? Basis = null)
{
    public bool Refused => Confidence > 1;
}

/// <summary>
/// Which merged answers a composition writes: for each combination of readings that found an
/// answer, the confidence it must reach, measured on the pair being written against what a source
/// already states for it.
///
/// <para>
/// A single threshold assumes that a confidence means the same thing whichever readings produced
/// it, and it does not. Against Luther's printed Strong numbers, which no reading sees, an answer
/// the direct alignment and the route through the King James both found named the stated Hebrew
/// word 96% of the time; one the direct alignment found alone, 36–68%, and its own confidence
/// hardly separated its right answers from its wrong ones. Over the Synodal's New Testament the
/// same lone answers were right 75–92% of the time and their confidence ranked them. So the floor
/// is read per combination, per pair: the lowest confidence from which that combination's answers
/// on the stated words were right at least <see cref="DefaultPrecision"/> of the time, unless a run
/// asks for another bar.
/// </para>
///
/// <para>
/// What it measures on is what a source states, and that leans towards content words: a printed
/// Strong number stands on a noun or a verb far more often than on an article. The words a run adds
/// are mostly the others, and are harder, so the figure is an upper bound on how often they are
/// right. That is why the bar is set high.
/// </para>
///
/// <para>
/// A combination with too few stated answers to measure keeps the ordinary threshold, as every
/// composition did before this was measured, and the outcome says which ones were not measured.
/// A pair with nothing stated at all — most English translations against the originals — borrows
/// its floors from texts in its language that are stated, composed the same way (<see cref="Borrow"/>).
/// </para>
/// </summary>
internal sealed class Admission
{
    /// <summary>
    /// How often a combination's written answers must name the word a source states. At 0.9 a
    /// composition adds only answers that are right about as often as the stated links beside them
    /// were found to be by every check made on them; lowering it buys coverage with the words the
    /// direct model and the middle texts disagree about.
    /// </summary>
    public const double DefaultPrecision = 0.9;

    /// <summary>Fewer stated answers than this, and a precision read off them is noise.</summary>
    public const int FewestToMeasure = 200;

    /// <summary>A floor no confidence reaches: the combination is refused.</summary>
    internal const double Unreachable = 1.01;

    private static readonly double[] Bands = [0.25, 0.4, 0.5, 0.6, 0.7, 0.8, 0.9];

    private readonly IReadOnlyDictionary<Route, RouteFloor> _floors;
    private readonly double _threshold;
    private readonly double _precision;
    private readonly string? _measuredOn;
    private readonly bool _refusesUnmeasured;
    private readonly Added? _added;
    private readonly IReadOnlySet<Route>? _named;
    private readonly bool _direct;

    /// <summary>
    /// A middle text added to a composition that stood without it (<see cref="Keeping"/>): its route, its name, and the
    /// answers the composition without it writes.
    /// </summary>
    private sealed record Added(Route Route, string Via, IReadOnlySet<(long From, long To)> Standing);

    private Admission(
        IReadOnlyDictionary<Route, RouteFloor> floors, double threshold, double precision, string? measuredOn = null,
        bool refusesUnmeasured = false, Added? added = null, IReadOnlySet<Route>? named = null, bool direct = false)
    {
        _floors = floors;
        _threshold = threshold;
        _precision = precision;
        _measuredOn = measuredOn;
        _refusesUnmeasured = refusesUnmeasured;
        _added = added;
        _named = named;
        _direct = direct;
    }

    /// <summary>Every answer from the ordinary threshold, which is what a measurement starts from.</summary>
    public static Admission Everything(double threshold) =>
        new(new Dictionary<Route, RouteFloor>(), threshold, 0);

    public IReadOnlyDictionary<Route, RouteFloor> Floors => _floors;

    public bool Admits(RoutedLink link) =>
        !Seconded(link)
        && !Unnamed(link.Route)
        && (_floors.TryGetValue(link.Route, out var floor)
            ? link.Confidence >= floor.Confidence
            : !Unmeasured(link.Route) && link.Confidence >= _threshold);

    /// <summary>
    /// Whether an answer the added middle text's route found with only one other family of readings is one the composition
    /// without it does not write, and so is refused (<see cref="Keeping"/>).
    /// </summary>
    private bool Seconded(RoutedLink link) =>
        _added is { } added && link.Route.HasFlag(added.Route) && Routes.Families(link.Route) < 3
        && !added.Standing.Contains((link.From, link.To));

    /// <summary>
    /// Whether a combination nothing measured is refused rather than written from the ordinary threshold: only when asked
    /// (<see cref="RefusingUnmeasured"/>), only for a combination one family of readings found (<see cref="Routes.Families"/>),
    /// and only in a pair where something was measured at all.
    /// </summary>
    private bool Unmeasured(Route route) => _refusesUnmeasured && _floors.Count > 0 && Routes.Families(route) == 1;

    /// <summary>
    /// Whether an answer is refused because no hand floor names its combination (<see cref="RefusingUnnamed"/>): any
    /// combination, or only one the middle texts found with no direct reading.
    /// </summary>
    private bool Unnamed(Route route) =>
        _named is { } named && !named.Contains(route) && (_direct || (route & (Route.Written | Route.Reduced)) == 0);

    /// <summary>
    /// The same, refusing every answer whose combination of readings no floor set by hand names (NOT-0228), so that nothing is
    /// written that was not read. A floor measured on statements, or borrowed from proxies, leans towards the content words a
    /// source states, and a composition writes mostly the others: hand-read, the King James and Berean routes of three English
    /// texts to the Hebrew were right 48-61% of the time, where their proxies measured 95-98%, and the English New Testament
    /// pairs' direct combinations, admitted from proxies at 90-93%, wrote new links of which 45-70% were right. A daughter,
    /// which reaches the target through its middle text by design, is not held to this.
    /// </summary>
    /// <param name="hand">The pair's hand floors, as its routes; a combination among them is judged by its floor.</param>
    /// <param name="direct">
    /// Whether combinations with a direct reading are held to it too; where not, only what the middle texts alone found is
    /// refused, and the rest stands on the floors measured or borrowed (a pair not yet read by hand).
    /// </param>
    public Admission RefusingUnnamed(IReadOnlyDictionary<Route, RouteFloor> hand, bool direct = true) =>
        new(_floors, _threshold, _precision, _measuredOn, _refusesUnmeasured, _added, hand.Keys.ToHashSet(), direct);

    /// <summary>
    /// The same, with floors measured by hand put before what was measured or borrowed (<c>mapping/&lt;pair&gt;/floors</c>,
    /// PRB-1607). A hand sample reads the words a source states and the words it does not, the pronouns and function words
    /// where composed answers go wrong most, so where one was made it is the better measure of the combination.
    /// </summary>
    public Admission With(IReadOnlyDictionary<Route, RouteFloor> hand)
    {
        if (hand.Count == 0)
        {
            return this;
        }

        var floors = new Dictionary<Route, RouteFloor>(_floors);
        foreach (var (route, floor) in hand)
        {
            floors[route] = floor;
        }

        return new Admission(floors, _threshold, _precision, _measuredOn, _refusesUnmeasured, _added, _named, _direct);
    }

    /// <summary>
    /// The same, with a middle text added to a composition that stood without it: every combination its route is not in is
    /// written from the floor <paramref name="before"/> set for it, where that is the lower one (PRB-0863).
    ///
    /// <para>
    /// Luther is the text the Elberfelder's composition through the King James and the Berean measured its floors on. Made a
    /// middle text, he can no longer measure what the other readings find without him, since he has no route through himself,
    /// and those answers would be refused although the composition without him writes them. An answer his route did not find
    /// has the confidence it had without him, so written from the floor it had then it is an answer the composition without him
    /// writes: nothing is admitted that was not, and nothing written before is taken away for his being added. What his route
    /// does find with two other families of readings or more is measured as any combination is.
    /// </para>
    ///
    /// <para>
    /// What his route finds with only one other family is written only where the composition without him writes the same
    /// answer: he may second it, never bring it in. Read by hand on 2026-11-16, the Elberfelder's New Testament words his
    /// route brought in with one other family were right 75 of 84 times (89%: <em>unter sich</em> on ἀσύμφωνοι, <em>und</em>
    /// on the verb after a participle, a pronoun on the wrong word), the ones it brought in with two or more 38 of 38.
    /// </para>
    /// </summary>
    /// <param name="before">The admission of the same pair through the other middle texts, numbered as they are here.</param>
    /// <param name="added">The route of the middle text added.</param>
    /// <param name="via">Its name, for the floor's basis.</param>
    /// <param name="standing">The answers the composition through the other middle texts admits.</param>
    public Admission Keeping(Admission before, Route added, string via, IReadOnlySet<(long From, long To)> standing)
    {
        var floors = new Dictionary<Route, RouteFloor>(_floors);
        foreach (var (route, floor) in before._floors)
        {
            if (route.HasFlag(added) || floor.Refused || !floors.TryGetValue(route, out var measured)
                || measured.Confidence <= floor.Confidence)
            {
                continue;
            }

            floors[route] = floor with { Basis = $"as before {via} was a middle text{(floor.Basis is null ? string.Empty : $", {floor.Basis}")}" };
        }

        return new Admission(floors, _threshold, _precision, _measuredOn, _refusesUnmeasured, new Added(added, via, standing), _named, _direct);
    }

    /// <summary>
    /// The same, refusing what one family of readings found where nothing measured that combination, instead of writing it
    /// from the ordinary threshold (NOT-0228: an unmeasured combination is not admitted on a default floor). Off unless a
    /// composition asks for it: the owner sees what it removes, text by text, before it is turned on (PRB-1607).
    /// </summary>
    public Admission RefusingUnmeasured(bool refuse = true) =>
        refuse == _refusesUnmeasured ? this : new Admission(_floors, _threshold, _precision, _measuredOn, refuse, _added, _named, _direct);

    /// <summary>
    /// Whether a source's statements can measure answers at all: enough of its words have a word they
    /// render. A source that states only what renders nothing — the words a translation's italics or
    /// brackets print as its own — makes every answer on them wrong by construction, so the precision
    /// read off it is 0 whatever the readings did.
    /// </summary>
    public static bool Measures(IReadOnlyDictionary<long, HashSet<long>> statements) =>
        statements.Values.Count(targets => targets.Count > 0) >= FewestToMeasure;

    /// <param name="every">The merged answers with every combination admitted from the threshold.</param>
    /// <param name="statements">The target words a source states for each source word.</param>
    /// <param name="precision">The bar, <see cref="DefaultPrecision"/> unless a run asks for another.</param>
    public static Admission Measure(
        IEnumerable<RoutedLink> every,
        IReadOnlyDictionary<long, HashSet<long>> statements,
        double threshold,
        double precision = DefaultPrecision)
    {
        var floors = new Dictionary<Route, RouteFloor>();
        foreach (var route in every.Where(link => statements.ContainsKey(link.From)).GroupBy(link => link.Route))
        {
            var answers = route.OrderByDescending(link => link.Confidence).ToList();
            if (answers.Count >= FewestToMeasure)
            {
                floors[route.Key] = Floor(answers, statements, precision);
            }
        }

        return new Admission(floors, threshold, precision);
    }

    /// <summary>
    /// Floors for a pair nothing states, read off other texts in its language that state the target,
    /// each composed through this pair's middle texts except itself and measured on its own
    /// statements as a pair of its own would be.
    ///
    /// <para>
    /// Such a text reads only some of this pair's routes: the Berean composed through the King James
    /// has no route through the Berean. What it measured for a combination speaks for this pair's
    /// answers only where the route it could not read <em>also</em> found them. Composed through
    /// both middle texts, the answers the direct model and the King James agree on split into those
    /// the Berean reached too and those it did not, and the second are the harder half: over
    /// Luther's Old Testament, 98% and 89% against his printed Strong numbers, where the two together
    /// are 96%. So the Berean's measure of the direct model and the King James agreeing is a floor
    /// for the answers all three reached — they are the easier half of what it measured — and says
    /// nothing about the answers the Berean did not reach, which are the harder half.
    /// </para>
    ///
    /// <para>
    /// A combination is written from the floor measured on a text for the part of it that text read,
    /// if the routes that text could not read all found the answer; the lowest such floor stands,
    /// since each is a floor. A combination no text can speak for in that way is refused, because
    /// nothing measured it.
    /// </para>
    /// </summary>
    /// <param name="measured">
    /// Each text measured, the routes of this pair it read, and its floors keyed by those routes.
    /// </param>
    /// <param name="vias">This pair's middle texts, which name the routes and say how many there are.</param>
    public static Admission Borrow(
        IReadOnlyList<(string Text, Route Reads, IReadOnlyDictionary<Route, RouteFloor> Floors)> measured,
        string[] vias,
        double threshold,
        double precision = DefaultPrecision)
    {
        if (measured.Count == 0)
        {
            return new Admission(new Dictionary<Route, RouteFloor>(), threshold, precision);
        }

        var texts = measured.Select(text => text.Text).Distinct().Order(StringComparer.Ordinal).ToList();
        var every = Possible(vias.Length).Aggregate(Route.None, (all, route) => all | route);
        var floors = new Dictionary<Route, RouteFloor>();
        foreach (var route in Possible(vias.Length))
        {
            var lowest = measured
                .Where(text => (every & ~text.Reads & route) == (every & ~text.Reads))
                .Select(text => (text.Text, Part: route & text.Reads, text.Floors))
                .Where(cut => cut.Floors.ContainsKey(cut.Part))
                .Select(cut => (cut.Text, cut.Part, Floor: cut.Floors[cut.Part]))
                .OrderBy(cut => cut.Floor.Confidence)
                .ThenBy(cut => cut.Text, StringComparer.Ordinal)
                .FirstOrDefault();

            floors[route] = lowest.Floor is null
                ? new RouteFloor(Unreachable, 0, 0, $"measured on neither {string.Join(" nor ", texts)}")
                : lowest.Floor with
                {
                    Basis = lowest.Part == route
                        ? $"on {lowest.Text}"
                        : $"as {Name(lowest.Part, vias)} on {lowest.Text}",
                };
        }

        return new Admission(floors, threshold, precision, string.Join(" and ", texts));
    }

    /// <summary>Every combination of readings a pair through this many middle texts can produce.</summary>
    private static IEnumerable<Route> Possible(int middles)
    {
        var flags = new[] { Route.Written, Route.Reduced }.Concat(Routes.Middles.Take(middles)).ToArray();
        for (var mask = 1; mask < 1 << flags.Length; mask++)
        {
            yield return flags.Where((_, i) => (mask & (1 << i)) != 0).Aggregate(Route.None, (all, flag) => all | flag);
        }
    }

    /// <summary>
    /// How often each combination's answers on stated words named the stated word, from each
    /// confidence up: what a floor is read off, printed so that a bar can be chosen by looking.
    /// </summary>
    public static string Curves(
        IEnumerable<RoutedLink> every,
        IReadOnlyDictionary<long, HashSet<long>> statements,
        params string[] vias)
    {
        var lines = new List<string>();
        foreach (var route in every.Where(link => statements.ContainsKey(link.From)).GroupBy(link => link.Route)
                     .Where(route => route.Count() >= FewestToMeasure)
                     .OrderByDescending(route => route.Count()))
        {
            var bands = Bands.Select(band =>
            {
                var above = route.Where(link => link.Confidence >= band).ToList();
                var right = above.Count(link => statements[link.From].Contains(link.To));
                return above.Count == 0
                    ? $"{Number(band)} –"
                    : $"{Number(band)} {(double)right / above.Count:P1} of {above.Count}";
            });
            lines.Add($"    {Name(route.Key, vias)}: {string.Join("; ", bands)}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>
    /// Read from the top down, because what is asked is whether everything written from a
    /// confidence on is good enough, not whether one band is.
    /// </summary>
    private static RouteFloor Floor(
        List<RoutedLink> answers,
        IReadOnlyDictionary<long, HashSet<long>> statements,
        double bar)
    {
        RouteFloor? floor = null;
        var hits = 0;
        for (var i = 0; i < answers.Count; i++)
        {
            hits += statements[answers[i].From].Contains(answers[i].To) ? 1 : 0;
            var boundary = i + 1 == answers.Count || answers[i + 1].Confidence < answers[i].Confidence;
            var precision = (double)hits / (i + 1);
            if (boundary && i + 1 >= FewestToMeasure && precision >= bar)
            {
                floor = new RouteFloor(answers[i].Confidence, i + 1, precision);
            }
        }

        return floor ?? new RouteFloor(Unreachable, answers.Count, (double)hits / answers.Count);
    }

    /// <param name="vias">The middle texts, which name the routes through them.</param>
    public string Describe(params string[] vias)
    {
        if (_floors.Count == 0)
        {
            return _named is not null && _direct
                ? "nothing: no hand floor names a combination of readings"
                : $"every answer from {Number(_threshold)}, with nothing stated to measure against" +
                  (_named is null ? string.Empty : ", but what only the middle texts found");
        }

        var written = _floors.Where(entry => !entry.Value.Refused && !Unnamed(entry.Key))
            .OrderByDescending(entry => entry.Value.Scored)
            .Select(entry => $"{Name(entry.Key, vias)} from {Number(entry.Value.Confidence)} " +
                             $"({entry.Value.Precision:P0} of {entry.Value.Scored}{Basis(entry.Value)})");
        var refused = _floors.Where(entry => (entry.Value.Refused || Unnamed(entry.Key)) && entry.Value.Scored > 0)
            .OrderByDescending(entry => entry.Value.Scored)
            .Select(entry => $"{Name(entry.Key, vias)} ({entry.Value.Precision:P0} of {entry.Value.Scored}{Basis(entry.Value)})");
        var unmeasured = _floors.Where(entry => entry.Value.Scored == 0)
            .Select(entry => Name(entry.Key, vias))
            .ToList();

        var where = _measuredOn is null
            ? string.Empty
            : $" on {_measuredOn}, which state the target where this pair states nothing";
        return $"each combination of readings measured against {_precision:P0}{where}; " +
               $"written: {string.Join(", ", written)}; refused: {string.Join(", ", refused)}; " +
               (_added is null ? string.Empty : $"what {_added.Via} found with one other family of readings only where the composition without it writes it; ") +
               (_named is null ? string.Empty
                   : _direct ? "every combination no hand floor names refused; "
                   : "what only the middle texts found refused, unless a hand floor admits it; ") +
               (unmeasured.Count == 0 ? string.Empty : $"refused as measured on neither: {string.Join(", ", unmeasured)}; ") +
               (_named is not null && _direct ? "nothing else"
                   : (_refusesUnmeasured ? "anything else one family of readings found is refused as measured by nothing, the rest " : "anything else ") +
                     $"from {Number(_threshold)}");
    }

    public static string Name(Route route, params string[] vias) =>
        string.Join("+", new[]
            {
                route.HasFlag(Route.Written) ? "W" : null,
                route.HasFlag(Route.Reduced) ? "R" : null,
            }
            .Concat(Routes.Middles.Select((middle, i) => route.HasFlag(middle) && i < vias.Length ? vias[i] : null))
            .OfType<string>());

    private static string Basis(RouteFloor floor) => floor.Basis is null ? string.Empty : $" {floor.Basis}";

    private static string Number(double value) => value.ToString("F2", CultureInfo.InvariantCulture);
}
