using Essenthos.Core.Loading.Links.Evidentia;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// The stages between the database and a candidate. Every EVIDENTIA test until now built its
/// candidates by hand, and the failures this work actually recorded happened here: the UDPipe
/// adapter returning raw tokens for weeks because it looked in the wrong folder, and a stemmer
/// collision that produced thousands of edges.
/// </summary>
public class EvidentiaAdapterTests
{
    [Fact]
    public void AWordTheModelSplitsStillLandsOnTheCorpusWordItCameFrom()
    {
        var reconciliation = UdpipeAnnotator.Reconcile(
            [Token(1, "Beer-sheba"), Token(2, "well")],
            [Parsed("Beer", "beer", "PROPN"), Parsed("sheba", "sheba", "PROPN"), Parsed("well", "well", "NOUN")]);

        reconciliation.ByCorpusIndex.Should().NotBeNull();
        reconciliation.ByCorpusIndex![0][2].Should().Be("beer", "the first part carries the analysis");
        reconciliation.ByCorpusIndex[1][3].Should().Be("NOUN");
        reconciliation.CorpusIndexByParsedWord.Should().Equal([0, 0, 1], "a head can name either part of a split word");
    }

    [Fact]
    public void AWordTheModelMergesIsRefusedAndTheDivergentTokenIsNamed()
    {
        var reconciliation = UdpipeAnnotator.Reconcile(
            [Token(1, "can"), Token(2, "not")],
            [Parsed("cannot", "cannot", "PART")]);

        reconciliation.ByCorpusIndex.Should().BeNull();
        reconciliation.Detail.Should().Contain("'can'").And.Contain("cannot");
    }

    [Fact]
    public void NothingReconciledIsNeverReportedAsAnAnalysis()
    {
        UdpipeAnnotator.Reconcile([Token(1, "beginning")], []).ByCorpusIndex.Should().BeNull(
            "an empty analysis reported as Annotated is how the adapter returned raw tokens for weeks");
    }

    [Fact]
    public void AModelTokenLeftOverIsRefusedRatherThanIgnored()
    {
        UdpipeAnnotator.Reconcile(
            [Token(1, "light")],
            [Parsed("light", "light", "NOUN"), Parsed("darkness", "darkness", "NOUN")])
            .ByCorpusIndex.Should().BeNull();
    }

    [Fact]
    public void ThePassageReachesTheModelAsSentencesCarryingTheirPunctuation()
    {
        var tokens = new[]
        {
            Verse(1, 1, "In"), Verse(1, 2, "form", ", "), Verse(1, 3, "void", ". "), Verse(1, 4, "And"),
            Verse(1, 5, "deep", ". "), Verse(2, 1, "Thus"), Verse(2, 2, "finished"),
        };

        UdpipeAnnotator.Sentences(tokens).Should().Be(
            "In form , void .\nAnd deep .\nThus finished\n");
    }

    [Fact]
    public void ASourceWithNoAdmissibleTargetIsLeftUnassignedRatherThanPairedWithARunnerUp()
    {
        // One real target column and a dummy per row, exactly as the resolver builds it. The
        // second source can only reach the target the first one takes; the property that matters
        // is that it is then given a dummy rather than the nearest thing left.
        var costs = new double[3, 4];
        costs[1, 1] = -2_000;
        costs[2, 1] = -1_000;
        costs[1, 2] = costs[1, 3] = costs[2, 2] = costs[2, 3] = 0;

        var assigned = EvidentiaKnownRenderingProposalResolver.MinimumCostAssignment(costs, sourceRows: 2, columnCount: 3);

        assigned[0].Should().Be(1, "the stronger evidence takes the target");
        assigned[1].Should().BeGreaterThan(1, "the other source gets a dummy column, not the target");
    }

    [Fact]
    public void ASourceWhoseOnlyTargetIsInadmissibleIsNotAssignedToIt()
    {
        var costs = new double[2, 3];
        costs[1, 1] = EvidentiaDefaults.InvalidAssignmentCost;
        costs[1, 2] = 0;

        var assigned = EvidentiaKnownRenderingProposalResolver.MinimumCostAssignment(costs, sourceRows: 1, columnCount: 2);

        assigned[0].Should().Be(2, "an inadmissible pairing costs more than abstaining");
    }

    private static EvidentiaToken Token(long id, string surface) =>
        new(id, new EvidentiaAddress(1, 1, 1), (int)id, surface, "eng");

    private static EvidentiaToken Verse(int verse, int position, string surface, string trailer = " ") =>
        new(verse * 100 + position, new EvidentiaAddress(1, 1, verse), position, surface, "eng", trailer);

    private static string[] Parsed(string surface, string lemma, string partOfSpeech) =>
        ["1", surface, lemma, partOfSpeech, "_", "_"];
}
