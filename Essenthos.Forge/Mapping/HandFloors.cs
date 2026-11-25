using System.Globalization;
using Essenthos.Core.Loading.Links;
using Essenthos.Core.Products;

namespace Essenthos.Core.Mapping;

/// <summary>
/// A line of a pair's <c>mapping/&lt;pair&gt;/floors</c>, kept by hand (PRB-1607): the confidence from which one combination of a
/// composition's readings is written, set from a measurement made by hand rather than read off what a source states.
/// </summary>
/// <param name="Readings">The combination as a composed link's source names it after <c>aligned</c>: <c>as written and through kjv</c>.</param>
/// <param name="From">The confidence from which its links stand, or <see cref="Refused"/> for none of them.</param>
/// <param name="Right">
/// How often the links read at or above the floor were right, as a fraction; for a refused combination, all of its links read.
/// </param>
/// <param name="Scored">How many links that figure was read off.</param>
/// <param name="Basis">The measurement, in words a person can check: who read how many links, when, and what the rest scored.</param>
internal sealed record HandFloorRow(string Readings, string From, string Right, string Scored, string Basis)
{
    public const string Refused = "refused";

    public static readonly ProductSchema<HandFloorRow> Schema = new ProductSchema<HandFloorRow>()
        .Text("readings", r => r.Readings)
        .Text("from", r => r.From)
        .Text("right", r => r.Right)
        .Text("scored", r => r.Scored)
        .Text("basis", r => r.Basis)
        .Reads(row => new HandFloorRow(row.Text(0), row.Text(1), row.Text(2), row.Text(3), row.Text(4)));
}

/// <summary>
/// The line <c>held</c> of a pair's floors, read: a composed link under <see cref="From"/> whose word of the second text
/// a standing composed link of at least <see cref="From"/>, and surer than it, renders from other words of the first is refused.
/// The model shares a word it cannot place onto a neighbour's target, and the composition then puts a second translation word
/// on an original word a surer one already renders («диявола» beside «стріли» on βέλη, Ephesians 6:16). Switched on pair by
/// pair, after a hand read of what it removes.
/// </summary>
/// <param name="Right">How often the links it refuses were wrong, as a fraction of the <see cref="Scored"/> read by hand.</param>
internal sealed record HandHold(double From, double? Right, int Scored, string Basis)
{
    /// <summary>What the merge writes in the pair's statuses for a link the hold refuses.</summary>
    public string Status =>
        $"{MappingStatus.RefusedByHold}: under {HandFloor.Number(From)}, its original word rendered by a surer composed link from other words; {Basis}";
}

/// <summary>A floor set by hand for one combination of readings (<see cref="HandFloorRow"/>), read.</summary>
/// <param name="Readings">Each reading of the combination, lower-cased: <c>as written</c>, <c>as stems</c>, <c>through kjv</c>.</param>
/// <param name="From">The confidence from which its links stand; null where the combination is refused.</param>
internal sealed record HandFloor(IReadOnlySet<string> Readings, string Name, double? From, double? Right, int Scored, string Basis)
{
    /// <summary>Whether a composed link of this combination at this confidence is refused.</summary>
    public bool Refuses(double? confidence) => From is not { } floor || (confidence ?? 0) < floor;

    /// <summary>What the merge writes in the pair's statuses for a link the floor refuses.</summary>
    public string Status => From is { } floor
        ? $"{MappingStatus.RefusedByFloor}: {Name} under {Number(floor)}; {Basis}"
        : $"{MappingStatus.RefusedByFloor}: {Name} refused; {Basis}";

    internal static string Number(double value) => value.ToString("0.00", CultureInfo.InvariantCulture);
}

