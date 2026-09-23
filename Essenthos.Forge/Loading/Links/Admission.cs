using System.Globalization;

namespace Essenthos.Core.Loading.Links;

/// <param name="Confidence">The confidence from which answers found this way are written.</param>
/// <param name="Scored">The answers on stated words the floor was measured over.</param>
/// <param name="Precision">How often those at or above the floor named the stated word.</param>
internal sealed record RouteFloor(double Confidence, int Scored, double Precision)
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

    private readonly IReadOnlyDictionary<Route, RouteFloor> _floors;
    private readonly double _threshold;
    private readonly double _precision;

    private Admission(IReadOnlyDictionary<Route, RouteFloor> floors, double threshold, double precision)
    {
        _floors = floors;
        _threshold = threshold;
        _precision = precision;
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
                             $"({entry.Value.Precision:P0} of {entry.Value.Scored})");
        var refused = _floors.Where(entry => entry.Value.Refused)
            .OrderByDescending(entry => entry.Value.Scored)
            .Select(entry => $"{Name(entry.Key, vias)} ({entry.Value.Precision:P0} of {entry.Value.Scored})");

        return $"each combination of readings measured against {_precision:P0}; " +
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

    private static string Number(double value) => value.ToString("F2", CultureInfo.InvariantCulture);
}
