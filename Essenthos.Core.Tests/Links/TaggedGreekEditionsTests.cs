using Essenthos.Core.Products;
using Essenthos.Core.StepBible;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Links;

/// <summary>
/// TAGNT's edition flags laid on the Greek editions' words: a word line is read for its reference, Strong number and the
/// editions TAGNT names, and a word joins an edition's word only where both carry the same number in the same order.
/// </summary>
public sealed class TaggedGreekEditionsTests
{
    [Fact]
    public void AWordLineGivesItsReferenceNumberAndEditionsAndNoGreek()
    {
        var word = TaggedGreekEditions.Word(
            "Mat.1.1#03=NKO\tἸησοῦ (Iēsou)\tof Jesus\tG2424G=N-GSM-P\tἸησοῦς=Jesus/Joshua\tNA28+NA27+Tyn+SBL+WH+Treg+TR»+Byz\t\t\tde Jesús")!;

        word.Reference.Should().Be("Mat.1.1#03");
        word.Verse.Should().Be(VerseKey.Parse("40.1:1"));
        word.Number.Should().Be("G2424");
        word.Flags.Should().BeEquivalentTo(["NA28", "NA27", "Tyn", "SBL", "WH", "Treg", "TR", "Byz"]);
        TaggedGreekEditions.Word("# Mat.1.1\tΒίβλος \tγενέσεως ").Should().BeNull();
        TaggedGreekEditions.Word("Editions\tByz=Byzantine based on RP 2005").Should().BeNull();
    }

    [Fact]
    public void AWordTheTwoDisputeIsLeftUnjoinedRatherThanGivenToItsNeighbour()
    {
        // ὁ θεὸς καὶ [ὁ] πατήρ against an edition without the second article: the καί and πατήρ join past it.
        var pairs = TaggedGreekEditions.Common(["G3588", "G2316", "G2532", "G3588", "G3962"], ["G3588", "G2316", "G2532", "G3962"]);

        pairs.Should().Equal((0, 0), (1, 1), (2, 2), (4, 3));
        TaggedGreekEditions.Common(["G0976", null], ["G976", null]).Should().Equal((0, 0));
    }
}
