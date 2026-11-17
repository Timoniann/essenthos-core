using Essenthos.Core.Conversion;
using Essenthos.Core.Loading;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Witnesses;

/// <summary>
/// Charles's 1 Enoch and Jubilees read from the two modules the fetch pins: every word placed in a verse or left out
/// by a named rule, numbered as GEEZ81 is but where its transcription lacks a verse, and the module's two broken verse
/// breaks in Jubilees mended.
/// </summary>
[Trait(TestCategory.Name, TestCategory.Corpus)]
public sealed class CharlesTests
{
    private static readonly Lazy<(TextSource Source, CharlesTextSource.Tally Tally)> Reading = new(() =>
    {
        var tally = new CharlesTextSource.Tally();
        return (CharlesTextSource.Read(TestResources.Folder(CharlesTextSource.Folder), tally), tally);
    });

    private static readonly Lazy<TextSource> Geez = new(() => GeezTextSource.Read(TestResources.Folder(GeezTextSource.Folder)));

    /// <summary>
    /// The words of the title pages and introductions, the notes, titles and headings, the verse numbers, the Greek
    /// and Latin fragments with the sigla, the dates and the one stray, with the words placed, are every word read.
    /// </summary>
    [Fact]
    public void EveryWordIsPlacedOrLeftOutByANamedRule()
    {
        var (source, tally) = Reading.Value;

        (tally.Read - tally.Placed - tally.FrontMatter - tally.Notes - tally.Titles - tally.Headings - tally.Markers
         - tally.Versions - tally.Dates - tally.Strays).Should().Be(0);
        tally.Placed.Should().Be(source.Books.Sum(book => book.Chapters.Sum(chapter =>
            chapter.Verses.Sum(verse => verse.Words.Count(word => word.Surface.Length > 0)))));
        new
        {
            tally.Read, tally.Placed, tally.FrontMatter, tally.Notes, tally.Titles, tally.Headings, tally.Markers,
            tally.Versions, tally.VersionBlocks, tally.Dates, tally.DateCount, tally.Strays,
        }.Should().BeEquivalentTo(new
        {
            Read = 124_479, Placed = 87_427, FrontMatter = 8_333, Notes = 20_761, Titles = 2_731, Headings = 1_072,
            Markers = 2_388, Versions = 1_380, VersionBlocks = 38, Dates = 386, DateCount = 129, Strays = 1,
        });
    }

    [Fact]
    public void EnochIsNumberedAsGeez81ButWhereItsTranscriptionLacksAVerse()
    {
        var (only, lacking) = Against(CharlesTextSource.Enoch);

        only.Should().Equal((20, 8), (21, 1), (28, 3), (98, 16), (100, 13));
        lacking.Should().BeEmpty();
        Addresses(Reading.Value.Source, CharlesTextSource.Enoch).Should().HaveCount(1_063);
    }

    [Fact]
    public void JubileesIsNumberedAsGeez81ButWhereItsTranscriptionLacksAVerse()
    {
        var (only, lacking) = Against(CharlesTextSource.Jubilees);

        only.Should().Equal([(1, 18), (1, 19), .. Enumerable.Range(3, 12).Select(verse => (4, verse)), (26, 33)]);
        lacking.Should().BeEmpty();
        Addresses(Reading.Value.Source, CharlesTextSource.Jubilees).Should().HaveCount(1_307);
    }

    /// <summary>The module runs Charles's 1:8-9 and 11:3-4 into one verse each; the 1913 volume divides them here.</summary>
    [Theory]
    [InlineData(1, 8, "And they will eat and be satisfied", "a witness against them.")]
    [InlineData(1, 9, "For they will forget all My commandments", "and a snare.")]
    [InlineData(1, 10, "And many will perish", "that it should dwell (there).")]
    [InlineData(1, 29, "And the angel of the presence who went before the camp", "all the days of the earth.")]
    [InlineData(11, 3, "And 'Ûr, the son of Kesed, built the city", "the name of his father.")]
    [InlineData(11, 4, "And they made for themselves molten images", "transgression and uncleanness.")]
    [InlineData(11, 5, "And the prince Mastêmâ exerted himself", "to shed blood upon the earth.")]
    public void TheJubileesVerseBreaksTheModuleLostAreCharless(int chapter, int verse, string opening, string end)
    {
        var text = Verse(CharlesTextSource.Jubilees, chapter, verse);

        text.Should().StartWith(opening).And.EndWith(end);
    }

    /// <summary>Charles prints 5:6 and 5:7 in pieces a to j and a to c, out of letter order; each verse reads in letter order.</summary>
    [Fact]
    public void AVerseDividedByLettersIsJoinedInLetterOrder()
    {
        Verse(CharlesTextSource.Enoch, 5, 7).Should().Be(
            "But for the elect there shall be light and joy and peace, And they shall inherit the earth. "
            + "And for you the godless there shall be a curse.");
        Verse(CharlesTextSource.Enoch, 5, 6).Should().StartWith("In those days ye shall make your names an eternal execration")
            .And.EndWith("But on you all shall abide a curse┐.");
    }

