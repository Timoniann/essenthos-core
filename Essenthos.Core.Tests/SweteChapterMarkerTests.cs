using Essenthos.Core.Loading;
using Essenthos.Core.Swete;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

public class SweteChapterMarkerTests
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

    [Fact]
    public void RomanLettersAtOtherAddressesRemainPartOfTheTranscription()
    {
        var lines = new[] { "12.18.1 XX", "12.18.1 Xανάαν", "12.18.1 Mεθλὰ", "12.19.43 Ἰσραήλ.", "12.19.43 XX" };
        var corrections = SweteRestorations.All.Where(r => r.Book == "12.Regnorum_II" && r.Chapter == 19 && r.Verse == 43).ToArray();
        SweteRestorations.Apply("12.Regnorum_II", lines, corrections).Take(3).Should().Equal(lines.Take(3));
    }

    [Fact]
    public void EveryOtherSourceTokenAndAddressIsUnchanged()
    {
        var before = SweteTextSource.Read(TestResources.SweteFolder, chapterMarkers: false);
        var after = SweteTextSource.Read(TestResources.SweteFolder);
        var expected = before.Books.SelectMany(b => b.Chapters.SelectMany(c => c.Verses.SelectMany(v =>
            v.Words.Select((w, index) => (Book: b.CanonicalOrdinal, Chapter: c.Number, Verse: v.Number,
                v.Label, Position: index + 1, w.Surface, w.Trailer)))))
            .Where(w => !(w.Book == 10 && w.Chapter == 19 && w.Verse == 43 && w.Surface == "XX"));
        var actual = after.Books.SelectMany(b => b.Chapters.SelectMany(c => c.Verses.SelectMany(v =>
            v.Words.Select((w, index) => (Book: b.CanonicalOrdinal, Chapter: c.Number, Verse: v.Number,
                v.Label, Position: index + 1, w.Surface, w.Trailer)))));
        actual.Should().Equal(expected);
    }
}
