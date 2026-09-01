using Essenthos.Core.Loading.Links;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// The scorer, which is what turns "universal but not exact" from a hope into a number. Every
/// property here is one a reader of a published alignment figure would assume.
/// </summary>
public class AlignmentScoreTests
{
    private static readonly (long, long)[] Gold = [(1, 10), (2, 20), (3, 30), (4, 40)];

    [Fact]
    public void ReproducingTheSourceExactlyScoresZeroError()
    {
        var score = Alignment.Score(Gold, Gold);

        score.Precision.Should().Be(1);
        score.Recall.Should().Be(1);
        score.AlignmentErrorRate.Should().Be(0);
    }

    [Fact]
    public void ProposingNothingScoresTheWorst()
    {
        var score = Alignment.Score([], Gold);

        score.AlignmentErrorRate.Should().Be(1);
        score.Hit.Should().Be(0);
    }

    /// <summary>
    /// A method that proposes everything finds every stated pair and is still useless. Recall alone
    /// cannot say that; the error rate can.
    /// </summary>
    [Fact]
    public void ProposingEverythingHasPerfectRecallAndIsStillPoor()
    {
        var everything = Enumerable.Range(1, 4)
            .SelectMany(from => Enumerable.Range(1, 40).Select(to => ((long)from, (long)to)))
            .ToList();

        var score = Alignment.Score(everything, Gold);

        score.Recall.Should().Be(1);
        score.Precision.Should().BeLessThan(0.05);
        score.AlignmentErrorRate.Should().BeGreaterThan(0.9);
    }

    [Fact]
    public void HalfRightIsReportedAsHalfRight()
    {
        var score = Alignment.Score([(1, 10), (2, 20), (5, 50), (6, 60)], Gold);

        score.Hit.Should().Be(2);
        score.Precision.Should().Be(0.5);
        score.Recall.Should().Be(0.5);
        score.AlignmentErrorRate.Should().Be(0.5);
    }

    /// <summary>
    /// A pair proposed twice is one claim. Counting it twice would let a method inflate its recall
    /// by repeating itself.
    /// </summary>
    [Fact]
    public void ARepeatedProposalCountsOnce()
    {
        var score = Alignment.Score([(1, 10), (1, 10), (2, 20)], Gold);

        score.Proposed.Should().Be(2);
        score.Hit.Should().Be(2);
        score.Precision.Should().Be(1);
    }
}