/// <summary>
/// The floors a pair's composition is held to by hand, <c>mapping/&lt;pair&gt;/floors</c> (PRB-1607, NOT-0228).
///
/// <para>
/// A composition reads its floors off what a source states for the pair, and where too little is stated it wrote every answer
/// from the ordinary threshold. Ohienko's Old Testament has one book stated (Esther), and a hand-scored sample outside it found
/// the answers only the King James route reached right 56% of the time, those the direct alignment reached as written alone
/// 40%. A hand measurement is the better one, so it binds twice: <c>compose</c> takes it before anything it measures or
/// borrows (<see cref="Admission.With"/>), and the merge refuses the composed rows under it, so a kept <c>composed</c> file
/// that was drawn before the measurement is held to it without being drawn again.
/// </para>
/// </summary>
internal static class HandFloors
{
    public const string Name = "floors";

    private const string Aligned = CompositionPipeline.Machine + ", aligned ";

    /// <summary>
    /// The <c>readings</c> of the line that switches the pair's hold on (<see cref="HandHold"/>): not a combination of readings
    /// but every composed link, held against a surer one.
    /// </summary>
    public const string Held = "held";

    /// <summary>
    /// The <c>readings</c> of the line that holds the pair's own aligner rows to a floor: not a composition's combination but the
    /// direct model's links, which a pair given a model of its own settles wherever nothing surer links a word. Measured by hand
    /// like a combination (PRB-1957: GNV1599-BHSA's new aligner links read 21 of 40 right).
    /// </summary>
    public const string Aligner = "aligner";

    /// <summary>
    /// The readings of the floor every pair but a daughter is held to without a line of its own: a composed link whose combination
    /// no line of the pair names is refused (<see cref="Admission.RefusingUnnamed"/>, which binds the composition the same way).
    /// A line of these readings may say so (<c>refused</c>) or keep the pair, until it is read by hand, on the floors its
    /// composition measures or borrows (<see cref="AsMeasured"/>).
    /// </summary>
    public const string Others = "every other combination";

    /// <summary>
    /// The <c>from</c> of the line <see cref="Others"/> that leaves a pair's unnamed combinations to its composition's own floors,
    /// except what only the middle texts found (<see cref="MiddlesAlone"/>), which stays refused.
    /// </summary>
    public const string AsMeasured = "as measured";

    /// <summary>The readings of the standing refusal of a pair whose other combinations stand as measured: what only the middle texts found.</summary>
    public const string MiddlesAlone = "through middle texts alone";

    private const string Through = "through ";

    private static readonly HandFloor Unnamed = new(
        new HashSet<string>(StringComparer.Ordinal) { Others }, Others, null, null, 0,
        "no hand floor of the pair names that combination (NOT-0228)");

    private static readonly HandFloor Middles = new(
        new HashSet<string>(StringComparer.Ordinal) { MiddlesAlone }, MiddlesAlone, null, null, 0,
        "only the middle texts found it, and no hand floor of the pair admits that (NOT-0228)");

    /// <summary>
    /// The pair's hand floors, its aligner's (<see cref="Aligner"/>) among them, and the refusal of every combination no line names
    /// (<see cref="Others"/>; only of what the middle texts alone found where the pair keeps the rest <see cref="AsMeasured"/>),
    /// unless the pair is a daughter's composition, which reaches its target through its middle text by design.
    /// </summary>
    public static IReadOnlyList<HandFloor> Read(ProductStore store, string pair)
    {
        var rows = Rows(store, pair).Where(row => !IsHold(row)).ToList();
        var others = rows.Where(IsOthers).ToList();
        if (others.Count > 1)
        {
            throw new InvalidDataException($"{MappingProducts.Of(pair, Name)}: the line '{Others}' stands twice.");
        }

        var measured = others.Count == 1 && Measured(pair, others[0]);
        var floors = Floors(pair, rows.Where(row => !IsOthers(row)));
        return ComposedSource.Pairs.Any(composed => composed.Daughter && composed.Name == pair)
            ? floors
            : [.. floors, measured ? Middles : Unnamed];
    }

    /// <summary>Whether the pair's floors refuse every combination no line names, not only what the middle texts alone found.</summary>
    public static bool NamesEvery(IReadOnlyList<HandFloor> floors) => floors.Any(IsOthers);

    private static bool IsOthers(HandFloorRow row) => string.Equals(row.Readings.Trim(), Others, StringComparison.OrdinalIgnoreCase);

