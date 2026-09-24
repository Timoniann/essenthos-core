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
    /// direct model and the two middle texts disagree about.
    /// </summary>
    public const double DefaultPrecision = 0.9;

    /// <summary>Fewer stated answers than this, and a precision read off them is noise.</summary>
    public const int FewestToMeasure = 200;

    private const double Unreachable = 1.01;

    private static readonly double[] Bands = [0.25, 0.4, 0.5, 0.6, 0.7, 0.8, 0.9];

    private readonly IReadOnlyDictionary<Route, RouteFloor> _floors;
    private readonly double _threshold;
    private readonly double _precision;
    private readonly string? _measuredOn;

    private Admission(
        IReadOnlyDictionary<Route, RouteFloor> floors, double threshold, double precision, string? measuredOn = null)
    {
        _floors = floors;
        _threshold = threshold;
        _precision = precision;
        _measuredOn = measuredOn;
    }

    /// <summary>Every answer from the ordinary threshold, which is what a measurement starts from.</summary>
    public static Admission Everything(double threshold) =>
        new(new Dictionary<Route, RouteFloor>(), threshold, 0);

    public IReadOnlyDictionary<Route, RouteFloor> Floors => _floors;

    public bool Admits(RoutedLink link) =>
        link.Confidence >= (_floors.TryGetValue(link.Route, out var floor) ? floor.Confidence : _threshold);

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
    /// cannot say what the route through the Berean adds. So an answer's combination is cut down to
    /// the routes each text read, and the answer is written from the floor that text measured for
    /// what is left — which is to say, where a composition through that text's middle texts alone
    /// would have written it, at a precision measured on a text of the same language composed
    /// exactly that way. Where several would, the lowest floor stands, since any one of them would
    /// have written the answer. What none of them measured keeps the threshold.
    /// </para>
    ///
    /// <para>
    /// The cut is what keeps the measure honest. Measured whole on the Berean, the direct model and
    /// the King James agreeing are right 94% of the time; composed through both middle texts, the
    /// same agreement splits into the answers the Berean also reached and those it did not, and the
    /// second are the harder half. A composition through the King James alone writes both halves
    /// together, and that whole is what the 94% was measured on.
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
        var floors = new Dictionary<Route, RouteFloor>();
        foreach (var route in Possible(vias.Length))
        {
            var loosest = measured
                .Select(text => (text.Text, Part: route & text.Reads, text.Floors))
                .Where(cut => cut.Part != Route.None && cut.Floors.ContainsKey(cut.Part))
                .Select(cut => (cut.Text, cut.Part, Floor: cut.Floors[cut.Part]))
                .OrderBy(cut => cut.Floor.Confidence)
                .ThenBy(cut => cut.Text, StringComparer.Ordinal)
                .FirstOrDefault();
            if (loosest.Floor is not null)
            {
                floors[route] = loosest.Floor with
                {
                    Basis = loosest.Part == route
                        ? $"on {loosest.Text}"
                        : $"as {Name(loosest.Part, vias)} on {loosest.Text}",
                };
            }
        }

        var texts = measured.Select(text => text.Text).Distinct().Order(StringComparer.Ordinal).ToList();
        return new Admission(floors, threshold, precision, texts.Count == 0 ? null : string.Join(" and ", texts));
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
            return $"every answer from {Number(_threshold)}, with nothing stated to measure against";
        }

        var written = _floors.Where(entry => !entry.Value.Refused)
            .OrderByDescending(entry => entry.Value.Scored)
            .Select(entry => $"{Name(entry.Key, vias)} from {Number(entry.Value.Confidence)} " +
                             $"({entry.Value.Precision:P0} of {entry.Value.Scored}{Basis(entry.Value)})");
        var refused = _floors.Where(entry => entry.Value.Refused)
            .OrderByDescending(entry => entry.Value.Scored)
            .Select(entry => $"{Name(entry.Key, vias)} ({entry.Value.Precision:P0} of {entry.Value.Scored}{Basis(entry.Value)})");

        var where = _measuredOn is null
            ? string.Empty
            : $" on {_measuredOn}, which state the target where this pair states nothing";
        return $"each combination of readings measured against {_precision:P0}{where}; " +
               $"written: {string.Join(", ", written)}; refused: {string.Join(", ", refused)}; " +
               $"anything else from {Number(_threshold)}";
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
