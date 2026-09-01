using Essenthos.Core.Loading.Links;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// The three ways of answering "this word was offered more than one counterpart". Genesis 1:2 is
/// the case that raised it: <em>над</em> renders עַל at 0.87 and is also offered מְרַחֶפֶת at 0.43,
/// which <em>носился</em> renders — so the reader lights both Russian words together and they read
/// as one phrase.
/// </summary>
public class SelectionTests
{
    /// <summary>над against עַל and מְרַחֶפֶת, and носился against מְרַחֶפֶת.</summary>
    private static readonly List<(int Source, int Target, double Confidence, double Position)> Hovering =
    [
        (14, 16, 0.68, 0.2),
        (15, 16, 0.43, 0.2),
        (15, 17, 0.87, 0.2),
    ];

    [Fact]
    public void KeepingEverythingIsWhatLetsTwoWordsShareAThird()
    {
        var kept = Selections.Apply(Selection.All, Hovering);

        kept.Should().HaveCount(3);
        kept.Where(pair => pair.Target == 16).Should().HaveCount(2);
    }

    [Fact]
    public void TheBestPerSourceRuleDropsTheRunnerUpAndKeepsTheAnswer()
    {
        var kept = Selections.Apply(Selection.BestPerSource, Hovering);

        kept.Should().BeEquivalentTo(new[] { (14, 16, 0.68, 0.2), (15, 17, 0.87, 0.2) });
    }

    /// <summary>
    /// A source word may genuinely render two words — Russian <em>отделяет</em> renders מַבְדִּיל and
    /// בֵּין at the same 0.57 — and a model with no preference between them is reporting that, not
    /// hesitating between alternatives. Both are kept.
    /// </summary>
    [Fact]
    public void ARealOneToManyIsKeptWhenTheModelHasNoPreference()
    {
        List<(int, int, double, double)> divides = [(11, 12, 0.57, 0.1), (11, 13, 0.57, 0.1)];

        Selections.Apply(Selection.BestPerSource, divides).Should().HaveCount(2);
    }

    /// <summary>But a second answer the model likes less is a runner-up, and goes.</summary>
    [Fact]
    public void ASecondAnswerTheModelLikesLessIsDropped()
    {
        List<(int, int, double, double)> offered = [(11, 12, 0.57, 0.1), (11, 13, 0.56, 0.1)];

        Selections.Apply(Selection.BestPerSource, offered).Should().ContainSingle();
    }

    /// <summary>
    /// The case a reader found. Matthew 1:4 writes Ἀμιναδάβ twice and the model scores the Russian
    /// name 1.000 against both — which says it cannot choose, not that the word renders both. Kept
    /// as a tie, the reader lights every occurrence when one is touched.
    /// </summary>
    [Fact]
    public void ATieBetweenTwoWritingsOfOneWordIsAmbiguityRatherThanOneToMany()
    {
        List<(int, int, double, double)> twice = [(2, 4, 1.0, 0.1), (2, 5, 1.0, 0.1)];

        Selections.Apply(Selection.BestPerSource, twice, ["a", "b", "c", "d", "Ἀμιναδάβ", "Ἀμιναδάβ"])
            .Should().ContainSingle();
    }

    /// <summary>And a tie between two different words is still the one-to-many it always was.</summary>
    [Fact]
    public void ATieBetweenTwoDifferentWordsIsStillKept()
    {
        List<(int, int, double, double)> divides = [(2, 4, 1.0, 0.1), (2, 5, 1.0, 0.1)];

        Selections.Apply(Selection.BestPerSource, divides, ["a", "b", "c", "d", "מַבְדִּיל", "בֵּין"])
            .Should().HaveCount(2);
    }

    /// <summary>
    /// The case a reader found, in full. Ναασσών stands twice in Matthew 1:4 and twice in the
    /// Synodal, and the model scores every combination 1.000 — correctly, because they are the same
    /// word. Left alone both source words land on the first occurrence and light together.
    /// </summary>
    [Fact]
    public void TwoWritingsOfOneWordTakeTheTwoOccurrencesInOrder()
    {
        List<(int, int, double, double)> both =
            [(5, 9, 1.0, 0.1), (5, 10, 1.0, 0.1), (6, 9, 1.0, 0.1), (6, 10, 1.0, 0.1)];
        var greek = new string[11];
        Array.Fill(greek, "x");
        greek[9] = greek[10] = "Ναασσών";

        var kept = Selections.Apply(Selection.BestPerSource, both, greek);

        kept.Should().BeEquivalentTo(new[] { (5, 9, 1.0, 0.1), (6, 10, 1.0, 0.1) });
    }

    /// <summary>
    /// Where the counts do not match there is nothing to pair off, and the extra source word keeps
    /// whatever it had rather than being given a target that is already spoken for.
    /// </summary>
    [Fact]
    public void ThreeWordsOntoTwoOccurrencesLeavesTheThirdAsItWas()
    {
        List<(int, int, double, double)> three =
            [(1, 9, 1.0, 0.1), (2, 9, 1.0, 0.1), (3, 9, 1.0, 0.1), (2, 10, 1.0, 0.1)];
        var greek = new string[11];
        Array.Fill(greek, "x");
        greek[9] = greek[10] = "де";

        var kept = Selections.Apply(Selection.BestPerSource, three, greek);

        kept.Select(p => p.Source).Should().OnlyHaveUniqueItems();
        kept.Should().HaveCount(3);
    }

    [Fact]
    public void CompetitiveLinkingLeavesEveryWordInAtMostOnePair()
    {
        var kept = Selections.Apply(Selection.Competitive, Hovering);

        kept.Select(pair => pair.Source).Should().OnlyHaveUniqueItems();
        kept.Select(pair => pair.Target).Should().OnlyHaveUniqueItems();
        kept.Should().BeEquivalentTo(new[] { (15, 17, 0.87, 0.2), (14, 16, 0.68, 0.2) });
    }

    /// <summary>
    /// A pair that loses does not spend its words. When the strongest pair takes a target, the
    /// source it did not use must still be free for its own best remaining pair.
    /// </summary>
    [Fact]
    public void ALosingPairDoesNotUseUpTheWordItFailedToClaim()
    {
        List<(int, int, double, double)> pairs = [(1, 10, 0.9, 0), (2, 10, 0.8, 0), (2, 11, 0.7, 0)];

        Selections.Apply(Selection.Competitive, pairs).Should()
            .BeEquivalentTo(new[] { (1, 10, 0.9, 0.0), (2, 11, 0.7, 0.0) });
    }

    [Fact]
    public void AVerseWithNothingProposedComesBackEmpty() =>
        Selections.Apply(Selection.Competitive, []).Should().BeEmpty();
}
