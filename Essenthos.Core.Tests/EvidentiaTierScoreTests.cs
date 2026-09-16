using Essenthos.Core.Loading.Links;
using Essenthos.Core.Loading.Links.Evidentia;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

public class EvidentiaTierScoreTests
{
    private static readonly HashSet<(long From, long To)> Gold = [(1, 11), (2, 12), (2, 13)];
    private static readonly HashSet<long> Covered = [1, 2];

    [Fact]
    public void AProposalOnAWordTheGoldNeverReachesIsAMistakeOnlyUnderTheOldRule()
    {
        var score = EvidentiaTierScore.Of([Proposal(1, 11), Proposal(3, 14)], Gold, Covered);

        score.Precision.Should().Be(0.5, "every figure published before the change counted it as wrong");
        score.CoveredPrecision.Should().Be(1, "the gold says nothing about word 3");
        score.Unscored.Should().Be(1);
    }

    [Fact]
    public void AProposalTheGoldContradictsOnACoveredWordIsWrongUnderBothRules()
    {
        var score = EvidentiaTierScore.Of([Proposal(1, 11), Proposal(2, 14)], Gold, Covered);

        score.Precision.Should().Be(0.5);
        score.CoveredPrecision.Should().Be(0.5);
        score.Unscored.Should().Be(0);
    }

    [Fact]
    public void TiersAddAsCountsSoAnAggregateIsNotAnAverageOfPercentages()
    {
        var total = EvidentiaTierScore.Total(
        [
            new EvidentiaTierScore(Proposals: 10, Correct: 9, OnCoveredWords: 10),
            new EvidentiaTierScore(Proposals: 100, Correct: 50, OnCoveredWords: 60),
        ]);

        total.Should().Be(new EvidentiaTierScore(110, 59, 70));
        total.CoveredPrecision.Should().BeApproximately(59d / 70, 1e-12);
        total.Recall(200).Should().BeApproximately(59d / 200, 1e-12);
    }

    private static EvidentiaProposal Proposal(long from, long to) => new(
        Analysis(from, "eng"),
        Analysis(to, "hbo"),
        EvidentiaProposalKind.ReviewKnownRendering,
        0.5);

    private static EvidentiaAnalysis Analysis(long id, string language) => new(
        new EvidentiaToken(id, new EvidentiaAddress(1, 1, 1), (int)id, $"w{id}", language),
        $"w{id}",
        null,
        null,
        EvidentiaWordClass.Content,
        default);
}
