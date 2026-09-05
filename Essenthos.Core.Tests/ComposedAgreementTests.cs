using Essenthos.Core.Loading.Links;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// What composition does when it arrives at a word pair a source has already stated.
///
/// <para>
/// It used to write a second link, and the corpus then held two rows saying the same thing about
/// the same two words — 699 of them between the Ukrainian and BHSA, every one a pair the Ukrainian
/// interlinear annotates and the aligner independently reached. Two rows for one correspondence
/// make a word pair nobody doubts read as contended, and they double-count it in every measure that
/// counts links.
/// </para>
///
/// <para>
/// The answer is the claim table: one link, two voices. The stated row survives because a guess can
/// be rebuilt by running the aligner again and a statement cannot be rebuilt at all.
/// </para>
/// </summary>
public class ComposedAgreementTests
{
    private static readonly Dictionary<(long From, long To), long> Stated = new()
    {
        [(11, 21)] = 500,
    };

    [Fact]
    public void APairNothingStatesBecomesALinkOfItsOwn()
    {
        var (fresh, agreeing) = CompositionPipeline.Split(
            [new RoutedLink(12, 22, 0.7, Route.Reduced)], Stated, "kjv");

        fresh.Should().ContainSingle().Which.From.Should().Be(12);
        agreeing.Should().BeEmpty();
    }

    [Fact]
    public void APairASourceStatesBecomesAClaimOnTheLinkThatStatesIt()
    {
        var (fresh, agreeing) = CompositionPipeline.Split(
            [new RoutedLink(11, 21, 0.7, Route.Reduced)], Stated, "kjv");

        fresh.Should().BeEmpty();
        agreeing.Should().ContainSingle();
        agreeing[0].Link.Should().Be(500);
        agreeing[0].Confidence.Should().Be(0.7);
    }

    /// <summary>
    /// The claim carries which readings found the pair, because that is the one thing it says that
    /// the link it stands on does not say for itself.
    /// </summary>
    [Fact]
    public void TheClaimSaysWhichReadingsReachedThePair()
    {
        var (_, agreeing) = CompositionPipeline.Split(
            [new RoutedLink(11, 21, 0.9, Route.Written | Route.Composed)], Stated, "kjv");

        agreeing[0].Source.Should().Be(Routes.Describe(Route.Written | Route.Composed, "kjv"));
        agreeing[0].Source.Should().Contain("through kjv");
    }

    /// <summary>
    /// The direction matters. A pair is one word of the source against one word of the target, and
    /// the same two ids read the other way round are a different pair.
    /// </summary>
    [Fact]
    public void ThePairIsReadInTheDirectionItWasWritten()
    {
        var (fresh, agreeing) = CompositionPipeline.Split(
            [new RoutedLink(21, 11, 0.7, Route.Reduced)], Stated, "kjv");

        fresh.Should().ContainSingle();
        agreeing.Should().BeEmpty();
    }
}
