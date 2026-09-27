using Essenthos.Core.BetaMasaheft;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading;
using Essenthos.Core.Loading.Frame;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// The rules the Ge'ez reader follows, on files written to show each one: which lines are verses,
/// which edition of a file is read, and what a line break inside a line is.
/// </summary>
[Trait(TestCategory.Name, TestCategory.Corpus)]
public class GeezReaderTests
{
    private const string Header =
        """<TEI xmlns="http://www.tei-c.org/ns/1.0"><teiHeader/><text><body>""";

    private const string Footer = "</body></text></TEI>";

    /// <summary>
    /// The printed Bible sets a heading between verses as a line with no number, and the files keep
    /// the prefaces and chapter lists the manuscripts carry around a book in parts that are not
    /// chapters, some of them numbered. None of it is text of the book.
    /// </summary>
    [Fact]
    public void OnlyAChaptersNumberedLinesAreVerses()
    {
        var chapters = Read(
            """
            <div type="edition">
              <div type="textpart" subtype="subtextunit"><l n="66">፷፮፡ ወእምድኅረ፡</l></div>
              <div type="textpart" subtype="chapter" n="1"><ab>
                <l>በእንተ፡ ኍልቈ፡ ትውልድ</l>
                <l n="1"><ref target="x"/>መጽሐፈ፡ ልደቱ።</l>
                <l n="2">አብርሃም፡ ወለዶ።</l>
              </ab></div>
            </div>
            """);

        chapters.Should().ContainSingle();
        GeezVerses.Sequence(chapters[0].Lines, unnumberedContinues: false).Verses
            .Should().Equal(new GeezVerse(1, "መጽሐፈ፡ ልደቱ።"), new GeezVerse(2, "አብርሃም፡ ወለዶ።"));
    }

    /// <summary>
    /// Jubilees and Ecclesiastes each hold a copyrighted edition beside the church's. Reading a file
    /// at its first edition is how the copyrighted one came to be taken for the only one.
    /// </summary>
    [Fact]
    public void AFileWithTwoEditionsIsReadAtTheOneChosenAndRefusedWithoutAChoice()
    {
        const string two =
            """
            <div type="edition" xml:id="Ran"><div type="textpart" subtype="chapter" n="1"><l n="1">ሀ</l></div></div>
            <div type="edition" xml:id="EOTCed"><div type="textpart" subtype="chapter" n="1"><l n="1">ለ</l></div></div>
            """;

        Read(two, "EOTCed")[0].Lines.Should().Equal(new GeezLine(1, "ለ"));
        FluentActions.Invoking(() => Read(two)).Should().Throw<InvalidOperationException>().WithMessage("*2 editions*");
    }

    /// <summary>The church's Jubilees numbers its chapters only in their identifiers, and prints a prologue before them.</summary>
    [Fact]
    public void AChapterNumberedOnlyByItsIdentifierIsReadAndOneNotNumberedAtAllIsLeftOut()
    {
        var chapters = Read(
            """
            <div type="edition">
              <div type="textpart" subtype="chapter" xml:id="introd"><l n="1">ዝንቱ፡</l></div>
              <div type="textpart" subtype="chapter" xml:id="Cap1"><l n="1">ወኮነ፡</l></div>
            </div>
            """, out var skipped);

        chapters.Select(chapter => chapter.Number).Should().Equal(1);
        skipped.Should().Equal("introd");
    }

    /// <summary>In the Psalter a line break divides a verse into its two halves.</summary>
    [Fact]
    public void ALineBreakInsideALineStartsALineOfTheVerse()
    {
        var line = Read(
            """
            <div type="edition"><div type="textpart" subtype="Psalmus" n="1"><ab>
              <l n="1"><lb n="1"/> ብፁዕ ፡ ብእሲ ፤<lb n="2"/>ወዘኢቆመ ።</l>
            </ab></div></div>
            """, out _, layout: GeezLayout.Psalter)[0].Lines[0];

        line.Text.Should().Be("ብፁዕ ፡ ብእሲ ፤\nወዘኢቆመ ።");
        GeezWords.Words(line.Text).Select(word => word.Break).Should().Equal(null, null, TextBreak.Line);
    }

