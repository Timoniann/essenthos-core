using Essenthos.Core.Accounts;
using Essenthos.Core.Endpoints;
using Essenthos.Core.Publishing;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// A reader's bookmark: what colour and comment it can have, what counts as a passage, and the check
/// that stops a release from taking the verse out from under one.
/// </summary>
public sealed class BookmarksTests
{
    [Fact]
    public void ACommentIsWhatWasWrittenWithItsEndsTrimmedAndNothingIsNoComment()
    {
        BookmarkEndpoints.Comment("  In the beginning\r\nwas the Word  ", out var written).Should().BeTrue();
        written.Should().Be("In the beginning\nwas the Word");

        BookmarkEndpoints.Comment("   \n  ", out var blank).Should().BeTrue();
        blank.Should().BeNull();
        BookmarkEndpoints.Comment(null, out var none).Should().BeTrue();
        none.Should().BeNull();

        BookmarkEndpoints.Comment(new string('a', Limits.BookmarkComment), out _).Should().BeTrue();
        BookmarkEndpoints.Comment(new string('a', Limits.BookmarkComment + 1), out _).Should().BeFalse();
    }

    [Fact]
    public void AColourIsOneOfThePaletteAndDefaultsToTheFirst()
    {
        BookmarkEndpoints.Color(null).Should().Be("amber");
        BookmarkEndpoints.Color("rose").Should().Be("rose");
        BookmarkEndpoints.Color("#ff0000").Should().BeNull();
        BookmarkEndpoints.Color("Rose").Should().BeNull();
    }

    [Theory]
    [InlineData(3, 16, 3, 16, true)]
    [InlineData(3, 16, 3, 21, true)]
    // John 7:53-8:11 is one passage.
    [InlineData(7, 53, 8, 11, true)]
    [InlineData(3, 16, 3, 15, false)]
    [InlineData(8, 1, 7, 53, false)]
    [InlineData(0, 1, 1, 1, false)]
    [InlineData(1, 0, 1, 1, false)]
    public void APassageEndsWhereItStartsOrLater(int chapter, int verse, int endChapter, int endVerse, bool ordered) =>
        BookmarkEndpoints.Ordered(chapter, verse, endChapter, endVerse).Should().Be(ordered);

    [Fact]
    public void AnchorsAreReadOutOfPsqlRows()
    {
        BookmarkAnchors.Parse("|43|3|16\nKJV|43|8|11\n\n").Should().Equal(
            new BookmarkAnchors.Point("", 43, 3, 16),
            new BookmarkAnchors.Point("KJV", 43, 8, 11));
    }

    [Fact]
    public void TheCheckAsksForTheNamedTextOrAnyAndQuotesWhatItIsGiven()
    {
        var query = BookmarkAnchors.Query([new BookmarkAnchors.Point("", 43, 3, 16), new BookmarkAnchors.Point("K'JV", 1, 1, 1)]);

        query.Should().Contain("('', 43, 3, 16), ('K''JV', 1, 1, 1)");
        query.Should().Contain("a.text = '' OR lower(t.slug) = lower(a.text)");
        query.Should().Contain("NOT EXISTS");
    }
}
