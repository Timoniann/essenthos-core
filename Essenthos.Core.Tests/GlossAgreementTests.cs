using Essenthos.Core.Loading.Links;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// The check that makes a positional join checkable.
///
/// Counting the words of a verse cannot tell a correct join from a verse the two sources divide
/// differently, because a different division does not change how many words there are. 1 Kings
/// 22:43 is the case: BHSA ends the verse where the mapping file ends 22:44, both hold thirty-one
/// words, and every one of them is off by two.
/// </summary>
public class GlossAgreementTests
{
    [Theory]
    [InlineData("<object marker>", "[object marker]")]
    [InlineData("beginning", "beginning")]
    [InlineData("God", "god")]
    [InlineData(" in ", "in")]
    public void TheSameGlossWrittenTwoWaysIsOneGloss(string stated, string witness)
    {
        Glosses.Same(stated, witness).Should().BeTrue();
    }

    [Fact]
    public void TwoGlossesThatAreNotTheSameStillDiffer()
    {
        // The revision the file predates renamed some of these, and a check that folded them
        // together would fold away the shifts it exists to catch.
        Glosses.Same("Kittim", "Cypriot").Should().BeFalse();
    }

    [Fact]
    public void AVerseGlossedTheSameWayThroughoutAgrees()
    {
        var agreement = Glosses.Agreement(
            ["in", "beginning", "create", "[object marker]"],
            ["in", "beginning", "create", "<object marker>"]);

        agreement.Compared.Should().Be(4);
        agreement.Share.Should().Be(1);
    }

    /// <summary>
    /// A verse the two divide differently. Every word after the join is against its neighbour, and
    /// only the sequence says so — the counts are identical.
    /// </summary>
    [Fact]
    public void AVerseShiftedByOneAgreesWithAlmostNothing()
    {
        var agreement = Glosses.Agreement(
            ["and", "say", "YHWH", "to", "Moses"],
            ["say", "YHWH", "to", "Moses", "and"]);

        agreement.Share.Should().BeLessThan(0.5);
    }

    /// <summary>
    /// A witness with no glosses cannot answer the question, and answering it anyway would be a
    /// check that passes over nothing. It says how far it reached instead.
    /// </summary>
    [Fact]
    public void AWitnessThatStatesNoGlossIsNotAWitnessThatAgrees()
    {
        var agreement = Glosses.Agreement(["in", "beginning"], [null, null]);

        agreement.Compared.Should().Be(0);
        agreement.Share.Should().Be(1);
    }

    [Fact]
    public void OnlyTheWordsBothSidesGlossAreCompared()
    {
        var agreement = Glosses.Agreement(["in", "beginning", "create"], ["in", null, "make"]);

        agreement.Compared.Should().Be(2);
        agreement.Same.Should().Be(1);
    }
}
