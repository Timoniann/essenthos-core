using Essenthos.Core.Loading.Links;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// Recovering the function words' Hebrew, on the two verses that show every shape at once. The
/// numbers are the file's own, so a change to the rules is scored against what it actually says
/// rather than against an invented example.
/// </summary>
public class HebrewPrefixTests
{
    /// <summary>Genesis 1:1, whose eleven morphemes include both articles and both object markers.</summary>
    private static readonly HebrewEntry[] InTheBeginning =
    [
        new("H9003", "c1", 1, "in"),
        new("H7225", "c1", 2, "beginning"),
        new("H1254", "c1", 3, "create"),
        new("H430", "c1", 4, "god(s)"),
        new("H853", "c1", 5, "[object marker]"),
        new("H9009", "c1", 6, "the"),
        new("H8064", "c1", 7, "heavens"),
        new("H9000", "c1", 8, "and"),
        new("H853", "c1", 9, "[object marker]"),
        new("H9009", "c1", 10, "the"),
        new("H776", "c1", 11, "earth"),
    ];

    private static readonly EnglishSegment[] TheHeavenAndTheEarth =
    [
        Segment(InTheBeginning, 2, "In", "the", "beginning"),
        Segment(InTheBeginning, 4, "God"),
        Segment(InTheBeginning, 3, "created"),
        Segment(InTheBeginning, 5),
        Segment(InTheBeginning, 7, "the", "heaven"),
        Segment(InTheBeginning, 11, "and", "the", "earth"),
    ];

    /// <summary>Genesis 1:3, where Hebrew's conjunction sits on the verb and English's opens the clause.</summary>
    private static readonly HebrewEntry[] AndGodSaid =
    [
        new("H9000", "c5", 32, "and"),
        new("H559", "c5", 33, "say"),
        new("H430", "c5", 34, "god(s)"),
        new("H1961", "c6", 35, "be"),
        new("H216", "c6", 36, "light"),
        new("H9000", "c7", 37, "and"),
        new("H1961", "c7", 38, "be"),
        new("H216", "c7", 39, "light"),
    ];

    private static readonly EnglishSegment[] LetThereBeLight =
    [
        Segment(AndGodSaid, 34, "And", "God"),
        Segment(AndGodSaid, 33, "said"),
        Segment(AndGodSaid, 35, "Let", "there", "be"),
        Segment(AndGodSaid, 36, "light"),
        Segment(AndGodSaid, 38, "and", "there", "was"),
        Segment(AndGodSaid, 39, "light"),
    ];

    [Fact]
    public void APrefixImmediatelyBeforeTheMarkedWordIsThatPhrasePrefix()
    {
        var matched = Matched(InTheBeginning, TheHeavenAndTheEarth);

        // "In the beginning" — the preposition בְּ, and nothing for the article English supplies.
        matched.Should().Contain(0, 1);
    }

    /// <summary>
    /// The English article is not in the Hebrew of "in the beginning", and there is a הָ two words
    /// along that belongs to "the heaven". Reaching past the phrase for it would be the same defect
    /// in the other direction.
    /// </summary>
    [Fact]
    public void AnArticleTheHebrewDoesNotHaveIsGivenNothing()
    {
        var matched = Matched(InTheBeginning, TheHeavenAndTheEarth);

        matched.Should().NotContainKey(1);
    }

    /// <summary>
    /// אֵת is never rendered, so it does not end the search for a phrase's prefixes — "and the earth"
    /// reaches וְ across it, and both function words land.
    /// </summary>
    [Fact]
    public void TheSearchReadsPastTheObjectMarker()
    {
        var matched = Matched(InTheBeginning, TheHeavenAndTheEarth);

        matched.Should().Contain(7, 8).And.Contain(8, 10);
    }

    [Fact]
    public void TheArticleOfTheHeavenIsFound()
    {
        Matched(InTheBeginning, TheHeavenAndTheEarth).Should().Contain(5, 6);
    }

