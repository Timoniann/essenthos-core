using System.Text.RegularExpressions;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading;
using Essenthos.Core.Loading.Links;
using Essenthos.Core.Sword;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// The Chinese Union Version and the Korean Revised Version, read from CrossWire's SWORD modules:
/// that every verse lands at its address, that the words are the edition's characters and nothing
/// else, and that FHL's numbers are read in the series the corpus's Hebrew and Greek write.
/// </summary>
public partial class SwordTextTests
{
    private static string Module(string name) =>
        TestResources.Folder(SwordTextSource.Texts[name].Folder);

    private static List<SwordWord> Words(string markup, SwordSegmentation segmentation, out List<VerseNoteDraft> notes)
    {
        var unit = 0;
        return SwordTextSource.Segment(OsisVerse.Parse(markup), segmentation, ref unit, out notes);
    }

    private static string Printed(IEnumerable<WordDraft> words) => string.Concat(words.Select(w => w.Surface + w.Trailer));

    [Fact]
    public void AChineseWordIsTheSpanTheEditionTagged()
    {
        var words = Words(
            """<w lemma="strong:H09002 strong:H07225">起初</w>，　<w lemma="strong:H0430">神</w> <w lemma="strong:H01254 strong:H8804 strong:H0853">創造</w> <w lemma="strong:H08064 strong:H0853">天</w> <w lemma="strong:H0776">地</w>。 """,
            SwordSegmentation.Tagged,
            out _);

        words.Select(w => w.Surface).Should().Equal("起初", "神", "創造", "天", "地");
        string.Concat(words.Select(w => w.Surface + w.Trailer)).Should().Be("起初，　神創造天地。");
        words[0].Numbers.Should().Equal("H9003", "H7225");
        words[2].Numbers.Should().Equal("H1254", "H853");
    }

    /// <summary>
    /// A number with no characters under it names a Hebrew or Greek word the translation does not
    /// render; there is nothing to give it to. A run no element claims is a word of its own.
    /// </summary>
    [Fact]
    public void AnEmptyTagIsNoWordAndAnUntaggedRunIsOne()
    {
        var words = Words(
            """「<w lemma="strong:G1063"></w>　<w lemma="strong:G2316">神</w> <w lemma="strong:G1325">賜給</w>他們，""",
            SwordSegmentation.Tagged,
            out _);

        words.Select(w => (w.Surface, w.Trailer, w.Elided)).Should().Equal(
            (string.Empty, "「　", true), ("神", string.Empty, false), ("賜給", string.Empty, false), ("他們", "，", false));
        words[3].Numbers.Should().BeEmpty();
        words[1].Unit.Should().NotBe(words[2].Unit);
    }

    [Fact]
    public void ANoteIsKeptBesideTheVerseAndNotInIt()
    {
        var words = Words(
            """<w lemma="strong:H07886">細羅</w><note placement="foot">就是賜平安者</note><w lemma="strong:H0935">來到</w>""",
            SwordSegmentation.Tagged,
            out var notes);

        words.Select(w => w.Surface).Should().Equal("細羅", "來到");
        notes.Should().ContainSingle().Which.Should().Be(new VerseNoteDraft(VerseNoteKind.Footnote, "就是賜平安者", 1));
    }

    [Theory]
    [InlineData("strong:H8804", new string[0])]
    [InlineData("strong:G5656 strong:G25", new[] { "G25" })]
    [InlineData("strong:H09001 strong:H03978", new[] { "H9005", "H3978" })]
    [InlineData("strong:H09003", new[] { "H9004" })]
    [InlineData("strong:H09004", new string[0])]
    public void FhlsNumbersAreReadInTheSeriesTheHebrewWrites(string lemma, string[] expected) =>
        UnionStrongNumbers.Read(lemma).Should().Equal(expected);

    [Fact]
    public void TheUnionVersionSaysWhichCharactersItIsPrintedIn()
    {
        SwordTextSource.Definitions["ChiUn"].Script.Should().Be("Hant");
        SwordTextSource.Definitions["ChiUns"].Script.Should().Be("Hans");
        SwordTextSource.Definitions["KorRV"].Script.Should().BeNull();
    }

    [Fact]
    public void AKoreanWordIsWhatTheEditionPrintsBetweenSpaces()
    {
        var words = Words(
            """<title canonical="true" type="psalm">다윗의 시</title> 여호와는 나의 목자시니 (셀라) """,
            SwordSegmentation.Spaced,
            out _);

        words.Select(w => w.Surface).Should().Equal("다윗의", "시", "여호와는", "나의", "목자시니", "셀라");
        string.Concat(words.Select(w => w.Surface + w.Trailer)).Should().Be("다윗의 시 여호와는 나의 목자시니 (셀라)");
    }

