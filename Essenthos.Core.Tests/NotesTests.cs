using Essenthos.Core.Accounts;
using Essenthos.Core.Endpoints;
using Essenthos.Core.Publishing;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// A reader's note: what counts as one, what counts as a passage, and the check that stops a release
/// from taking the verse out from under one.
/// </summary>
public sealed class NotesTests
{
    [Fact]
    public void ANoteIsWhatWasWrittenWithItsEndsTrimmed()
    {
        NoteEndpoints.Body("  In the beginning\r\nwas the Word  ").Should().Be("In the beginning\nwas the Word");
        NoteEndpoints.Body("   \n  ").Should().BeNull();
        NoteEndpoints.Body(null).Should().BeNull();
        NoteEndpoints.Body(new string('a', Limits.NoteBody)).Should().HaveLength(Limits.NoteBody);
        NoteEndpoints.Body(new string('a', Limits.NoteBody + 1)).Should().BeNull();
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
        NoteEndpoints.Ordered(chapter, verse, endChapter, endVerse).Should().Be(ordered);

    [Fact]
    public void AnchorsAreReadOutOfPsqlRows()
    {
        NoteAnchors.Parse("|43|3|16\nKJV|43|8|11\n\n").Should().Equal(
            new NoteAnchors.Point("", 43, 3, 16),
            new NoteAnchors.Point("KJV", 43, 8, 11));
    }

    [Fact]
    public void TheCheckAsksForTheNamedTextOrAnyAndQuotesWhatItIsGiven()
    {
        var query = NoteAnchors.Query([new NoteAnchors.Point("", 43, 3, 16), new NoteAnchors.Point("K'JV", 1, 1, 1)]);

        query.Should().Contain("('', 43, 3, 16), ('K''JV', 1, 1, 1)");
        query.Should().Contain("a.text = '' OR lower(t.slug) = lower(a.text)");
        query.Should().Contain("NOT EXISTS");
    }
}
