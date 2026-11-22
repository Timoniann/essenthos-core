using Essenthos.Core.Loading;
using Essenthos.Core.Usfm;
using Essenthos.Core.XmlBible;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Witnesses;

/// <summary>
/// The words a verse prints before the first address it states. bible4u and eBible write an address
/// only where the edition numbers what follows otherwise than the file, so the words before it are the
/// edition's verse of the file's own number, and a row holding them states that number first: the
/// Synodal's Psalm 9:1 holds its 9:1 (the superscription) and 9:2, not 9:2 alone.
/// </summary>
public class UnmarkedNumberTests
{
    [Theory]
    [InlineData("^^Начальнику хора. По смерти Лабена. Псалом Давида.^^ (9-2) Буду славить [Тебя], Господи.", true)]
    [InlineData("Пасовиська пустині спливаються краплями! (65-14) Луги зодягнулись отарами.", true)]
    [InlineData("(11-1) ^^Начальнику хора.^^ (11-2) Спаси, Господи.", false)]
    [InlineData("(113-9) Не нам, Господи, не нам.", false)]
    [InlineData("В начале сотворил Бог небо и землю.", false)]
    public void WordsStandBeforeTheFirstAddress(string verse, bool expected) =>
        VerseWords.WordsBeforeItsFirstAddress(verse).Should().Be(expected);

    [Fact]
    public void TheSynodalsSuperscriptionStatesItsOwnNumber()
    {
        var verses = Bible4uVerses("^^Начальнику хора. По смерти Лабена. Псалом Давида.^^ (9-2) Буду славить [Тебя], Господи, всем сердцем моим.");

        verses[0].Stated.Should().Equal(new StatedNumberDraft(9, 1), new StatedNumberDraft(9, 2));
    }

    [Fact]
    public void AVerseThatStatesItsFirstNumberIsLeftAsItIs() =>
        Bible4uVerses("(11-1) ^^Начальнику хора.^^ (11-2) Спаси, Господи.", chapter: 12)[0].Stated
            .Should().Equal(new StatedNumberDraft(11, 1), new StatedNumberDraft(11, 2));

    [Fact]
    public void JpsSuperscriptionStatesItsOwnNumber() =>
        UsfmReader.Read(
                """
                \id PSA Psalms
                \c 81
                \p
                \v 1 For the Leader; upon the Gittith. A Psalm of Asaph. (81-2) Sing aloud unto God our strength.
                \v 2 (81-3) Take up the melody, and sound the timbrel.
                """)
            .Chapters[0]!.Verses.Select(verse => verse.Stated).Should().BeEquivalentTo(
                [new[] { new UsfmAddress(81, 1), new UsfmAddress(81, 2) }, [new UsfmAddress(81, 3)]],
                options => options.WithStrictOrdering());

    /// <summary>JPS 1917 prints "BOOK II" before the address of 42:1, which the verse already states.</summary>
    [Fact]
    public void AnAddressTheVerseStatesIsNotStatedTwice() =>
        UsfmReader.Read(
                """
                \id PSA Psalms
                \c 42
                \v 1 BOOK II (42-1) For the Leader; Maschil of the sons of Korah. (42-2) As the hart panteth.
                """)
            .Chapters[0]!.Verses[0]!.Stated.Should().Equal(new UsfmAddress(42, 1), new UsfmAddress(42, 2));

    /// <summary>
    /// The World English Bible sets the Psalter's books as <c>\ms1 BOOK 2</c> over Psalm 42, and the American
    /// Standard sets them before the chapter, where read as passage they ended the psalm before.
    /// </summary>
    [Fact]
    public void ThePsaltersBookHeadingIsNoWordOfAVerse()
    {
        var book = UsfmReader.Read(
            """
            \id PSA Psalms
            \c 41
            \q1
            \v 13 Blessed be the LORD, the God of Israel. Amen and amen.
            \ms1 BOOK II
            \c 42
            \ms1 BOOK 2
            \q1
            \v 1 As the deer pants for the water brooks.
            """);

        book.Chapters[0]!.Verses[0]!.Words.Select(word => word.Surface).Should().NotContain(["BOOK", "II"]);
        book.Chapters[1]!.Verses[0]!.Words.Select(word => word.Surface).Should().StartWith(["As", "the", "deer"]);
    }