    private static IReadOnlyList<GeezRawChapter> Read(string body, string? edition = null) =>
        Read(body, out _, edition);

    private static IReadOnlyList<GeezRawChapter> Read(
        string body, out IReadOnlyList<string> skipped, string? edition = null, GeezLayout layout = GeezLayout.Verses)
    {
        var path = Path.Combine(Path.GetTempPath(), $"geez-{Guid.NewGuid():n}.xml");
        File.WriteAllText(path, Header + body + Footer);
        try
        {
            return GeezReader.Read(path, edition, layout, out skipped);
        }
        finally
        {
            File.Delete(path);
        }
    }
}

/// <summary>How a hand-typed numbering is read, each case taken from a chapter of the church's Bible.</summary>
[Trait(TestCategory.Name, TestCategory.Corpus)]
public class GeezVerseSequenceTests
{
    /// <summary>A verse broken over two lines carries its number twice (Malachi 3:13).</summary>
    [Fact]
    public void ANumberRepeatedContinuesTheVerse()
    {
        var read = GeezVerses.Sequence([new(12, "ሀ"), new(13, "ለ"), new(13, "ሐ"), new(14, "መ")], false);

        read.Verses.Should().Equal(new GeezVerse(12, "ሀ"), new GeezVerse(13, "ለ ሐ"), new GeezVerse(14, "መ"));
        read.Joined.Should().Be(1);
    }

    /// <summary>
    /// A stray low number on the second half of a broken line is a continuation too: in 4 Ezra 5 the
    /// last word of verse 24 stands on a line of its own numbered 3.
    /// </summary>
    [Fact]
    public void ANumberThatGoesBackContinuesTheVerse()
    {
        GeezVerses.Sequence([new(24, "ሀ"), new(3, "ሰዓት።"), new(25, "ለ")], false).Verses
            .Should().Equal(new GeezVerse(24, "ሀ ሰዓት።"), new GeezVerse(25, "ለ"));
    }

    /// <summary>
    /// Where the next line confirms that exactly one verse is missing, the line in between is that
    /// verse, whatever it is numbered: Revelation 2 reads 26, 26, 28.
    /// </summary>
    [Fact]
    public void ANumberInTheGapTheNextLineConfirmsIsTheMissingVerse()
    {
        var read = GeezVerses.Sequence([new(26, "ሀ"), new(26, "ለ"), new(28, "ሐ")], false);

        read.Verses.Select(verse => verse.Number).Should().Equal(26, 27, 28);
        read.Renumbered.Should().Be(1);
    }

    /// <summary>
    /// A jump the next line carries on from is verses missing from the file (Zechariah 2 runs 1, 2,
    /// 3, 12, 13); one it does not carry on from is a stray number (Amos 5 runs 3, 12, 30, 30, 10, 4).
    /// </summary>
    [Fact]
    public void AJumpIsAGapOnlyWhereTheNextLineCarriesOnFromIt()
    {
        GeezVerses.Sequence([new(3, "ሀ"), new(12, "ለ"), new(13, "ሐ")], false).Verses
            .Select(verse => verse.Number).Should().Equal(3, 12, 13);

        GeezVerses.Sequence([new(3, "ሀ"), new(12, "ለ"), new(30, "ሐ"), new(30, "መ"), new(10, "ሠ"), new(4, "ረ")], false)
            .Verses.Should().Equal(new GeezVerse(3, "ሀ ለ ሐ መ ሠ"), new GeezVerse(4, "ረ"));
    }

