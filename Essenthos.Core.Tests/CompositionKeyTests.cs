using Essenthos.Core.Loading.Links;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// What a composition is measured against: a statement about a written word covers the prefixes
/// the corpus divides out of it, and nothing written apart from it.
/// </summary>
public class CompositionKeyTests
{
    /// <summary>
    /// Written words as BHSA divides them: וַ|יֹּאמֶר, a word written alone, one with two prefixes
    /// such as וּ|בָ|אָרֶץ at the end of the verse, and the next verse opening with וַ|יַּרְא.
    /// </summary>
    private static readonly (long Id, int Verse, string Trailer)[] Words =
    [
        (1, 3, ""), (2, 3, " "), (3, 3, " "), (4, 3, ""), (5, 3, ""), (6, 3, "׃ "),
        (7, 4, ""), (8, 4, " "),
    ];

    [Fact]
    public void AStatementOnAWrittenWordCoversItsPrefixesAndNotTheHost()
    {
        var together = CompositionPipeline.WrittenTogether(Words);

        together[2].Should().BeEquivalentTo([1L]);
        together[1].Should().BeEquivalentTo([1L]);
        together[6].Should().BeEquivalentTo([4L, 5L]);
        together[8].Should().BeEquivalentTo([7L]);
    }

    [Fact]
    public void AWordWrittenAloneHasNothingJoinedToIt()
    {
        var together = CompositionPipeline.WrittenTogether(Words);

        together.Should().NotContainKey(3);
    }

    [Fact]
    public void AVerseEndsAWrittenWordEvenWithoutAMarkAfterIt()
    {
        var together = CompositionPipeline.WrittenTogether([(1, 1, ""), (2, 2, " ")]);

        together.Should().BeEmpty();
    }
}
