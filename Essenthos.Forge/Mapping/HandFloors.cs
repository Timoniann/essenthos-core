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

    /// <summary>The pair's hand floors; empty where it has none.</summary>
    public static IReadOnlyList<HandFloor> Read(ProductStore store, string pair)
    {
        var product = MappingProducts.Of(pair, Name);
        if (!store.Exists(product))
        {
            return [];
        }

        using var reader = store.Open(product, HandFloorRow.Schema);
        var floors = reader.ReadAll().Select(row => Parse(pair, row)).ToList();
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

    /// <summary>The floor a composed link of this source is held to, if the pair sets one for its combination.</summary>
    public static HandFloor? For(IReadOnlyList<HandFloor> floors, string source) =>
        floors.Count == 0 || OfSource(source) is not { } readings
            ? null
            : floors.FirstOrDefault(floor => floor.Readings.SetEquals(readings));

    /// <summary>
    /// The floors as a composition through these middle texts numbers its routes, for <see cref="Admission.With"/>. A reading
    /// that names no route of this composition stops it: a floor that silently bound nothing would be worse than none.
    /// </summary>
    public static IReadOnlyDictionary<Route, RouteFloor> AsRoutes(string pair, IReadOnlyList<HandFloor> floors, IReadOnlyList<string> vias)
    {
        var routes = new Dictionary<Route, RouteFloor>();
        foreach (var floor in floors)
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
        const string Through = "through ";
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