    /// <summary>In the Psalter a line with no number is the second half of the verse before; elsewhere it is a heading.</summary>
    [Fact]
    public void AnUnnumberedLineContinuesTheVerseOnlyInThePsalter()
    {
        GeezLine[] lines = [new(15, "ሀ"), new(null, "ለ"), new(16, "ሐ")];

        GeezVerses.Sequence(lines, unnumberedContinues: true).Verses[0].Text.Should().Be("ሀ\nለ");
        GeezVerses.Sequence(lines, unnumberedContinues: false).Verses[0].Text.Should().Be("ሀ");
    }
}

/// <summary>Which rows of the Ge'ez are aligned against which Greek, and which against none.</summary>
[Trait(TestCategory.Name, TestCategory.Corpus)]
public class GeezAlignmentScopeTests
{
    [Theory]
    [InlineData("SWETE", 1, 1, 1, true)]
    [InlineData("SWETE", 75, 16, 1, true)]
    [InlineData("GRCBRENT", 75, 16, 1, true)]
    [InlineData("SWETE", 76, 1, 1, false)]
    [InlineData("SWETE", 17, 4, 1, false)]
    [InlineData("GRCBRENT", 17, 4, 1, true)]
    [InlineData("GRCBRENT", 19, 23, 1, false)]
    [InlineData("NESTLE1904", 40, 1, 1, true)]
    public void SweteAnswersOnlyWhereItStandsOnTheFramesRowsAndThePsalterNowhere(
        string greek, int book, int chapter, int verse, bool aligned)
    {
        GeezTextSource.Aligns(greek, book, chapter, verse).Should().Be(aligned);
    }

    /// <summary>
    /// The church's Sirach 30:25-36 is in the Greek manuscripts' order, where both Greek editions stand
    /// in the standard's, so neither answers for it.
    /// </summary>
    [Theory]
    [InlineData("GRCBRENT", 30, 24, true)]
    [InlineData("GRCBRENT", 30, 25, false)]
    [InlineData("SWETE", 32, 1, false)]
    [InlineData("GRCBRENT", 36, 31, false)]
    [InlineData("SWETE", 37, 1, true)]
    public void NeitherGreekAnswersForSirachInTheManuscriptsOrder(string greek, int chapter, int verse, bool aligned)
    {
        GeezTextSource.Aligns(greek, 72, chapter, verse).Should().Be(aligned);
    }

    /// <summary>Every verse of the books the church divides its own way is read, and read once.</summary>
    [Fact]
    public void EveryVerseOfAMappedBookHasOneLineAndEveryLineAConfidence()
    {
        var geez = GeezTextSource.Read(TestResources.Folder(GeezTextSource.Folder));
        var verses = geez.Books
            .Where(book => GeezVerseMap.Books.Contains(book.CanonicalOrdinal))
            .SelectMany(book => book.Chapters.SelectMany(chapter =>
                chapter.Verses.Select(verse => (book.CanonicalOrdinal, chapter.Number, verse.Number))))
            .ToList();
        var mapped = GeezVerseMap.Lines.SelectMany(line => line.From.Select(verse => (line.Book, verse.Chapter, verse.Verse))).ToList();

        mapped.Should().OnlyHaveUniqueItems().And.BeEquivalentTo(verses);
        GeezVerseMap.Lines.Should().OnlyContain(line => line.Confidence > 0 && line.Confidence < 1);
    }
}

/// <summary>A verse the typist ran on into the next, with the next one's number as a word between them.</summary>
[Trait(TestCategory.Name, TestCategory.Corpus)]
public class GeezRunInTests
{
    [Fact]
    public void ANumberTheChapterLacksBeginsThatVerse()
    {
        var verses = GeezVerses.Sequence(
            [new GeezLine(1, "ሀ ፡ ለ ] 2 ሐ ፡ መ 3 ሠ"), new GeezLine(4, "ረ")],
            unnumberedContinues: false).Verses;

        verses.Should().Equal(
            new GeezVerse(1, "ሀ ፡ ለ ]"), new GeezVerse(2, "ሐ ፡ መ"), new GeezVerse(3, "ሠ"), new GeezVerse(4, "ረ"));
    }

