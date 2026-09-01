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
