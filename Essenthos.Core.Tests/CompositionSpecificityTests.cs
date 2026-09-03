using Essenthos.Core.Loading.Links;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// What composing through a middle link costs, by how many words that link names.
///
/// The number is measured rather than reasoned about, so what a test can hold is the shape of it:
/// a stated one-to-one pair costs nothing, a phrase costs something, more words cost more, and no
/// phrase is written off entirely. It was 1/n, which is what a pure n-way choice would cost and is
/// two to three times what the measurement says. PRB-0076.
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
}
