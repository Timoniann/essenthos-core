using Essenthos.Core.Loading.CrossReferences;
using Essenthos.Core.Loading.Encyclopedia;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Reader;

/// <summary>
/// A cross reference or a topic's verse sends the reader to a verse, so it is read to one the English numbering has:
/// the Treasury's line broken out of the line before it goes on in that line's book where the verse's own book has no
/// such verse, its plain slips are read as they can only be meant, OpenBible's ESV-only verses are the frame's, Nave's
/// dropped book is put back, and what still names nothing is left for the loader to leave out.
/// </summary>
public sealed class ReferencesInTheFrameTests
{
    private const int Genesis = 1;
    private const int Deuteronomy = 5;
    private const int Nehemiah = 16;
    private const int Hosea = 28;
    private const int Luke = 42;
    private const int John = 43;
    private const int Acts = 44;
    private const int Philippians = 50;
    private const int Philemon = 57;
    private const int ThirdJohn = 64;

    /// <summary>Acts 7:5's "9:5; 10:11; 11:9; 34:4" follows "De 6:10,11" on a line of its own; Acts has no chapter 34.</summary>
    [Fact]
    public void ALineBrokenOutOfTheOneBeforeGoesOnInItsBookWhereTheVersesOwnBookHasNoSuchVerse()
    {
        const string entry = "he promised.<br /><scripRef>Ge 12:7; De 6:10,11</scripRef><br />" +
                             "<scripRef>9:5; 10:11; 11:9; 34:4; Ne 9:8</scripRef>";

        var (rows, unread) = TreasuryReferences.Entry(new VerseAddress(Acts, 7, 5), entry, ShippedCorrections.Treasury);

        unread.Should().Be(0);
        rows.Select(r => r.To).Should().Equal(
            new VerseAddress(Genesis, 12, 7), new VerseAddress(Deuteronomy, 6, 10), new VerseAddress(Deuteronomy, 6, 11),
            new VerseAddress(Deuteronomy, 9, 5), new VerseAddress(Deuteronomy, 10, 11), new VerseAddress(Deuteronomy, 11, 9),
            new VerseAddress(Deuteronomy, 34, 4), new VerseAddress(Nehemiah, 9, 8));
    }

    /// <summary>
    /// Genesis 12:15's "40:2; 41:1" after a line ending in Hosea is Pharaoh's officers in Genesis, which has those verses:
    /// a line that reads in the verse's own book stays there.
    /// </summary>
    [Fact]
    public void ALineThatReadsInTheVersesOwnBookStaysThere()
    {
        const string entry = "the princes.<br /><scripRef>Ho 1:1</scripRef><br /><scripRef>40:2; 41:1</scripRef>";

        var (rows, _) = TreasuryReferences.Entry(new VerseAddress(Genesis, 12, 15), entry, ShippedCorrections.Treasury);

        rows.Select(r => r.To).Should().Equal(
            new VerseAddress(Hosea, 1, 1), new VerseAddress(Genesis, 40, 2), new VerseAddress(Genesis, 41, 1));
    }

    [Fact]
    public void APlainSlipIsReadAsTheVersesItCanOnlyMean()
    {
        var (rows, _) = TreasuryReferences.Entry(new VerseAddress(Genesis, 49, 8),
            "thy father's.<br /><scripRef>Isa 9:7; Phm 2:10,11; Heb 7:14</scripRef>", ShippedCorrections.Treasury);

        rows.Select(r => r.To).Should().Contain(new VerseAddress(Philippians, 2, 10))
            .And.Contain(new VerseAddress(Philippians, 2, 11))
            .And.NotContain(r => r.Book == Philemon);
    }

    /// <summary>Deuteronomy 23:15's "Phm 10:19" may be Philemon 10-19 or 10 and 19; it is read as written and left out.</summary>
    [Fact]
    public void AReferenceNoBookHasIsReadAsWrittenAndTheLoaderLeavesItOut()
    {
        var (rows, _) = TreasuryReferences.Entry(new VerseAddress(Deuteronomy, 23, 15),
            "<scripRef>1Sa 30:15; Ob 1:14; Phm 10:19</scripRef>", ShippedCorrections.Treasury);

        var philemon = rows.Single(r => r.To.Book == Philemon);
        philemon.To.Should().Be(new VerseAddress(Philemon, 10, 19));
        EnglishVerses.Has(philemon).Should().BeFalse();
        rows.Where(r => r != philemon).Should().OnlyContain(r => EnglishVerses.Has(r));
    }

    [Fact]
    public void OpenBiblesEsvOnlyVerseIsTheFramesVerse()
    {
        var single = OpenBibleCrossReferences.Parse("3John.1.15\tJohn.10.3\t1")!;
        var run = OpenBibleCrossReferences.Parse("2John.1.1\t2John.1.13-3John.1.15\t6")!;

        single.From.Should().Be(new VerseAddress(ThirdJohn, 1, 14));
        run.End.Should().Be(new VerseAddress(ThirdJohn, 1, 14));
        EnglishVerses.Has(single).Should().BeTrue();
        EnglishVerses.Has(new VerseAddress(ThirdJohn, 1, 15)).Should().BeFalse();
    }

    /// <summary>MIRACLES' "MRK 2:9-12; 18:42,43": Mark has sixteen chapters, Luke 18 ends at 43 with the people praising God.</summary>
    [Fact]
    public void NavesDroppedBookIsPutBack()
    {
        const string entry = "DESIGN OF MRK 2:9-12; 18:42,43; JHN 2:11";

        var citations = NaveEntries.Read(NaveCorrections.Apply("MIRACLES", entry)).Citations;

        citations.Select(c => (c.Book, c.Chapter, c.FirstVerse)).Should().Contain((Luke, 18, 42)).And.Contain((Luke, 18, 43))
            .And.Contain((John, 2, 11));
        citations.Should().OnlyContain(c => EnglishVerses.HasChapter(c.Book, c.Chapter));
    }
}
