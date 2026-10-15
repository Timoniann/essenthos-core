using Essenthos.Core.Loading.Links.Evidentia;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// A verse of the original the frame places inside a translation's verse is read as that verse: a psalm's title,
/// which Hebrew numbers as its first verse and an English Bible prints inside its own first verse.
/// </summary>
public class EvidentiaVerseMapTests
{
    private static readonly EvidentiaAddress Title = new(19, 3, 0) { Sequence = 10 };
    private static readonly EvidentiaAddress First = new(19, 3, 1) { Sequence = 11 };
    private static readonly EvidentiaAddress Second = new(19, 3, 2) { Sequence = 12 };

    [Fact]
    public void ATitleTheTranslationPrintsInItsFirstVerseIsReadAsThatVerse()
    {
        var source = new[] { Word(1, First, 1, [First, Title]), Word(2, Second, 1) };
        var target = new[] { Word(11, Title, 1), Word(12, Title, 2), Word(13, First, 1), Word(14, Second, 1) };

        var moved = EvidentiaVerseMap.Read(source, target);

        moved.Keys.Should().BeEquivalentTo([11L, 12L, 13L]);
        moved.Values.Select(token => (token.Address, token.Position)).Should().Equal((First, 1), (First, 2), (First, 3));
        moved[11].Printed.Should().Be((Title, 1));
        moved[13].Printed.Should().Be((First, 1));
    }

    [Fact]
    public void WithoutTheFrameJoiningThemNeighbouringVersesStayApart()
    {
        var source = new[] { Word(1, First, 1), Word(2, Second, 1) };
        var target = new[] { Word(11, Title, 1), Word(13, First, 1), Word(14, Second, 1) };

        EvidentiaVerseMap.Read(source, target).Should().BeEmpty();
    }

    [Fact]
    public void AnOriginalVerseSpanningTwoVersesOfTheTranslationIsLeftToNeither()
    {
        var source = new[] { Word(1, First, 1), Word(2, Second, 1) };
        var target = new[] { Word(13, First, 1, [First, Second]) };

        EvidentiaVerseMap.Read(source, target).Should().BeEmpty();
    }

    private static EvidentiaToken Word(long id, EvidentiaAddress address, int position, EvidentiaAddress[]? span = null) =>
        new(id, address, position, $"w{id}", "hbo") { Span = span };
}
