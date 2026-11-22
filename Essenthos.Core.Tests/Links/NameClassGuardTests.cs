using Essenthos.Core.Loading.Links;
using Essenthos.Core.Mapping;
using Essenthos.Core.Strong;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Links;

/// <summary>
/// The guard that keeps a laid numbering's slipped tag from sending a name to a common word or a common word to a name
/// (<see cref="NameClassGuard"/>, PRB-1962): Matthew 3:9 in the Synodal, where Bob Jones University's numbering swaps
/// G11 (Abraham) and G3962 (father) between <em>отец</em> and <em>Авраам</em>.
/// </summary>
public sealed class NameClassGuardTests
{
    private static readonly HashSet<string> Names = ["G11", "G2424"];

    /// <summary>Matthew 3:9's two slipped words (ids 0 and 1), beside the numbering's habit with both surfaces elsewhere.</summary>
    private static (NameClassGuard Guard, string[] Surfaces) Synodal(IReadOnlySet<long>? corrected = null)
    {
        var words = new List<(string Surface, string Number)> { ("отец", "G11"), ("Авраам", "G3962") };
        words.AddRange(Enumerable.Repeat(("Авраам", "G11"), 20));
        words.AddRange(Enumerable.Repeat(("отец", "G3962"), 30));
        words.AddRange(Enumerable.Repeat(("Иисуса", "G2424"), 10));
        words.Add(("Иисуса", "G846"));
        var tags = words.Select((word, id) => (Id: (long)id, Tag: new WordTag([word.Number], id))).ToDictionary(w => w.Id, w => w.Tag);
        var surfaces = words.Select(word => word.Surface).ToArray();
        return (NameClassGuard.Of(tags, corrected ?? new HashSet<long>(), id => surfaces[id], StrongNumbers.Greek, Names), surfaces);
    }

    private static readonly HashSet<string> MatthewThreeNine = ["G2532", "G3361", "G3962", "G2192", "G3588", "G11"];

    [Fact]
    public void A_name_its_tag_sends_to_a_common_word_is_refused_where_the_verse_holds_its_own_number()
    {
        var (guard, _) = Synodal();

        guard.Refuses(1, ["G3962"], MatthewThreeNine).Should().BeTrue("Авраам matched onto πατέρα while Ἀβραάμ stands in the verse");
        guard.Refuses(0, ["G11"], MatthewThreeNine).Should().BeTrue("отец matched onto Ἀβραάμ while πατέρα stands in the verse");
    }

    [Fact]
    public void A_word_matched_on_its_own_side_of_the_line_stands()
    {
        var (guard, _) = Synodal();

        guard.Refuses(2, ["G11"], MatthewThreeNine).Should().BeFalse("Авраам onto Ἀβραάμ");
        guard.Refuses(30, ["G3962"], MatthewThreeNine).Should().BeFalse("отец onto πατέρα");
    }

    [Fact]
    public void A_name_the_translation_supplies_for_a_pronoun_stands_where_the_verse_has_no_such_name()
    {
        var (guard, surfaces) = Synodal();
        var supplied = surfaces.Length - 1;

        guard.Refuses(supplied, ["G846"], new HashSet<string> { "G2532", "G846", "G3004" }).Should().BeFalse("Иисуса for αὐτόν: the verse names nobody");
        guard.Refuses(supplied, ["G846"], new HashSet<string> { "G2532", "G846", "G2424" }).Should().BeTrue("the verse holds Ἰησοῦς, which the name renders");
    }

    [Fact]
    public void A_number_a_correction_set_is_trusted()
    {
        var (guard, _) = Synodal(new HashSet<long> { 1 });

        guard.Refuses(1, ["G3962"], MatthewThreeNine).Should().BeFalse();
    }

    [Fact]
    public void A_surface_the_numbering_tags_too_seldom_or_both_ways_is_left_alone()
    {
        var tags = new Dictionary<long, WordTag>
        {
            [0] = new(["G3962"], 0), [1] = new(["G11"], 1), [2] = new(["G11"], 2), [3] = new(["G3962"], 3),
            [4] = new(["G3962"], 4), [5] = new(["G11"], 5),
        };
        var guard = NameClassGuard.Of(tags, new HashSet<long>(), id => id < 4 ? "отче" : "Авва", StrongNumbers.Greek, Names);

        guard.Refuses(0, ["G3962"], MatthewThreeNine).Should().BeFalse("four tags are too few to say what отче is");
        guard.Refuses(4, ["G3962"], MatthewThreeNine).Should().BeFalse();
    }

    [Fact]
    public void Names_are_the_greek_entries_with_a_capital_and_the_hebrew_numbers_mostly_marked_proper()
    {
        NameClassGuard.GreekNames([new GreekEntry("G0011", "Ἀβραάμ", null), new GreekEntry("G3962", "πατήρ", null)])
            .Should().BeEquivalentTo(["G11"]);

        const string proper = """{"pos": "nmpr"}""";
        const string noun = """{"pos": "subs"}""";
        NameClassGuard.HebrewNames([("H85", proper), ("H85", proper), ("H1", noun), ("H1", proper), ("H1", noun), ("G11", proper)])
            .Should().BeEquivalentTo(["H85"]);
    }
}
