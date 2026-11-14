using Essenthos.Core.Loading.Links;
using Essenthos.Core.Mapping;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Links;

/// <summary>
/// What composing through a middle link costs, by how many words that link names.
///
/// The number is measured rather than reasoned about, so what a test can hold is the shape of it:
/// a stated one-to-one pair costs nothing, a phrase costs something, more words cost more, and no
/// phrase is written off entirely. It was 1/n, which is what a pure n-way choice would cost and is
/// two to three times what the measurement says.
/// </summary>
public class CompositionSpecificityTests
{
    [Fact]
    public void CostsNothingToComposeThroughAStatedPair() =>
        CompositionPipeline.SpecificityOf(1).Should().Be(1);

    [Fact]
    public void CostsMoreTheMoreWordsTheMiddleLinkNames()
    {
        var costs = new long[] { 1, 2, 3, 4, 5 }.Select(n => CompositionPipeline.SpecificityOf(n)).ToList();
        costs.Should().BeInDescendingOrder();
    }

    /// <summary>
    /// The case the old number lost. The King James states that *Let there be* renders one Hebrew
    /// word; the Russian **будет** aligns to *be* at 0.55, and the composition floor is 0.25. A
    /// third of 0.55 is under it and the word reached nothing.
    /// </summary>
    [Fact]
    public void LeavesAThreeWordPhraseAboveTheFloor()
    {
        var composed = 0.553 * CompositionPipeline.SpecificityOf(3);
        composed.Should().BeGreaterThan(AlignmentPipeline.DefaultMinimumConfidence);
    }

    /// <summary>
    /// Beyond what was measured the last measured value stands, because inventing a curve for
    /// counts we have thirteen examples of would be asserting what nothing states.
    /// </summary>
    [Fact]
    public void HoldsTheLastMeasuredValueBeyondTheMeasurement()
    {
        CompositionPipeline.SpecificityOf(9).Should().Be(CompositionPipeline.SpecificityOf(5));
        CompositionPipeline.SpecificityOf(9).Should().BeGreaterThan(0);
    }

    private static readonly Dictionary<long, string> Strong = new() { [1] = "G3588", [2] = "G4151", [3] = "G1325" };

    /// <summary>
    /// The King James's <em>the Spirit</em> is one link over τοῦ Πνεύματος. Carried to both, <em>Spirit</em> reached the
    /// article as surely as the noun, and a text composed through it took the article for <em>Spirit</em> as often as not.
    /// </summary>
    [Theory]
    [InlineData("G4151", new long[] { 2 })]
    [InlineData("G3588", new long[] { 1 })]
    [InlineData(null, new long[] { 2 })]
    [InlineData("G9999", new long[] { 2 })]
    public void AMiddleWordOfAPhraseReachesTheTargetItsOwnNumberNames(string? own, long[] reached) =>
        MappingLinks.Targets(own, [1, 2], t => Strong[t]).Should().Equal(reached);

    /// <summary><em>he</em> of <em>he gave</em> has no number of its own and still reaches ἔδωκεν, which the phrase renders.</summary>
    [Fact]
    public void AMiddleWordWithoutANumberReachesWhatItsPhraseRenders() =>
        MappingLinks.Targets(null, [3], t => Strong[t]).Should().Equal(3);

    [Fact]
    public void AMiddleWordOverArticlesAloneReachesThemAll() =>
        MappingLinks.Targets(null, [1, 1], t => Strong[t]).Should().HaveCount(2);
}
