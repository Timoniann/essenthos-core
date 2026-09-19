using Essenthos.Core.Berean;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Usfm;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// Where an edition starts a paragraph or a line, as the readers find it. The mark stands before the
/// text it opens, often on a line of its own and often before the verse number, so what is tested is
/// that it lands on the first word after it and nowhere else.
/// </summary>
public class ParagraphMarkTests
{
    [Fact]
    public void AParagraphMarkBeforeAVerseOpensItsFirstWord()
    {
        var verses = UsfmReader.Read(
                """
                \id GEN
                \c 1
                \p
                \v 1 In the beginning
                \v 2 The earth was formless.
                \p
                \v 3 God said
                """)
            .Chapters[0]!.Verses;

        verses.Select(verse => verse.Words[0]!.Break)
            .Should().Equal(TextBreak.Paragraph, null, TextBreak.Paragraph);
        verses.SelectMany(verse => verse.Words.Skip(1)).Should().OnlyContain(word => word.Break == null);
    }

    /// <summary>
    /// A psalm is a line of poetry a marker, and a blank line between stanzas is a paragraph as a
    /// reader sees one — including when a line marker follows it before the words do.
    /// </summary>
    [Fact]
    public void PoetryOpensLinesAndABlankLineBetweenStanzasOpensAParagraph()
    {
        var words = UsfmReader.Read(
                """
                \id PSA
                \c 1
                \q1
                \v 1 Blessed is the man
                \q2 nor stand on the path
                \b
                \q1
                \v 2 but his delight
                """)
            .Chapters[0]!.Verses.SelectMany(verse => verse.Words)
            .ToList();

        words.Where(word => word.Break != null).Select(word => (word.Surface, word.Break))
            .Should().Equal(("Blessed", TextBreak.Line), ("nor", TextBreak.Line), ("but", TextBreak.Paragraph));
    }

    [Fact]
    public void AParagraphInsideAVerseOpensTheWordAfterIt()
    {
        var words = UsfmReader.Read(
                """
                \id GEN
                \c 2
                \v 4 This is the history.
                \p In the day that God made
                """)
            .Chapters[0]!.Verses[0]!.Words;

        words.Single(word => word.Break != null).Surface.Should().Be("In");
    }

    [Fact]
    public void TheBereanTablesPlaceAParagraphOnThePublishedWordTheirEnglishOpensWith()
    {
        var tables = Path.GetTempFileName();
        try
        {
            // Two rows of one verse, in the Berean's English order: the second opens a paragraph
            // mid-verse. Columns the reader does not use are left empty.
            File.WriteAllLines(tables,
            [
                "header",
                Row(english: 1, verse: 7, reference: "Genesis 1:1", par: "", rendering: " In the beginning "),
                Row(english: 2, verse: 7, reference: "", par: "<p class=|reg|>", rendering: " God "),
                Row(english: 3, verse: 7, reference: "", par: "<p class=|indent2|>", rendering: " created "),
            ]);

            var published = new Dictionary<string, List<BereanWord>>
            {
                ["Genesis 1:1"] = BereanWords.Split("In the beginning God created."),
            };

            var marks = BereanParagraphs.Read(tables, published)["Genesis 1:1"];

            marks.Should().BeEquivalentTo(new Dictionary<int, TextBreak>
            {
                [4] = TextBreak.Paragraph,
                [5] = TextBreak.Line,
            });
        }
        finally
        {
            File.Delete(tables);
        }
    }

    [Fact]
    public void ABereanClassNobodyDecidedAboutIsRefused()
    {
        var tables = Path.GetTempFileName();
        try
        {
            File.WriteAllLines(tables,
            [
                "header",
                Row(english: 1, verse: 1, reference: "Genesis 1:1", par: "<p class=|marginalia|>", rendering: " In "),
            ]);

            var read = () => BereanParagraphs.Read(tables, new Dictionary<string, List<BereanWord>>
            {
                ["Genesis 1:1"] = BereanWords.Split("In"),
            });

            read.Should().Throw<InvalidOperationException>().WithMessage("*marginalia*");
        }
        finally
        {
            File.Delete(tables);
        }
    }

    private static string Row(int english, int verse, string reference, string par, string rendering)
    {
        var cells = Enumerable.Repeat(string.Empty, 23).ToArray();
        cells[2] = english.ToString(System.Globalization.CultureInfo.InvariantCulture);
        cells[3] = verse.ToString(System.Globalization.CultureInfo.InvariantCulture);
        cells[12] = reference;
        cells[15] = par;
        cells[18] = rendering;
        return string.Join('\t', cells);
    }
}
