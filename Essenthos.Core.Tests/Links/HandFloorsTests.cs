using Essenthos.Core.Loading.Links;
using Essenthos.Core.Mapping;
using Essenthos.Core.Products;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Links;

/// <summary>
/// Floors a pair sets by hand for its composition, <c>mapping/&lt;pair&gt;/floors</c> (PRB-1607): the merge refuses the kept
/// composed rows under them, a composition takes them before what it measures, and an unmeasured combination one family of
/// readings found is refused only when a composition asks (NOT-0228).
/// </summary>
public sealed class HandFloorsTests : IDisposable
{
    private const string Pair = "UBIO-BHSA";
    private const string Direct = "SIL.Machine, aligned as written and as stems";
    private const string Middle = "SIL.Machine, aligned through kjv";
    private const string Both = "SIL.Machine, aligned as written and as stems and through KJV";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "essenthos-floors-test", Guid.NewGuid().ToString("N"));
    private readonly ProductStore _store;

    public HandFloorsTests() => _store = new ProductStore(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static readonly IReadOnlyList<HandFloor> Floors =
    [
        HandFloors.Parse(Pair, new HandFloorRow("as written and as stems", "0.9", "1", "14", "14 of 14 right")),
        HandFloors.Parse(Pair, new HandFloorRow("through kjv", HandFloorRow.Refused, "0.563", "52", "33 of 52 right")),
        HandFloors.Parse(Pair, new HandFloorRow("as written and as stems and through kjv", "0.25", "0.992", "64", "62 of 64 right")),
    ];

    private static MappingRow Composed(string from, string to, double confidence, string source) =>
        new(from, to, "renders", MappingMethods.Composed, confidence, source, null, null, null);

    private static MappingMerge.Output Merge(IEnumerable<MappingRow> rows, IReadOnlyList<HandFloor>? floors) =>
        MappingMerge.Merge(new MappingMerge.Input(
            Pair, "UBIO", "BHSA",
            rows.GroupBy(r => r.Method).ToDictionary(g => g.Key, g => g.ToList()),
            [], new Dictionary<(string, VerseKey), string>(), new Dictionary<string, IReadOnlyDictionary<VerseKey, string>?>(), 1,
            Floors: floors));

    [Fact]
    public void The_merge_refuses_composed_rows_under_their_combination_floor_and_says_why()
    {
        var output = Merge(
            [
                Composed("1.1:1.1", "1.1:1.1", 0.95, Direct),
                Composed("1.1:1.2", "1.1:1.2", 0.6, Direct),
                Composed("1.1:1.3", "1.1:1.3", 0.99, Middle + "; a note"),
                Composed("1.1:1.4", "1.1:1.4", 0.3, Both),
                Composed("1.1:1.5", "1.1:1.5", 0.3, "SIL.Machine, aligned as stems"),
            ],
            Floors);

        output.Final.Where(r => r.Settled).Select(r => r.From).Should().Equal(["1.1:1.1", "1.1:1.4", "1.1:1.5"],
            "a combination the file sets no floor for is left to the composition's own floor");
        var statuses = output.Methods[MappingMethods.Composed].Select(r => r.Status).ToList();
        statuses[1].Should().Be("refused-by floor: as written and as stems under 0.90; 14 of 14 right");
        statuses[2].Should().Be("refused-by floor: through kjv refused; 33 of 52 right", "the source's casing and note do not matter");
        statuses.Where((_, i) => i is 0 or 3 or 4).Should().OnlyContain(s => s == null);
        output.Report.Should().Contain(r => r.Kind == "refused-by" && r.Rows == 1 && r.Note == statuses[2]);
    }

    /// <summary>A word a refused composed row leaves is not handed anything: only what the competition would have kept stands.</summary>
    [Fact]
    public void A_link_the_refused_row_would_have_withdrawn_stands_as_it_would_without_it()
    {
        var aligner = new MappingRow("1.1:1.3", "1.1:1.9", "renders", MappingMethods.Aligner, 0.4, "SIL.Machine ibm4", null, null, null);

        var without = Merge([Composed("1.1:1.3", "1.1:1.3", 0.99, Middle), aligner], null);
        var with = Merge([Composed("1.1:1.3", "1.1:1.3", 0.99, Middle), aligner], Floors);

        without.Final.Single(r => r.Settled).Method.Should().Be(MappingMethods.Composed);
        with.Final.Single(r => r.Settled).Method.Should().Be(MappingMethods.Aligner);
        Merge([aligner], null).Final.Should().BeEquivalentTo(with.Final);
    }

    /// <summary>
    /// A pair's own model is held to the line <c>aligner</c> (PRB-1957): GNV1599-BHSA, given a model it never had, settled its
    /// guesses on every word the composition left bare, 21 of 40 right. A composition's combination is never read as that line.
    /// </summary>
    [Fact]
    public void The_merge_refuses_the_pair_s_aligner_rows_under_its_aligner_floor_and_leaves_compositions_to_theirs()
    {
        MappingRow Aligned(string from, string to, double confidence) =>
            new(from, to, "renders", MappingMethods.Aligner, confidence, "SIL.Machine ibm4, symmetrised och", null, null, null);
        IReadOnlyList<HandFloor> floors =
        [
            HandFloors.Parse(Pair, new HandFloorRow("aligner", "0.8", "0.9", "20", "18 of 20 right from 0.80")),
            HandFloors.Parse(Pair, new HandFloorRow("as written", HandFloorRow.Refused, "0.4", "30", "10 of 30 right")),
        ];

        var output = Merge(
            [
                Aligned("1.1:1.1", "1.1:1.1", 0.95),
                Aligned("1.1:1.2", "1.1:1.2", 0.6),
                Composed("1.1:1.3", "1.1:1.3", 0.3, "SIL.Machine, aligned as written"),
                Composed("1.1:1.4", "1.1:1.4", 0.3, Middle),
            ],
            floors);

        output.Final.Where(r => r.Settled).Select(r => r.From).Should().Equal(["1.1:1.1", "1.1:1.4"]);
        output.Methods[MappingMethods.Aligner].Select(r => r.Status).Should().Equal(null, "refused-by floor: aligner under 0.80; 18 of 20 right from 0.80");
        output.Methods[MappingMethods.Composed][0].Status.Should().Be("refused-by floor: as written refused; 10 of 30 right");
        HandFloors.For(floors, "SIL.Machine, aligned aligner").Should().BeNull("the aligner's line binds no composition");
        HandFloors.AsRoutes(Pair, floors, ["KJV"]).Should().ContainSingle("a composition reads no aligner line as a route");
    }

    [Fact]
    public void A_floors_file_is_read_from_the_store_and_is_no_method_file()
    {
        var folder = Path.Combine(_root, ProductStore.EssenthosFolder, MappingProducts.Folder, Pair);
        Directory.CreateDirectory(folder);
        File.WriteAllLines(Path.Combine(folder, "floors.tsv"),
        [
            "# producer   by hand",
            "readings\tfrom\tright\tscored\tbasis",
            "as written\trefused\t0.4\t30\t10 of 30 right",
            "As Written And As Stems\t0.9\t1\t14\tfrom 0.90 14 of 14",
        ]);

        var floors = HandFloors.Read(_store, Pair);

        floors.Select(f => (f.Name, f.From)).Should().Equal(("as written", null), ("as written and as stems", 0.9));
        HandFloors.For(floors, "SIL.Machine, aligned as stems and as written")!.From.Should().Be(0.9, "a combination is its set of readings");
        HandFloors.For(floors, "SIL.Machine ibm4").Should().BeNull("an aligner row is no composition's");
        MappingProducts.Methods(_store, Pair).Should().BeEmpty();
        HandFloors.Read(_store, "KJV-BHSA").Should().BeEmpty();
    }

    [Fact]
    public void A_composition_takes_hand_floors_before_what_it_measured()
    {
        var measured = Admission.Measure(
            Enumerable.Range(0, 400).Select(i => new RoutedLink(i, 1, 0.3 + i / 1000.0, Route.Written | Route.Reduced)),
            Enumerable.Range(0, 400).ToDictionary(i => (long)i, _ => new HashSet<long> { 1 }), 0.25);
        measured.Admits(new RoutedLink(1, 1, 0.5, Route.Written | Route.Reduced)).Should().BeTrue("measured right every time");

        var bound = measured.With(HandFloors.AsRoutes(Pair, Floors, ["KJV"]));

        bound.Admits(new RoutedLink(1, 1, 0.5, Route.Written | Route.Reduced)).Should().BeFalse("the hand floor is 0.9");
        bound.Admits(new RoutedLink(1, 1, 0.95, Route.Written | Route.Reduced)).Should().BeTrue();
        bound.Admits(new RoutedLink(1, 1, 1.0, Route.Composed)).Should().BeFalse("refused by hand");
        bound.Describe("KJV").Should().Contain("by hand: 33 of 52 right");
        FluentActions.Invoking(() => HandFloors.AsRoutes(Pair, Floors, ["BSB"])).Should().Throw<InvalidDataException>()
            .WithMessage("*through kjv*");
    }

    [Fact]
    public void An_unmeasured_combination_one_family_found_is_refused_only_when_asked()
    {
        var measured = Admission.Measure(
            Enumerable.Range(0, 400).Select(i => new RoutedLink(i, 1, 0.3 + i / 1000.0, Route.Written | Route.Composed)),
            Enumerable.Range(0, 400).ToDictionary(i => (long)i, _ => new HashSet<long> { 1 }), 0.25);
        var lone = new RoutedLink(1, 1, 0.5, Route.Composed);
        var agreed = new RoutedLink(1, 1, 0.5, Route.Reduced | Route.Composed);

        measured.Admits(lone).Should().BeTrue("off by default: an unmeasured combination keeps the threshold");
        measured.RefusingUnmeasured().Admits(lone).Should().BeFalse();
        measured.RefusingUnmeasured().Admits(agreed).Should().BeTrue("two families of readings found it");
        Admission.Everything(0.25).RefusingUnmeasured().Admits(lone).Should().BeTrue("a pair nothing measured is left as it is run");
    }
}