    /// <summary>
    /// The case that made this necessary. "And God said" is one clause over וַ־יֹּאמֶר אֱלֹהִים:
    /// walking back from <em>God</em> reaches the verb, so only the clause can say that the English
    /// conjunction and the Hebrew one are the same word.
    /// </summary>
    [Fact]
    public void AClauseInitialConjunctionIsFoundThoughItIsNotAdjacent()
    {
        var matched = Matched(AndGodSaid, LetThereBeLight);

        matched.Should().Contain(0, 32);
    }

    /// <summary>The same verse's second conjunction is adjacent, and adjacency is the stronger claim.</summary>
    [Fact]
    public void AnAdjacentConjunctionIsScoredAboveAClauseInitialOne()
    {
        var matches = HebrewPrefixes.Match(AndGodSaid, LetThereBeLight);

        matches.Single(m => m.EnglishWord == 7).Should()
            .BeEquivalentTo(new PrefixMatch(7, 37, HebrewPrefixes.Adjacent));
        matches.Single(m => m.EnglishWord == 0).Confidence.Should().Be(HebrewPrefixes.ClauseInitial);
    }

    /// <summary>One clause has one opening conjunction, however many phrases it is written in.</summary>
    [Fact]
    public void AConjunctionIsClaimedOnlyOnce()
    {
        var matches = HebrewPrefixes.Match(AndGodSaid, LetThereBeLight);

        matches.Select(m => m.HebrewPosition).Should().OnlyHaveUniqueItems();
        matches.Select(m => m.EnglishWord).Should().OnlyHaveUniqueItems();
    }

    /// <summary>
    /// A phrase must keep a word. Taking the only word of "light" would leave a link claiming a
    /// Hebrew word is rendered by nothing, which is a different statement and a false one.
    /// </summary>
    [Fact]
    public void APhraseIsNeverLeftEmpty()
    {
        HebrewEntry[] hebrew = [new("H9009", "c1", 1, "the"), new("H4325", "c1", 2, "water")];
        EnglishSegment[] english = [Segment(hebrew, 2, "the")];

        HebrewPrefixes.Match(hebrew, english).Should().BeEmpty();
    }

    /// <summary>
    /// Where the file marks the prefix itself, it is already stated and the inference must keep off
    /// it: "the" here is the English supplying an article, not a second claim on the same וְ.
    /// </summary>
    [Fact]
    public void APrefixTheFileAlreadyMarksIsNotClaimedAgain()
    {
        HebrewEntry[] hebrew =
        [
            new("H9000", "c1", 1, "and"),
            new("H7307", "c1", 2, "wind"),
        ];
        EnglishSegment[] english = [Segment(hebrew, 1, "And"), Segment(hebrew, 2, "the", "Spirit")];

        HebrewPrefixes.Match(hebrew, english).Should().BeEmpty();
    }

    /// <summary>
    /// The run stops at the first word that renders no prefix. "of the deep" opens with a genitive
    /// English writes and Hebrew does not, and past it the two orders are no longer parallel.
    /// </summary>
    [Fact]
    public void TheSearchStopsAtAWordNoPrefixRenders()
    {
        HebrewEntry[] hebrew =
        [
            new("H9009", "c1", 1, "the"),
            new("H8415", "c1", 2, "primeval ocean"),
        ];
        EnglishSegment[] english = [Segment(hebrew, 2, "of", "the", "deep")];

        HebrewPrefixes.Match(hebrew, english).Should().BeEmpty();
    }

    private static EnglishSegment Segment(IReadOnlyList<HebrewEntry> hebrew, int position, params string[] words) =>
        new(words, hebrew.Single(entry => entry.Position == position));

    private static Dictionary<int, int> Matched(
        IReadOnlyList<HebrewEntry> hebrew,
        IReadOnlyList<EnglishSegment> english) =>
        HebrewPrefixes.Match(hebrew, english).ToDictionary(m => m.EnglishWord, m => m.HebrewPosition);
}