    private static bool Measured(string pair, HandFloorRow row) => row.From.Trim().ToLowerInvariant() switch
    {
        AsMeasured => true,
        HandFloorRow.Refused => false,
        _ => throw new InvalidDataException(
            $"{pair} floors: the line '{Others}' is '{HandFloorRow.Refused}' or '{AsMeasured}', not '{row.From.Trim()}'."),
    };

    /// <summary>The pair's hold of composed links against surer ones, where its floors switch it on; null where they do not.</summary>
    public static HandHold? Hold(ProductStore store, string pair)
    {
        var lines = Rows(store, pair).Where(IsHold).ToList();
        if (lines.Count > 1)
        {
            throw new InvalidDataException($"{MappingProducts.Of(pair, Name)}: the line '{Held}' stands twice.");
        }

        if (lines.Count == 0)
        {
            return null;
        }

        var row = lines[0];
        return new HandHold(Real(pair, row.From, "from"),
            row.Right.Trim().Length == 0 ? null : Real(pair, row.Right, "right"),
            row.Scored.Trim().Length == 0 ? 0 : int.Parse(row.Scored.Trim(), CultureInfo.InvariantCulture),
            row.Basis.Trim());
    }

    private static bool IsHold(HandFloorRow row) => string.Equals(row.Readings.Trim(), Held, StringComparison.OrdinalIgnoreCase);

    private static List<HandFloorRow> Rows(ProductStore store, string pair)
    {
        var product = MappingProducts.Of(pair, Name);
        if (!store.Exists(product))
        {
            return [];
        }

        using var reader = store.Open(product, HandFloorRow.Schema);
        return reader.ReadAll();
    }