    [Fact]
    public void TheModuleIsReadAtTheAddressesItsVersificationGivesIt()
    {
        var verses = SwordModule.Verses(Module("ChiUn"));

        Regex.Replace(verses[(1, 1, 1)], "<[^>]+>", string.Empty).Should().StartWith("起初，　神 創造");
        Regex.Replace(verses[(66, 22, 21)], "<[^>]+>", string.Empty).Should().StartWith("願主 耶穌 的恩惠");
        verses.Should().ContainKey((64, 1, 15)).And.ContainKey((66, 12, 18));
    }

    /// <summary>
    /// The two verses SWORD's NRSV numbers apart are joined to the verse the King James prints them
    /// in, and that verse says it is both.
    /// </summary>
    [Theory]
    [InlineData("ChiUn")]
    [InlineData("KorRV")]
    public void TheTextIsPlacedInTheEnglishFrame(string module)
    {
        var text = SwordTextSource.Read(Module(module));
        var thirdJohn = text.Books[63].Chapters.Single().Verses;

        text.Books.Should().HaveCount(66);
        thirdJohn.Should().HaveCount(14);
        thirdJohn[^1].Stated.Should().Equal(new StatedNumberDraft(1, 14), new StatedNumberDraft(1, 15));
    }

    [Fact]
    public void RevelationTwelveEighteenOpensThirteenOne()
    {
        var revelation = SwordTextSource.Read(Module("ChiUn")).Books[65];

        revelation.Chapters[11].Verses.Should().HaveCount(17);
        var first = revelation.Chapters[12].Verses[0];
        first.Stated.Should().Equal(new StatedNumberDraft(12, 18), new StatedNumberDraft(13, 1));
        Printed(first.Words).Should().StartWith("那時龍就站在海邊的沙上。");
    }

    /// <summary>
    /// Nothing is added to the words and nothing dropped: every verse reads back as the module prints
    /// it, less the spaces CrossWire writes between Chinese elements, which Chinese does not print.
    /// The Korean Bible Society keeps the integrity of its text, and this is what holds it.
    /// </summary>
    [Theory]
    [InlineData("ChiUn", true)]
    [InlineData("ChiUns", true)]
    [InlineData("KorRV", false)]
    public void EveryVerseReadsBackAsTheModulePrintsIt(string module, bool chinese)
    {
        var folder = Module(module);
        var printed = SwordModule.Verses(folder).ToDictionary(
            verse => verse.Key,
            verse => Plain(verse.Value, chinese));
        var text = SwordTextSource.Read(folder);

        var read = 0;
        foreach (var book in text.Books)
        {
            foreach (var chapter in book.Chapters)
            {
                foreach (var verse in chapter.Verses.Where(v => v.Stated.Count == 0))
                {
                    Printed(verse.Words).Should().Be(
                        printed[(book.CanonicalOrdinal, chapter.Number, verse.Number)],
                        $"{book.Name} {chapter.Number}:{verse.Number} should read as the module prints it");
                    read++;
                }
            }
        }

        read.Should().BeGreaterThan(31_000);
    }

    /// <summary>
    /// The Korean prints 2 Corinthians 13 in thirteen verses and the module keeps its numbers, so
    /// the benediction is moved to the King James's 13:14 and says it is the edition's 13:13.
    /// </summary>
    [Fact]
    public void TheKoreanBenedictionStandsWhereTheKingJamesPrintsIt()
    {
        var chapter = SwordTextSource.Read(Module("KorRV")).Books[46].Chapters[12].Verses;

        chapter.Select(verse => verse.Number).Should().EndWith([11, 13, 14]);
        chapter[^1].Stated.Should().Equal(new StatedNumberDraft(13, 13));
        Printed(chapter[^1].Words).Should().StartWith("주 예수 그리스도의 은혜와");
        Printed(chapter[^2].Words).Should().Be("모든 성도가 너희에게 문안하느니라");
    }

    /// <summary>The Chinese marks a psalm's title by printing it in parentheses at the head of verse one.</summary>
    [Fact]
    public void APsalmTitleInParenthesesIsASuperscription()
    {
        var psalms = SwordTextSource.Read(Module("ChiUn")).Books[18].Chapters;

        psalms[2].Verses[0].MarksASuperscription.Should().BeTrue();
        psalms[0].Verses[0].MarksASuperscription.Should().BeFalse();
    }

    private static string Plain(string markup, bool chinese)
    {
        var text = Notes().Replace(markup, string.Empty);
        text = Tags().Replace(text, string.Empty);
        text = System.Net.WebUtility.HtmlDecode(text);
        return chinese
            ? string.Concat(text.Where(c => c is not (' ' or '\t' or '\r' or '\n')))
            : text.Trim();
    }

    [GeneratedRegex("<note[^>]*>.*?</note>", RegexOptions.Singleline)]
    private static partial Regex Notes();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex Tags();
}