    /// <summary>Where Charles prints the Greek or Latin beside the Ethiopic, only the Ethiopic is read, and his own marks stay.</summary>
    [Fact]
    public void OnlyTheEthiopicIsReadWhereAFragmentStandsBesideIt()
    {
        Verse(CharlesTextSource.Enoch, 22, 2).Should().Be(
            "And there was in it †four† hollow places, deep and wide and very smooth. †How† smooth are the hollow places "
            + "and deep and dark to look at.");
        Verse(CharlesTextSource.Enoch, 1, 1).Should().Contain("the elect ╓and╖ righteous");
        Verses(CharlesTextSource.Enoch).Should().NotContain(text =>
            text.Contains(")Gg", StringComparison.Ordinal) || text.Contains("Factum", StringComparison.Ordinal)
            || text.Any(letter => letter >= 'Α' && letter <= 'ω'));
    }

    [Fact]
    public void NoYearFromTheCreationIsReadAsAWordOfJubilees()
    {
        Verse(CharlesTextSource.Jubilees, 1, 1).Should().Contain("on the sixteenth day of the month, that God spake to Moses");
        Verse(CharlesTextSource.Jubilees, 13, 8).Should().Contain("the first month, that he built an altar");
        Verses(CharlesTextSource.Jubilees).Should().NotContain(text =>
            text.Contains("A.M", StringComparison.OrdinalIgnoreCase) || text.Contains("Anno Mundi", StringComparison.Ordinal)
            || text.Any(char.IsAsciiDigit) && text.Contains('[', StringComparison.Ordinal) && text.Contains("A.", StringComparison.Ordinal));
    }

    /// <summary>The passages Charles prints out of order stand at their own numbers.</summary>
    [Theory]
    [InlineData(91, 19)]
    [InlineData(93, 14)]
    [InlineData(51, 5)]
    [InlineData(60, 25)]
    [InlineData(106, 19)]
    public void AChapterPrintedOutOfOrderHasEveryVerseInOrder(int chapter, int last)
    {
        var verses = Reading.Value.Source.Books.Single(book => book.CanonicalOrdinal == CharlesTextSource.Enoch)
            .Chapters.Single(c => c.Number == chapter).Verses.Select(verse => verse.Number);

        verses.Should().StartWith(Enumerable.Range(1, last));
    }

    /// <summary>
    /// The converter takes every sign whose span is certain out of the words and keeps it as a mark; what stays printed is
    /// the scan's seven odd daggers, its two closing brackets for a misread letter and Charles's (?) after Enoch 30:2's word.
    /// </summary>
    [Fact]
    public void EverySignWhoseSpanIsCertainBecomesAMark()
    {
        var text = TextConverter.From(Reading.Value.Source);
        new CharlesSigns().Apply(text, string.Empty);

        var left = text.Verses
            .SelectMany(verse => verse.Words.Where(word => word.Trailer.Any(CharlesSigns.IsSign))
                .Select(word => $"{verse.Key.Book} {verse.Key.Chapter}:{verse.Key.Verse} {word.Surface}{word.Trailer}"))
            .ToList();
        left.Should().Equal(
            "85 30:2 colour (?) ", "85 39:1 it †", "85 39:1 children †", "85 39:1 seed† ", "85 75:5 dew† ", "85 83:11 as †",
            "85 83:11 known† ", "85 83:11 east, †", "86 23:11 away].", "86 40:2 to]");
        var words = text.Words().Select(w => w.Word).ToList();
        int Spans(Func<ConvertedWord, int?> mark) => words.Where(word => mark(word) is not null).Select(mark).Distinct().Count();
        new
        {
            Corrupt = Spans(word => word.Corrupt), NotInEthiopic = Spans(word => word.NotInEthiopic),
            NotInGreek = Spans(word => word.NotInGreek), Doubtful = Spans(word => word.Doubtful),
        }.Should().BeEquivalentTo(new { Corrupt = 60, NotInEthiopic = 67, NotInGreek = 46, Doubtful = 113 });
        text.Verses.SelectMany(verse => verse.Words).Should().NotContain(word => word.Surface.Length == 0 && word.Trailer.Length == 0);

        var nine = text.Verses.Single(verse => verse.Key is { Book: CharlesTextSource.Enoch, Chapter: 1, Verse: 9 }).Words;
        string.Concat(nine.Select(word => word.Surface + word.Trailer)).Should().Contain("And to destroy all the ungodly:");
        nine.Where(word => word.NotInEthiopic is not null).Select(word => word.Surface).Should().Equal(
            "His", "all", "of", "their", "ungodliness", "And", "of", "all", "the", "hard", "things", "which", "have", "spoken");
    }

    private static ((int Chapter, int Verse)[] Only, (int Chapter, int Verse)[] Lacking) Against(int book)
    {
        var charles = Addresses(Reading.Value.Source, book);
        var geez = Addresses(Geez.Value, book);
        return ([.. charles.Except(geez).Order()], [.. geez.Except(charles).Order()]);
    }

    private static HashSet<(int Chapter, int Verse)> Addresses(TextSource source, int book) =>
    [
        .. source.Books.Single(b => b.CanonicalOrdinal == book).Chapters
            .SelectMany(chapter => chapter.Verses.Select(verse => (chapter.Number, verse.Number))),
    ];

    private static string Verse(int book, int chapter, int verse) =>
        Text(Reading.Value.Source.Books.Single(b => b.CanonicalOrdinal == book)
            .Chapters.Single(c => c.Number == chapter).Verses.Single(v => v.Number == verse));

    private static IEnumerable<string> Verses(int book) =>
        Reading.Value.Source.Books.Single(b => b.CanonicalOrdinal == book).Chapters.SelectMany(c => c.Verses).Select(Text);

    private static string Text(VerseDraft verse) => string.Concat(verse.Words.Select(word => word.Surface + word.Trailer));
}