    /// <summary>1 Kings 16:28 carries a numbered list of its own, and a number the chapter has is a word.</summary>
    [Fact]
    public void ANumberThatGoesBackOrIsTakenIsAWord()
    {
        GeezVerses.Sequence(
                [new GeezLine(27, "ሀ"), new GeezLine(28, "ለ 2 ሐ 3 መ 27 ሠ"), new GeezLine(29, "ረ")],
                unnumberedContinues: false).Verses
            .Select(verse => verse.Number).Should().Equal(27, 28, 29);
    }
}

/// <summary>Words, divided the same way whichever way the source sets its wordspace.</summary>
[Trait(TestCategory.Name, TestCategory.Corpus)]
public class GeezWordTests
{
    [Theory]
    [InlineData("በቀዳሚ ፡ ገብረ ፡ እግዚአብሔር ፡ ሰማየ ፡ ወምድረ ።")]
    [InlineData("መጽሐፈ፡ ልደቱ፡ ለእግዚእነ፡ ኢየሱስ፡ ክርስቶስ። ወልደ፡ ዳዊት፡ ወልደ፡ አብርሃም።")]
    public void TheVerseReadsBackExactlyAndNoSurfaceHoldsAMark(string verse)
    {
        var words = GeezWords.Words(verse);

        string.Concat(words.Select(word => word.Surface + word.Trailer)).Should().Be(verse);
        words.Should().OnlyContain(word =>
            word.Surface.Length > 0 && !word.Surface.Contains('፡') && !word.Surface.Contains('።'));
    }

    [Fact]
    public void AWordspaceWithNoSpaceAfterItStillDividesTwoWords()
    {
        GeezWords.Words("ሐመልማለ፡ወዕፀወ").Select(word => (word.Surface, word.Trailer))
            .Should().Equal(("ሐመልማለ", "፡"), ("ወዕፀወ", ""));
    }

    /// <summary>
    /// Dillmann's round brackets mark what the Ethiopic adds to the Greek, and a bracket can open
    /// inside a word: of <c>ለ(ሱራፌል ፡ ወለ)ኪሩቤል</c> the first word is an addition and the second
    /// is the Greek's own word with "and to" added in front.
    /// </summary>
    [Fact]
    public void AWordMostlyInsideRoundBracketsIsAnAddition()
    {
        var words = GeezWords.Words("ወአዘዞሙ ፡ ለ(ሱራፌል ፡ ወለ)ኪሩቤል ፡ በሰይፈ ፡", marksBrackets: true);

        words.Select(word => (word.Surface, word.SuppliedSpan))
            .Should().Equal(("ወአዘዞሙ", null), ("ለሱራፌል", 1), ("ወለኪሩቤል", null), ("በሰይፈ", null));
        words.Should().OnlyContain(word => word.RestoredSpan == null);
    }

    /// <summary>
    /// Square brackets mark what his base manuscript lacks and he took from later ones, a span of
    /// its own; a bracket standing alone after a wordspace is no word, and a letter restored inside
    /// a word does not make the word restored.
    /// </summary>
    [Fact]
    public void SquareBracketsAreARestorationAndNeverAWord()
    {
        var words = GeezWords.Words("[ወይሰቅያ ፡ ለየብስ ፡] ወነገ[ሮ]ሙ ፡ [ቃለ ]", marksBrackets: true);

        words.Select(word => (word.Surface, word.RestoredSpan))
            .Should().Equal(("ወይሰቅያ", 1), ("ለየብስ", 1), ("ወነገሮሙ", null), ("ቃለ", 3));
        words.Should().OnlyContain(word => !word.Surface.Contains('[') && !word.Surface.Contains(']'));
    }