    /// <summary>
    /// The superscription is the words before the last address of a psalm's first verse, which opens the line after the
    /// title: one address after it (Psalm 9), one over it and one after (Psalm 12), two over its halves (Psalm 51).
    /// </summary>
    [Theory]
    [InlineData("^^Начальнику хора. По смерти Лабена. Псалом Давида.^^ (9-2) Буду славить [Тебя], Господи.", 9, 7)]
    [InlineData("(11-1) ^^Начальнику хора. На восьмиструнном. Псалом Давида.^^ (11-2) Спаси, Господи.", 12, 6)]
    [InlineData("(50-1) ^^Начальнику хора. Псалом Давида, (50-2) Когда приходил к нему пророк Нафан.^^ (50-3) Помилуй меня.", 51, 10)]
    [InlineData("(141-1) ^^Учение Давида.^^ Голосом моим к Господу воззвал я.", 142, 0)]
    public void ThePsalmsTitleIsTheWordsBeforeItsLastAddress(string text, int chapter, int title)
    {
        var words = Bible4uVerses(text, chapter)[0].Words;

        words.TakeWhile(word => word.Superscription).Count().Should().Be(title);
        words.Skip(title).Should().NotContain(word => word.Superscription);
    }

    [Fact]
    public void AnotherBooksFirstVerseHoldsNoTitle() =>
        Bible4uVerses("Пасовиська пустині спливаються краплями! (65-14) Луги зодягнулись отарами.", 65, book: 9)[0].Words
            .Should().NotContain(word => word.Superscription);

    [Fact]
    public void JpsSuperscriptionIsTheWordsBeforeTheAddress() =>
        UsfmReader.Read(
                """
                \id PSA Psalms
                \c 81
                \p
                \v 1 For the Leader; upon the Gittith. A Psalm of Asaph. (81-2) Sing aloud unto God our strength.
                \v 2 (81-3) Take up the melody, and sound the timbrel.
                """)
            .Chapters[0]!.Verses.Select(verse => verse.Words.Count(word => word.Superscription)).Should().Equal(10, 0);

    [Fact]
    public void ADescriptiveTitleIsTheSuperscription()
    {
        var verse = UsfmReader.Read(
                """
                \id PSA Psalms
                \c 9
                \d To the chief Musician upon Muthlabben, A Psalm of David.
                \q1
                \v 1 I will praise thee, O LORD, with my whole heart;
                \q2 I will shew forth all thy marvellous works.
                """)
            .Chapters[0]!.Verses[0]!;

        verse.MarksASuperscription.Should().BeTrue();
        verse.Words.TakeWhile(word => word.Superscription).Select(word => word.Surface).Should()
            .Equal("To", "the", "chief", "Musician", "upon", "Muthlabben", "A", "Psalm", "of", "David");
        verse.Words.Skip(10).Should().NotContain(word => word.Superscription);
    }

    private static IReadOnlyList<VerseDraft> Bible4uVerses(string text, int chapter = 9, int book = 19)
    {
        var bible = new XmlBibleParser().Parse(
            $"""
             <XMLBIBLE biblename="RUSV"><INFORMATION><title>RUSV</title><language>RUS</language><identifier>RUSV</identifier></INFORMATION>
             <BIBLEBOOK bnumber="{book}" bname="{(book == 19 ? "Псалтирь" : "Книга")}" bsname="{(book == 19 ? "Ps" : "1Sam")}">
             <CHAPTER cnumber="{chapter}"><VERS vnumber="1">{text}</VERS></CHAPTER>
             </BIBLEBOOK></XMLBIBLE>
             """);
        return Bible4uTextSource.Build(bible, Bible4uTextSource.Definitions["RUSV"]).Books[0].Chapters[0].Verses;
    }
}
