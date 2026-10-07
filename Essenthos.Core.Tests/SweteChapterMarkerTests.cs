using Essenthos.Core.Corpus;
using Essenthos.Core.Loading;
using Essenthos.Core.Swete;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Essenthos.Core.Tests;

public class SweteChapterNumberRuleTests
{
    private static List<string> Surfaces(SweteBook book, int chapter, int verse) =>
        [.. book.Chapters.Single(c => c.Number == chapter).Verses.Single(v => v.Number == verse).Words.Select(w => w.Surface)];

    [Fact]
    public void ANumeralClosingAChapterOrOpeningTheNextIsTakenOutAndOneRunIntoAWordLeavesTheWord()
    {
        string[] lines =
        [
            "2.19.24 καὶ", "2.19.25 εἶπεν", "2.19.25 αὐτοῖς.", "2.19.25 XX",
            "2.20.1 XX",
            "2.20.2 Ἐγώ", "2.20.2 εἰμι", "2.20.26 τέλος.", "2.20.26 XXI",
            "2.21.1 XXIκαὶ", "2.21.1 ταῦτα",
        ];

        var book = SweteReader.Read(lines);

        Surfaces(book, 19, 25).Should().Equal("εἶπεν", "αὐτοῖς");
        Surfaces(book, 20, 1).Should().BeEmpty("the verse the edition numbers stays, with none of the words the transcription lost");
        Surfaces(book, 20, 26).Should().Equal("τέλος");
        Surfaces(book, 21, 1).Should().Equal("καὶ", "ταῦτα");
        SweteReader.ChapterMarkers(lines).Select(m => (m.Chapter, m.Verse, m.Token, m.Kept)).Should().Equal(
            ("19", "25", "XX", ""), ("20", "1", "XX", ""), ("20", "26", "XXI", ""), ("21", "1", "XXIκαὶ", "καὶ"));
    }

    [Fact]
    public void ALatinLetterThatNamesNoChapterBesideItStays()
    {
        string[] lines =
        [
            "1.10.1 Xαναὰν", "1.10.1 L", "1.10.2 κακία", "1.10.2 V", "1.10.2 οὐχ", "1.10.3 τῆς", "1.10.3 C",
            "1.11.1 XIIκαὶ", "1.11.2 καὶ", "1.11.2 Xαναάν",
        ];

        var book = SweteReader.Read(lines);

        Surfaces(book, 10, 1).Should().Equal("Xαναὰν", "L");
        Surfaces(book, 10, 2).Should().Equal("κακία", "V", "οὐχ");
        Surfaces(book, 10, 3).Should().Equal("τῆς", "C");
        Surfaces(book, 11, 1).Should().Equal("XIIκαὶ");
        Surfaces(book, 11, 2).Should().Equal("καὶ", "Xαναάν");
        SweteReader.ChapterMarkers(lines).Should().BeEmpty();
    }

    [Fact]
    public void ReadingWithTheNumbersKeptIsTheTranscriptionAsItWas()
    {
        string[] lines = ["2.19.25 αὐτοῖς.", "2.19.25 XX", "2.20.1 XX", "2.20.2 Ἐγώ"];

        var book = SweteReader.Read(lines, keepChapterMarkers: true);

        Surfaces(book, 19, 25).Should().Equal("αὐτοῖς", "XX");
        Surfaces(book, 20, 1).Should().Equal("XX");
    }

    [Fact]
    public void TheEditsBetweenTheTwoReadingsAreTheNumbersAlone()
    {
        WordDraft[] before = [new("Ἰσραήλ", ". "), new("XX", " ")];
        SweteChapterMarkerEdits.Between(before, [new("Ἰσραήλ", ". ")]).Should().Equal(new SweteChapterMarkerEdits.Edit(1, null));
        SweteChapterMarkerEdits.Between([new("XVαβὰθ", " "), new("βασιλεύει", " ")], [new("αβὰθ", " "), new("βασιλεύει", " ")])
            .Should().Equal(new SweteChapterMarkerEdits.Edit(0, "αβὰθ"));
        var other = () => SweteChapterMarkerEdits.Between(before, [new("Ἰσραήλ", " ")]);
        other.Should().Throw<InvalidOperationException>();
    }
}

[Trait(TestCategory.Name, TestCategory.Corpus)]
public class SweteChapterMarkerTests(ITestOutputHelper output)
{
    [Fact]
    public void SecondSamuelChapterTwentyMarkerIsNotTheLastWordOfNineteen()
    {
        const string file = "12.Regnorum_II";
        var lines = SweteDivisions.Lines(file, File.ReadLines(Path.Combine(TestResources.SweteFolder, file + ".txt")));
        var book = SweteReader.Read(SweteRestorations.Apply(file, lines));
        var chapter = book.Chapters.Single(c => c.Number == 19);
        chapter.Verses.Single(v => v.Number == 42).Words.Should().HaveCount(37);
        var last = chapter.Verses.Single(v => v.Number == 43);
        last.Words.Should().HaveCount(59);
        last.Words[^1].Surface.Should().Be("Ἰσραήλ");
        last.Words[^1].Trailer.Should().Be(". ");
        book.Chapters.Single(c => c.Number == 20).Verses.Single(v => v.Number == 1)
            .Words.Select(w => w.Surface).Should().Contain("υἱὸς").And.NotContain("XXυἱὸς");
    }