    /// <summary>A source whose brackets nobody has explained keeps them as they are typed.</summary>
    [Fact]
    public void BracketsMeanNothingWhereTheSourceIsNotDillmanns()
    {
        GeezWords.Words("ኅሩያኒ[ሁ] ወእሙንቱሰ").Select(word => word.Surface).Should().Equal("ኅሩያኒ[ሁ]", "ወእሙንቱሰ");
    }

    [Fact]
    public void ARunOfDotsIsAMarkAfterAWordAndNotAWord()
    {
        var words = GeezWords.Words("ወይቤ ፡ …. ሎቱ");

        words.Select(word => word.Surface).Should().Equal("ወይቤ", "ሎቱ");
        string.Concat(words.Select(word => word.Surface + word.Trailer)).Should().Be("ወይቤ ፡ …. ሎቱ");
    }

    [Fact]
    public void AnEthiopicNumeralIsAWord()
    {
        GeezWords.Words("፲ወ፪ ፡ ፲").Select(word => word.Surface).Should().Equal("፲ወ፪", "፲");
    }
}

/// <summary>The eighty-one books as the pinned files hold them, read from the corpus on this machine.</summary>
[Trait(TestCategory.Name, TestCategory.Corpus)]
public class GeezTextTests
{
    private static readonly Lazy<TextSource> Text =
        new(() => GeezTextSource.Read(TestResources.Folder(GeezTextSource.Folder)));

    private static BookDraft Book(int ordinal) => Text.Value.Books.Single(book => book.CanonicalOrdinal == ordinal);

    private static VerseDraft Verse(int ordinal, int chapter, int verse) =>
        Book(ordinal).Chapters.Single(c => c.Number == chapter).Verses.Single(v => v.Number == verse);

    private static string Opening(VerseDraft verse, int words = 3) =>
        string.Concat(verse.Words.Take(words).Select(word => word.Surface + word.Trailer)).TrimEnd();

    [Fact]
    public void TheTextHoldsTheEthiopianCanonInItsOwnOrder()
    {
        Text.Value.Books.OrderBy(book => book.Position).Select(book => book.CanonicalOrdinal)
            .Should().Equal(Canons.Find("ethiopian")!.Ordinals);
    }

    /// <summary>
    /// Chapters and verses per book, read from the pinned files: 38,537 verses and 505,888 words in all. A change here
    /// is a change to the source or to how it is read, and either is worth seeing.
    /// </summary>
    public static TheoryData<int, int, int> Counts => new()
    {
        { 1, 50, 1526 }, { 2, 40, 1170 }, { 3, 27, 858 }, { 4, 36, 1287 }, { 5, 34, 956 },
        { 6, 24, 652 }, { 7, 21, 617 }, { 8, 4, 85 }, { 9, 31, 804 }, { 10, 24, 692 },
        { 11, 22, 743 }, { 12, 25, 719 }, { 13, 29, 952 }, { 14, 36, 854 }, { 86, 50, 1292 },
        { 85, 108, 1058 }, { 15, 10, 272 }, { 16, 13, 408 }, { 68, 9, 448 }, { 69, 12, 637 },
        { 70, 14, 242 }, { 71, 16, 343 }, { 17, 13, 252 }, { 87, 36, 753 }, { 88, 21, 424 },
        { 89, 10, 208 }, { 18, 42, 1020 }, { 19, 151, 2620 }, { 91, 26, 766 }, { 92, 6, 161 },
        { 75, 19, 321 }, { 21, 12, 226 }, { 22, 8, 130 }, { 72, 51, 1360 }, { 23, 66, 1289 },
        { 24, 52, 1346 }, { 67, 5, 141 }, { 25, 5, 154 }, { 76, 1, 43 }, { 90, 9, 9 },
        { 26, 48, 1272 }, { 27, 14, 437 }, { 28, 14, 197 }, { 30, 9, 147 }, { 33, 7, 105 },
        { 29, 3, 73 }, { 31, 1, 20 }, { 32, 4, 48 }, { 34, 3, 47 }, { 35, 3, 56 },
        { 36, 3, 52 }, { 37, 2, 38 }, { 38, 14, 200 }, { 39, 4, 55 },
        { 40, 28, 1072 }, { 41, 16, 678 }, { 42, 24, 1150 }, { 43, 21, 879 }, { 44, 28, 1004 },
        { 45, 16, 430 }, { 46, 16, 438 }, { 47, 13, 260 }, { 48, 6, 149 }, { 49, 6, 154 },
        { 50, 4, 104 }, { 51, 4, 94 }, { 52, 5, 89 }, { 53, 3, 47 }, { 54, 6, 113 },
        { 55, 4, 84 }, { 56, 3, 46 }, { 57, 1, 25 }, { 58, 13, 302 }, { 59, 5, 108 },
        { 60, 5, 105 }, { 61, 3, 61 }, { 62, 5, 105 }, { 63, 1, 13 }, { 64, 1, 15 },
        { 65, 1, 25 }, { 66, 22, 402 },
    };

