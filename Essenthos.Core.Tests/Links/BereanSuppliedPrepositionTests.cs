using Essenthos.Core.Berean;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Links;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Links;

/// <summary>
/// A Berean row of a preposition and bracketed words on a word without a preposition of its own, where the preposition is the
/// prefix of the word the English goes on to: the bracketed words render the row's word and the preposition renders the
/// prefix, which BHSA writes as a word of its own.
/// </summary>
public class BereanSuppliedPrepositionTests
{
    private const long Waw = 10;

    private const long Min = 30;

    private const long Lamed = 40;

    /// <summary>2 Samuel 23:21: הַחֲנִית «the spear», מִיַּד «hand», הַמִּצְרִי «from [his]», וַיַּהַרְגֵהוּ «and killed [the Egyptian]».</summary>
    [Fact]
    public void ThePrepositionRendersThePrefixOfTheNextWordAndTheBracketedWordRendersTheNoun()
    {
        var rows = new[]
        {
            Row(1, 1, " the spear ", "Article | Noun - feminine singular"),
            Row(2, 3, " hand ", "Preposition-m | Noun - feminine singular construct"),
            Row(3, 2, " from [his] ", "Article | Noun - proper - masculine singular"),
            Row(4, 4, " and killed [the Egyptian] ",
                "Conjunctive waw | Verb - Qal - Consecutive imperfect - third person masculine singular | third person masculine singular"),
        };
        var silences = new BereanLinks.Silences();

        var drafts = Pair(rows, [[20, 21], [Min, 31], [32, 33], [Waw, 34]], "the spear from his hand and killed the Egyptian",
            new Dictionary<long, string> { [Min] = "H4480" }, silences);

        drafts.Select(Shape).Should().BeEquivalentTo(
            "100,101>20,21", "102>30", "103>32,33 bracketed", "104>30,31", "105,106,107,108>10,34");
        silences.PrepositionsMoved.Should().Be(1);
    }

    /// <summary>
    /// Exodus 19:21 gives מֹשֶׁה «to [him]» after יְהוָה וַיֹּאמֶר «said the LORD»: the English goes on to a word without a
    /// prefixed ל, to one whose own English opens with the preposition that renders its prefix, or to one whose prefix is not
    /// the preposition the English names. The row is left as the table has it.
    /// </summary>
    [Theory]
    [InlineData("Verb - Qal - Imperative - masculine singular", " Go down ", "H9005")]
    [InlineData("Preposition-l | Verb - Qal - Infinitive construct", " to go down ", "H9005")]
    [InlineData("Preposition-b | Noun - masculine singular construct", " go down ", "H9003")]
    public void APrepositionWhoseNextWordHasNoPrefixForItIsUnchanged(string parsing, string next, string strong)
    {
        var rows = new[]
        {
            Row(1, 2, " said ", "Conjunctive waw | Verb - Qal - Consecutive imperfect - third person masculine singular"),
            Row(2, 1, " the LORD ", "Noun - proper - masculine singular"),
            Row(3, 4, next, parsing),
            Row(4, 3, " to [him] ", "Noun - proper - masculine singular"),
        };
        var silences = new BereanLinks.Silences();
        var english = string.Join(' ', new[] { " the LORD ", " said ", " to [him] ", next }.SelectMany(BereanWords.Rendering));

        var drafts = Pair(rows, [[Waw, 11], [12], [Lamed, 13], [14]], english,
            new Dictionary<long, string> { [Lamed] = strong }, silences);

        drafts.Select(Shape).Should().Contain("103,104>14").And.NotContain(shape => shape.Contains("bracketed"));
        silences.PrepositionsMoved.Should().Be(0);
    }

    private static string Shape(BereanLinks.Draft draft) =>
        $"{string.Join(',', draft.From)}>{string.Join(',', draft.To)}{(draft.Bracketed ? " bracketed" : string.Empty)}"
        + (draft.Relation == LinkRelation.Renders ? string.Empty : " omits");

    private static List<BereanLinks.Draft> Pair(
        BereanRow[] rows,
        List<List<long>> witness,
        string english,
        Dictionary<long, string> prepositions,
        BereanLinks.Silences silences)
    {
        var ours = english.Split(' ').Select((word, at) => new BereanLinks.Word(100 + at, 1, word, null)).ToList();
        var drafts = new List<BereanLinks.Draft>();

        BereanLinks.Pair(rows, ours, witness, drafts, silences, conjunctions: new HashSet<long> { Waw }, prepositions: prepositions)
            .Should().BeTrue();
        return drafts;
    }

    private static BereanRow Row(double order, int english, string rendering, string parsing) =>
        new(order, english, 1, "Hebrew", "א", "א", string.Empty, "2 Samuel 23:21", rendering, parsing);
}
