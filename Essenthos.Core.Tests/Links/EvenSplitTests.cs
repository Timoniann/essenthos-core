using Essenthos.Core.Loading.Links;
using Essenthos.Core.Mapping;
using Essenthos.Core.Products;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Links;

/// <summary>
/// A word the model cannot place is shared evenly onto its neighbours' target, by the direct alignment and by the hop to a middle
/// text alike, so the two "agree" on the wrong word («диявола» on βέλη beside «стріли», Ephesians 6:16). A composition
/// counts two readings that split the word among the same words once; a pair's hold, where its floors switch it on, refuses a
/// weak composed link on an original word a surer composed link renders from other words.
/// </summary>
public sealed class EvenSplitTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "essenthos-split-test", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static readonly string[] Forms = ["розпечені", "стріли", "диявола", "диявола"];

    private static string Form(long word) => Forms[word];

    [Fact]
    public void Words_given_one_target_at_the_same_confidence_are_a_split_and_two_occurrences_of_one_word_are_not()
    {
        var split = Routes.Split([(0, 10, 0.33316974), (1, 10, 0.33316972), (2, 10, 0.33316857), (3, 11, 0.2)], Form);

        split[(2, 10)].Should().BeEquivalentTo([0L, 1L]);
        split.Should().NotContainKey((3, 11));
        Routes.Split([(1, 10, 0.5), (2, 10, 0.41)], Form).Should().BeEmpty("answers from evidence of their own differ");
        Routes.Split([(2, 10, 0.5), (3, 10, 0.5)], Form).Should().BeEmpty("the same word twice is a question of order");
    }

    private static Dictionary<(long, long), long[]> Shared(params (long From, long To, long[] With)[] splits) =>
        splits.ToDictionary(s => (s.From, s.To), s => s.With);

    [Fact]
    public void A_middle_text_splitting_the_word_with_a_surer_neighbour_adds_nothing_to_the_direct_split()
    {
        var splits = new Splits(Shared((2, 10, [0, 1]), (1, 10, [0, 2])), Splits.Empty,
            [Shared((2, 10, [1]), (1, 10, [2])), Splits.Empty]);
        IReadOnlyList<(long, long, double)> direct = [(2, 10, 0.333), (1, 10, 0.333)];
        IReadOnlyList<(long, long, double)> kjv = [(2, 10, 0.2), (1, 10, 0.2)];
        IReadOnlyList<(long, long, double)> bsb = [(1, 10, 0.9)];

        var merged = Routes.MergeFamilies(direct, [], [kjv, bsb], splits).ToDictionary(l => l.From);

        merged[2].Should().Be(new RoutedLink(2, 10, 0.333, Route.Written), "the King James route made the same split, and «стріли» has the Berean's too");
        merged[1].Route.Should().Be(Route.Written | Route.Composed | Route.ComposedAgain);
        Routes.MergeFamilies(direct, [], [kjv, bsb]).Single(l => l.From == 2).Route.Should().Be(Route.Written | Route.Composed,
            "without the splits the two read as independent");
    }

    [Fact]
    public void A_neighbour_found_as_written_where_the_word_was_found_as_stems_is_no_surer()
    {
        var splits = new Splits(Splits.Empty, Shared((2, 10, [1])), [Shared((2, 10, [1]))]);

        Routes.MergeFamilies([(1, 10, 0.5)], [(2, 10, 0.5), (1, 10, 0.5)], [[(2, 10, 0.6), (1, 10, 0.6)]], splits)
            .Single(l => l.From == 2).Route.Should().Be(Route.Reduced | Route.Composed, "the two direct readings are one family");
        Routes.Lacks(Route.Reduced, Route.Written | Route.Reduced).Should().BeFalse();
        Routes.Lacks(Route.Reduced | Route.Composed, Route.Composed | Route.ComposedAgain).Should().BeTrue();
    }

    [Fact]
    public void A_split_whose_words_reach_the_target_no_better_is_a_phrase_and_counts_twice()
    {
        var splits = new Splits(Shared((2, 10, [1]), (1, 10, [2])), Splits.Empty, [Shared((2, 10, [1]), (1, 10, [2]))]);

        Routes.MergeFamilies([(2, 10, 0.5), (1, 10, 0.5)], [], [[(2, 10, 0.6), (1, 10, 0.6)]], splits)
            .Should().OnlyContain(l => l.Route == (Route.Written | Route.Composed));
    }

    [Fact]
    public void A_split_with_other_words_or_an_answer_of_its_own_still_counts_as_a_second_family()
    {
        var otherNeighbour = new Splits(Shared((2, 10, [0])), Splits.Empty, [Shared((2, 10, [1]))]);
        var ownAnswer = new Splits(Shared((2, 10, [0, 1])), Splits.Empty, [Splits.Empty]);
        IReadOnlyList<(long, long, double)> neighbours = [(0, 10, 0.333), (1, 10, 0.98)];

        Routes.MergeFamilies([(2, 10, 0.333), (0, 10, 0.333)], [], [[(2, 10, 0.2), (1, 10, 0.9)]], otherNeighbour)
            .Single(l => l.From == 2).Route.Should().Be(Route.Written | Route.Composed);
        Routes.MergeFamilies([(2, 10, 0.333), .. neighbours], [], [[(2, 10, 0.9), (1, 10, 0.9)]], ownAnswer)
            .Single(l => l.From == 2).Route.Should().Be(Route.Written | Route.Composed);
    }

    [Fact]
    public void Two_middle_texts_splitting_with_the_same_surer_neighbour_are_one_family()
    {
        var splits = new Splits(Splits.Empty, Splits.Empty, [Shared((2, 10, [1])), Shared((2, 10, [1, 0]))]);

        Routes.MergeFamilies([(1, 10, 0.9)], [], [[(2, 10, 0.2), (1, 10, 0.2)], [(2, 10, 0.3), (1, 10, 0.3)]], splits)
            .Single(l => l.From == 2).Should().Be(new RoutedLink(2, 10, 0.3, Route.Composed));
    }

    [Fact]
    public void A_composed_pair_takes_the_split_of_the_hop_it_was_reached_through()
    {
        var store = new ProductStore(_root);
        ProductCorpus.Text(store, "NPU2022", "ukr", [("1.1:1", ["стріли", "диявола"])]);
        ProductCorpus.Text(store, "NESTLE1904", "grc", [("1.1:1", ["βέλη", "πονηροῦ"])]);
        var texts = new ProductTexts(store);
        var joins = VerseJoins.Of(store, texts["NPU2022"], texts["NESTLE1904"]);
        const long darts = 9_000_000, evil = 9_000_001;

        var (pairs, split) = CompositionPipeline.Compose(
            [(1, darts, 0.2), (1, evil, 0.004), (0, darts, 0.2)],
            new (long Bridge, long To, double Confidence)[] { (darts, 0, 1.0), (evil, 1, 1.0) }.ToLookup(r => r.Bridge, r => (r.To, r.Confidence)),
            joins,
            new Dictionary<(long From, long To), long[]> { [(1, darts)] = [0], [(0, darts)] = [1] });

        pairs.Should().HaveCount(3);
        split[(1, 0)].Should().Equal(0L);
        split.Should().NotContainKey((1, 1));
    }

    private static MappingRow Composed(string from, string to, double confidence) =>
        new(from, to, "renders", MappingMethods.Composed, confidence, "SIL.Machine, aligned as written and through KJV", null, null, null);

    private static MappingMerge.Output Merge(IEnumerable<MappingRow> rows, HandHold? hold) =>
        MappingMerge.Merge(new MappingMerge.Input(
            "NPU2022-NESTLE1904", "NPU2022", "NESTLE1904",
            rows.GroupBy(r => r.Method).ToDictionary(g => g.Key, g => g.ToList()),
            [], new Dictionary<(string, VerseKey), string>(), new Dictionary<string, IReadOnlyDictionary<VerseKey, string>?>(), 1,
            Hold: hold));

    private static readonly HandHold Hold = new(0.6, 0.31, 40, "a hand read");

    [Fact]
    public void The_hold_refuses_a_weak_composed_link_on_a_word_a_surer_one_renders_from_another_word()
    {
        var rows = new[]
        {
            Composed("49.6:16.11", "49.6:16.13", 0.98),
            Composed("49.6:16.12", "49.6:16.13", 0.447),
            Composed("49.6:16.10", "49.6:16.17", 0.5),
        };

        Merge(rows, null).Final.Count(r => r.Settled).Should().Be(3, "two compositions never hold a word against each other");
        var output = Merge(rows, Hold);

        output.Final.Where(r => r.Settled).Select(r => r.From).Should().Equal(["49.6:16.10", "49.6:16.11"]);
        output.Methods[MappingMethods.Composed][1].Status.Should().StartWith(MappingStatus.RefusedByHold + ": under 0.60").And.EndWith("a hand read");
        output.Report.Should().Contain(r => r.Kind == "held-composed" && r.Rows == 1);
    }

    [Fact]
    public void The_hold_needs_a_holder_of_its_confidence_surer_than_the_link_and_from_other_words()
    {
        Merge([Composed("1.1:1.1", "1.1:1.3", 0.59), Composed("1.1:1.2", "1.1:1.3", 0.5)], Hold)
            .Final.Count(r => r.Settled).Should().Be(2, "no holder reaches the hold's confidence");
        Merge([Composed("1.1:1.1", "1.1:1.3", 0.7), Composed("1.1:1.2", "1.1:1.3", 0.95)], Hold)
            .Final.Count(r => r.Settled).Should().Be(2, "the surer link is never the one held");
        Merge([Composed("1.1:1.1", "1.1:1.3", 0.9), Composed("1.1:1.2", "1.1:1.4", 0.5)], Hold)
            .Final.Count(r => r.Settled).Should().Be(2, "a link on another original word is not held");
    }

    [Fact]
    public void The_held_line_of_the_floors_file_switches_the_hold_on_and_is_no_floor()
    {
        var store = new ProductStore(_root);
        var folder = Path.Combine(_root, ProductStore.EssenthosFolder, MappingProducts.Folder, "NPU2022-NESTLE1904");
        Directory.CreateDirectory(folder);
        File.WriteAllLines(Path.Combine(folder, "floors.tsv"),
        [
            "# producer   by hand",
            "readings\tfrom\tright\tscored\tbasis",
            "as written\trefused\t0.4\t30\t10 of 30 right",
            "held\t0.6\t0.7\t40\t28 of 40 refused were wrong",
        ]);

        HandFloors.Read(store, "NPU2022-NESTLE1904").Select(floor => floor.Name).Should().Equal("as written", HandFloors.Others);
        HandFloors.Hold(store, "NPU2022-NESTLE1904").Should().Be(new HandHold(0.6, 0.7, 40, "28 of 40 refused were wrong"));
        HandFloors.Hold(store, "UBIO-NESTLE1904").Should().BeNull();
    }
}