    /// <summary>
    /// The whole edition read with the chapter numbers and without them: every word that differs is a
    /// Roman numeral naming the chapter it closes or opens, or a word with one run into its front, and
    /// every other word stands where it stood with the same letters and marks.
    /// </summary>
    [Fact]
    public void EveryChapterNumberTheTranscriptionLetInIsTakenOutAndNoWordWithIt()
    {
        var marked = SweteTextSource.Read(TestResources.SweteFolder, chapterMarkers: false);
        var read = SweteTextSource.Read(TestResources.SweteFolder);
        var removed = new List<string>();
        var kept = new List<string>();

        foreach (var book in marked.Books)
        foreach (var chapter in book.Chapters)
        foreach (var verse in chapter.Verses)
        {
            var after = read.Books.Single(b => b.CanonicalOrdinal == book.CanonicalOrdinal)
                .Chapters.Single(c => c.Number == chapter.Number)
                .Verses.Single(v => v.Number == verse.Number && v.Label == verse.Label).Words;
            foreach (var edit in SweteChapterMarkerEdits.Between(verse.Words, after))
            {
                var token = verse.Words[edit.Index].Surface;
                var figures = token.TakeWhile(c => "IVXLC".Contains(c)).Count();
                var value = SweteReader.Roman(token[..figures]);
                value.Should().NotBeNull(token);
                var last = verse == chapter.Verses[^1];
                var first = verse == chapter.Verses[0];
                (first && value == chapter.Number || last && value == chapter.Number + 1)
                    .Should().BeTrue($"{token} at {book.CanonicalOrdinal} {chapter.Number}:{verse.Number} names a chapter beside it");
                var address = $"{BookReferences.Name(book.CanonicalOrdinal)} {chapter.Number}:{verse.Number}{verse.Label}";
                if (edit.Kept is null)
                {
                    removed.Add($"{address} {token}");
                }
                else
                {
                    edit.Kept.Should().Be(token[figures..]);
                    kept.Add($"{address} {token} → {edit.Kept}");
                }
            }
        }

        foreach (var line in removed.Concat(kept))
        {
            output.WriteLine(line);
        }

        removed.Should().HaveCount(59);
        kept.Should().Equal("Judges 7:1 VIIἸαρβάλ → Ἰαρβάλ", "1 Samuel 9:1 IXἈρὲδ → Ἀρὲδ", "1 Kings 15:1 XVαβὰθ → αβὰθ");
        removed.Should().Contain(["2 Samuel 11:27 XII", "2 Samuel 15:37 XVI", "2 Samuel 16:23 XVII", "Exodus 19:25 XX",
            "Numbers 16:50 XVII", "Numbers 18:32 XIX", "1 Kings 14:1 XIV", "1 Kings 16:1 XVI"]);
        removed.Count(line => line == "1 Kings 16:1 XVI").Should().Be(2, "the number closing 15:34 goes with the words it closed");
    }

    /// <summary>
    /// 3 Kingdoms 14:1 is empty in the edition itself: Vaticanus has no 14:1-20, and Swete prints XIV and
    /// then 21.
    /// </summary>
    [Fact]
    public void AVerseThatHeldOnlyItsChapterNumberStaysEmpty()
    {
        var read = SweteTextSource.Read(TestResources.SweteFolder);
        read.Books.Single(b => b.CanonicalOrdinal == 11).Chapters.Single(c => c.Number == 14)
            .Verses.Single(v => v.Number == 1).Words.Should().BeEmpty();
    }

    /// <summary>
    /// The chapters whose opening line the transcription lost with its number, read from the page; and
    /// 3 Kingdoms 16:1, whose line it ran into 15:34.
    /// </summary>
    [Theory]
    [InlineData(4, 17, 1, "Καὶ ἐλάλησεν Κύριος πρὸς Μωυσῆν λέγων")]
    [InlineData(4, 19, 1, "Καὶ ἐλάλησεν Κύριος πρὸς Μωυσῆν καὶ Ἀαρὼν λέγων")]
    [InlineData(11, 16, 1, "καὶ ἐγένετο λόγος Κυρίου ἐν χειρὶ Εἰοὺ υἱοῦ Ἁνανεὶ πρὸς Βααςά")]
    public void AChapterOpensWithTheLineThePagePrintsBesideItsNumber(int book, int chapter, int verse, string printed)
    {
        var read = SweteTextSource.Read(TestResources.SweteFolder);
        var chapters = read.Books.Single(b => b.CanonicalOrdinal == book).Chapters;
        string.Concat(chapters.Single(c => c.Number == chapter).Verses.Single(v => v.Number == verse).Words
                .Select(w => w.Surface + w.Trailer)).TrimEnd()
            .Should().Be(printed);
        chapters.Single(c => c.Number == chapter - 1).Verses[^1].Words[^1].Surface.Should()
            .NotStartWith("X", "the chapter before closes with its own words, not this one's number");
    }

    [Fact]
    public void ExodusTwentyOneReadsAsThePagePrintsIt()
    {
        var read = SweteTextSource.Read(TestResources.SweteFolder);
        var exodus = read.Books.Single(b => b.CanonicalOrdinal == 2);
        string.Concat(exodus.Chapters.Single(c => c.Number == 20).Verses.Single(v => v.Number == 1).Words
                .Select(w => w.Surface + w.Trailer)).TrimEnd()
            .Should().Be("Καὶ ἐλάλησεν Κύριος πάντας τοὺς λόγους τούτους λέγων");
        exodus.Chapters.Single(c => c.Number == 19).Verses[^1].Words[^1].Surface.Should().Be("αὐτοῖς");
    }
}