    [Theory]
    [MemberData(nameof(Counts))]
    public void EachBookHoldsTheChaptersAndVersesItsFileNumbers(int ordinal, int chapters, int verses)
    {
        var book = Book(ordinal);

        book.Chapters.Should().HaveCount(chapters);
        book.Chapters.Sum(chapter => chapter.Verses.Count).Should().Be(verses);
    }

    [Fact]
    public void EveryBookIsCountedOnce()
    {
        Counts.Select(row => (int)row[0]).Should().BeEquivalentTo(Text.Value.Books.Select(book => book.CanonicalOrdinal));
        Counts.Sum(row => (int)row[2]).Should().Be(38537);
        Text.Value.Books.Sum(book => book.Chapters.Sum(chapter => chapter.Verses.Sum(verse => verse.Words.Count)))
            .Should().Be(505888);
    }

    /// <summary>
    /// Every address is claimed once: a hand-typed numbering read at its word would give a chapter
    /// two verses of one number, which the corpus refuses.
    /// </summary>
    [Fact]
    public void EveryChapterAndEveryVerseHasAnAddressOfItsOwn()
    {
        foreach (var book in Text.Value.Books)
        {
            book.Chapters.Select(chapter => chapter.Number).Should().OnlyHaveUniqueItems(book.Name)
                .And.OnlyContain(number => number > 0, book.Name);

            foreach (var chapter in book.Chapters)
            {
                chapter.Verses.Select(verse => verse.Number).Should().BeInAscendingOrder($"{book.Name} {chapter.Number}")
                    .And.OnlyHaveUniqueItems($"{book.Name} {chapter.Number}")
                    .And.OnlyContain(number => number > 0, $"{book.Name} {chapter.Number}");
                chapter.Verses.Should().OnlyContain(verse => verse.Words.Count > 0, $"{book.Name} {chapter.Number}");
            }
        }
    }

    /// <summary>Jubilees is the church's, with its prologue left out, and not VanderKam's 1,139 verses.</summary>
    [Fact]
    public void JubileesAndEcclesiastesAreTheChurchsEditions()
    {
        Opening(Verse(86, 1, 1)).Should().Be("ወኮነ፡ በቀዳሚ፡ ዓመት፡");
        Book(21).Chapters.Sum(chapter => chapter.Verses.Count).Should().Be(226, "Mercer's has 222");
    }

    /// <summary>
    /// The Letter of Jeremiah counts its title as verse 0. The frame has no address before 1, so it
    /// stands at 1 and says what the church numbers it.
    /// </summary>
    [Fact]
    public void AChapterCountedFromZeroIsCountedFromOneAndSaysSo()
    {
        var letter = Book(76).Chapters.Single();

        letter.Verses.Select(verse => verse.Number).Should().Equal(Enumerable.Range(1, 43));
        letter.Verses[0].Stated.Should().Equal(new StatedNumberDraft(1, 0));
        letter.Verses[^1].Stated.Should().Equal(new StatedNumberDraft(1, 42));
    }