    private static IReadOnlyList<HandFloor> Floors(string pair, IEnumerable<HandFloorRow> rows)
    {
        var product = MappingProducts.Of(pair, Name);
        var floors = rows.Select(row => Parse(pair, row)).ToList();
        var twice = floors.GroupBy(f => Key(f.Readings), StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
        if (twice is not null)
        {
            throw new InvalidDataException($"{product}: the combination '{twice.First().Name}' has two floors.");
        }

        return floors;
    }

    internal static HandFloor Parse(string pair, HandFloorRow row)
    {
        var readings = Readings(row.Readings);
        if (readings.Count == 0)
        {
            throw new InvalidDataException($"{pair} floors: a line names no readings.");
        }

        double? from = row.From.Trim() == HandFloorRow.Refused ? null : Real(pair, row.From, "from");
        return new HandFloor(readings, row.Readings.Trim().ToLowerInvariant(), from,
            row.Right.Trim().Length == 0 ? null : Real(pair, row.Right, "right"),
            row.Scored.Trim().Length == 0 ? 0 : int.Parse(row.Scored.Trim(), CultureInfo.InvariantCulture),
            row.Basis.Trim());
    }

    /// <summary>The readings a combination names, lower-cased: <c>as written and through KJV</c> is {as written, through kjv}.</summary>
    public static IReadOnlySet<string> Readings(string combination) =>
        combination.ToLowerInvariant().Split(" and ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// The readings a composed link's source says found it (<see cref="Routes.Describe"/>): what follows <c>aligned</c>, up to a note
    /// after a semicolon; null for a source that is not a composition's.
    /// </summary>
    public static IReadOnlySet<string>? OfSource(string source)
    {
        if (!source.StartsWith(Aligned, StringComparison.Ordinal))
        {
            return null;
        }

        var readings = source[Aligned.Length..];
        var note = readings.IndexOf(';');
        return Readings(note < 0 ? readings : readings[..note]);
    }

    /// <summary>
    /// The floor a composed link of this source is held to: the pair's line for its combination, or, for a combination no line
    /// names, the standing refusal where the floors hold one (<see cref="Others"/>, or <see cref="MiddlesAlone"/> for a
    /// combination only middle texts found).
    /// </summary>
    public static HandFloor? For(IReadOnlyList<HandFloor> floors, string source)
    {
        if (floors.Count == 0 || OfSource(source) is not { } readings)
        {
            return null;
        }

        var line = floors.FirstOrDefault(floor => !IsAligner(floor) && !IsStanding(floor) && floor.Readings.SetEquals(readings));
        if (line is not null || readings.Count == 0 || floors.FirstOrDefault(IsStanding) is not { } standing
            || (IsMiddlesAlone(standing) && !readings.All(reading => reading.StartsWith(Through, StringComparison.Ordinal))))
        {
            return line;
        }

        var named = source[Aligned.Length..];
        var note = named.IndexOf(';');
        return standing with { Readings = readings, Name = (note < 0 ? named : named[..note]).Trim().ToLowerInvariant() };
    }

    /// <summary>The floor the pair's own aligner rows are held to (<see cref="Aligner"/>), if it sets one.</summary>
    public static HandFloor? ForAligner(IReadOnlyList<HandFloor> floors) => floors.FirstOrDefault(IsAligner);

    /// <summary>
    /// Whether the pair's floors refuse this method-file row: a composed row under its combination's floor, an aligner row under
    /// the aligner's. A refused row keeps nothing off a word for the rules that place names and possessives where no link stands.
    /// </summary>
    public static bool Refuses(IReadOnlyList<HandFloor> floors, MappingRow row) => floors.Count > 0 && row.Method switch
    {
        MappingMethods.Composed => For(floors, row.Source)?.Refuses(row.Confidence) == true,
        MappingMethods.Aligner => ForAligner(floors)?.Refuses(row.Confidence) == true,
        _ => false,
    };

    private static bool IsAligner(HandFloor floor) => floor.Readings.Count == 1 && floor.Readings.Contains(Aligner);

    private static bool IsMiddlesAlone(HandFloor floor) => floor.Readings.Count == 1 && floor.Readings.Contains(MiddlesAlone);

    private static bool IsOthers(HandFloor floor) => floor.Readings.Count == 1 && floor.Readings.Contains(Others);

    private static bool IsStanding(HandFloor floor) => IsMiddlesAlone(floor) || IsOthers(floor);

    /// <summary>
    /// The floors as a composition through these middle texts numbers its routes, for <see cref="Admission.With"/>. A reading
    /// that names no route of this composition stops it: a floor that silently bound nothing would be worse than none.
    /// </summary>
    public static IReadOnlyDictionary<Route, RouteFloor> AsRoutes(string pair, IReadOnlyList<HandFloor> floors, IReadOnlyList<string> vias)
    {
        var routes = new Dictionary<Route, RouteFloor>();
        foreach (var floor in floors.Where(floor => !IsAligner(floor) && !IsStanding(floor)))
        {
            var route = Route.None;
            foreach (var reading in floor.Readings)
            {
                route |= reading switch
                {
                    "as written" => Route.Written,
                    "as stems" => Route.Reduced,
                    _ => Middle(reading, vias) ?? throw new InvalidDataException(
                        $"{pair} floors: '{reading}' of '{floor.Name}' is no reading of a composition through {string.Join(" and ", vias)}."),
                };
            }

            routes[route] = new RouteFloor(floor.From ?? Admission.Unreachable, floor.Scored, floor.Right ?? 0, $"by hand: {floor.Basis}");
        }

        return routes;
    }

    private static Route? Middle(string reading, IReadOnlyList<string> vias)
    {
        if (!reading.StartsWith(Through, StringComparison.Ordinal))
        {
            return null;
        }

        for (var i = 0; i < Math.Min(vias.Count, Routes.Middles.Length); i++)
        {
            if (string.Equals(vias[i], reading[Through.Length..], StringComparison.OrdinalIgnoreCase))
            {
                return Routes.Middles[i];
            }
        }

        return null;
    }

    private static string Key(IReadOnlySet<string> readings) => string.Join('|', readings.Order(StringComparer.Ordinal));

    private static double Real(string pair, string value, string column) =>
        double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && number is >= 0 and <= 1
            ? number
            : throw new InvalidDataException($"{pair} floors: '{value}' in {column} is not a number from 0 to 1.");
}