    /// <summary>3 Ezra is the Greek 1 Esdras and Ezra Sutuel the Latin 4 Ezra, each a chapter off in its file.</summary>
    [Fact]
    public void TheTwoBooksOfEzraTheFilesNumberAChapterOffStandWhereTheirWordsAre()
    {
        var josiah = Verse(68, 1, 1);
        Opening(josiah).Should().Be("ወአምጽአ፡ ኢዮስያስ፡ ፋሲካ፡");
        josiah.Stated.Should().Equal(new StatedNumberDraft(2, 1));

        Opening(Verse(69, 3, 1)).Should().Be("አመ፡ ፴፡ ዓመት፡");
        Book(69).Chapters.Select(chapter => chapter.Number).Should().Equal(Enumerable.Range(3, 12));
    }

    [Fact]
    public void TheTwoLinesTheFilesMisplaceStandWhereTheirWordsAre()
    {
        Book(9).Chapters.Single(chapter => chapter.Number == 31).Verses.Should().HaveCount(13);
        Opening(Verse(10, 1, 1)).Should().Be("ወእምዝ፡ እምድኅረ፡ ሞተ፡");
        Opening(Verse(40, 7, 29)).Should().Be("እስመ፡ ከመ፡ መኰንን፡");
    }

    /// <summary>The headings the printed Bible sets between verses are not text of the Gospel.</summary>
    [Fact]
    public void NoHeadingIsReadAsAVerse()
    {
        Opening(Verse(40, 1, 1), 2).Should().Be("መጽሐፈ፡ ልደቱ፡");
        Book(44).Chapters.Should().HaveCount(28, "Acts' preface and list of chapter titles are not chapters");
    }

    [Fact]
    public void APsalmsTitleOpensItsFirstVerse()
    {
        var third = Verse(19, 3, 1);

        third.MarksASuperscription.Should().BeTrue();
        third.Words[0].Surface.Should().Be("መዝሙር");
    }

    [Fact]
    public void FourBaruchIsNineChaptersEachOnePassage()
    {
        Book(90).Chapters.Should().HaveCount(9).And.OnlyContain(chapter => chapter.Verses.Count == 1);
    }

    [Fact]
    public void OnlyTheBooksDividedInTheirOwnWayAreKeptApart()
    {
        GeezTextSource.UnlinkedBooks.Should().BeEquivalentTo([17, 75, 76]);
    }

    /// <summary>
    /// The frame places the Old Testament by the Greek numbering and leaves the New where it is: the
    /// Psalter is numbered as the Septuagint numbers it, Jeremiah stands in the Hebrew order the
    /// church's Bible prints, and 3 John 15 is the English 14.
    /// </summary>
    [Fact]
    public void TheFramePlacesTheEditionWhereItsNumberingSaysItStands()
    {
        var verses = (from book in Text.Value.Books
                      from chapter in book.Chapters
                      from verse in chapter.Verses
                      select (book.CanonicalOrdinal, chapter.Number, verse.Number, verse.Label,
                          verse.Words.Sum(word => word.Surface.Length))).ToList();
        var frame = TvtmsReader.Read(TestResources.Tvtms).Frame(Versification.Septuagint, EditionShape.Of(verses));

        frame.Resolve(19, 22, 1)[0].Should().Be(new CanonicalReference(19, 23, 1));
        frame.Resolve(24, 25, 1)[0].Should().Be(new CanonicalReference(24, 25, 1));
        frame.Resolve(40, 1, 1)[0].Should().Be(new CanonicalReference(40, 1, 1));
        frame.Resolve(64, 1, 15)[0].Should().Be(new CanonicalReference(64, 1, 14));
    }
}
