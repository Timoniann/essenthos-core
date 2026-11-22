using Essenthos.Core.Loading;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Witnesses;

/// <summary>Swete's Ecclesiastes, read from First1KGreek's transcription of volume 2, pages 480 to 505.</summary>
[Trait(TestCategory.Name, TestCategory.Corpus)]
public class SweteEcclesiastesTests(Swete swete) : IClassFixture<Swete>
{
    private const int Ecclesiastes = 21;

    private BookDraft Book => swete.Book(Ecclesiastes);

    [Fact]
    public void ItStandsBetweenProverbsAndTheSongAsSwetePrintsIt()
    {
        Book.Position.Should().Be(swete.Book(20).Position + 1);
        swete.Book(22).Position.Should().Be(Book.Position + 1);
    }

    /// <summary>
    /// Every verse Swete numbers, and no other. His chapter 6 ends at 11 and his 7 runs to 30, because he begins
    /// chapter 7 with the words the other numbering ends chapter 6 with.
    /// </summary>
    [Fact]
    public void EveryVerseItNumbersIsAVerse()
    {
        Book.Chapters.Select(chapter => chapter.Verses.Count)
            .Should().Equal(18, 26, 22, 17, 19, 11, 30, 17, 18, 20, 10, 14);
        foreach (var chapter in Book.Chapters)
        {
            chapter.Verses.Select(verse => verse.Number)
                .Should().Equal(Enumerable.Range(1, chapter.Verses.Count), $"chapter {chapter.Number}");
        }

        Book.Chapters.Sum(chapter => chapter.Verses.Sum(verse => verse.Words.Count)).Should().Be(4517);
    }

    /// <summary>
    /// The bracketed figures of the other numbering, Swete's signs for where Codex Ephraemi begins and ends, the
    /// Latin letters of the drop capital and the figure 6 are the page's marks, not words.
    /// </summary>
    [Fact]
    public void NoWordCarriesAMarkOfThePage() =>
        Book.Chapters.SelectMany(chapter => chapter.Verses).SelectMany(verse => verse.Words)
            .Where(word => word.Surface.Any(letter => letter is '§' or '¶' or '(' or ')' || char.IsAsciiLetterOrDigit(letter)))
            .Select(word => word.Surface)
            .Should().BeEmpty();

    [Theory]
    [InlineData(1, 1, "ΡΗΜΑΤΑ Ἐκκλησιαστοῦ υἱοῦ Δαυεὶδ")]
    [InlineData(1, 2, "Ματαιότης ματαιοτήτων, εἶπεν ὁ ἐκκλησιαστής, ματαιότης ματαιοτήτων")]
    [InlineData(1, 6, "ἐκεῖ πορεύεται πρὸς νότον καὶ κυκλοῖ πρὸς βορρᾶν·")]
    [InlineData(1, 17, "καὶ καρδία μου εἶδεν πολλά, σοφίαν καὶ γνῶσιν, καὶ ἔδωκα καρδίαν μου")]
    [InlineData(7, 1, "τί περισσὸν τῷ ἀνθρώπῳ ; ὅτι τίς οἶδεν ἀγαθὸν")]
    [InlineData(8, 1, "Τίς οἶδεν σοφούς; καὶ τίς οἶδεν λύσιν ῥήματος; σοφία ἀνθρώπου")]
    [InlineData(9, 1, "ὅτι σύμπαν τοῦτο ἔδωκα εἰς καρδίαν μου")]
    public void TheVerseOpensAsThePagePrintsIt(int chapter, int verse, string opening) =>
        Swete.Text(swete.Verse(Ecclesiastes, chapter, verse)).Should().StartWith(opening);

    [Fact]
    public void TheBookEndsWhereThePageEndsIt() =>
        Swete.Text(swete.Verse(Ecclesiastes, 12, 14)).Should().EndWith("ἐὰν ἀγαθὸν καὶ ἐὰν πονηρόν.");
}

/// <summary>
/// Swete's Psalms numbered as his pages number them. The transcription misnumbered four verses, ran four into the
/// verse before and lost eight of Psalm 118; what is left skipped is what the page does not print.
/// </summary>
[Trait(TestCategory.Name, TestCategory.Corpus)]
public class SwetePsalmNumberingTests(Swete swete) : IClassFixture<Swete>
{
    private const int Psalms = 19;

    [Theory]
    [InlineData(24, 14, "κραταίωμα Κύριος τῶν φοβουμένων αὐτόν")]
    [InlineData(47, 7, "τρόμος ἐπελάβετο αὐτῶν")]
    [InlineData(88, 48, "μνήσθητι τίς μου ἡ ὑπόστασις· μὴ γὰρ")]
    [InlineData(115, 6, "τίμιος ἐναντίον Κυρίου")]
    [InlineData(118, 19, "πάροικός εἰμι ἐν τῇ γῆ")]
    [InlineData(118, 28, "ἐνύσταξεν ἡ ψυχή μου ἀπὸ ἀκηδίας, βεβαίωσόν με ἐν τοῖς λόγοις σου·")]
    [InlineData(118, 29, "ὁδὸν ἀδικίας ἀπόστησον ἀπʼ ἐμοῦ, καὶ τῷ νόμῳ σου ἐλέησόν με.")]
    [InlineData(118, 30, "ὁδὸν ἀληθείας ᾑρετισάμην· τὰ κρίματά σου οὐκ ἐπελαθόμην.")]
    [InlineData(118, 31, "ἐκολλήθην τοῖς μαρτυρίοις σου· Κύριε, μή με καταισχύνῃς.")]
    [InlineData(118, 32, "ὁδὸν ἐντολῶν σου ἔδραμον, ὅταν ἐπλάτυνας τὴν καρδίαν μου.")]
    [InlineData(118, 33, "Νομοθέτησόν με, Κύριε, τὴν ὁδὸν τῶν δικαιωμάτων σου, καὶ ἐκζητήσω αὐτὴν διὰ παντός.")]
    [InlineData(118, 34, "συνέτισόν με, καὶ ἐκζητήσω τὸν νόμον σου, καὶ φυλάξω αὐτὸν ἐν ὅλῃ καρδίᾳ μου.")]
    [InlineData(118, 35, "ὁδήγησόν με ἐν τρίβῳ τῶν ἐντολῶν σου, ὅτι αὐτὸν ἠθέλησα.")]
    [InlineData(118, 131, "τὸ στόμα μου ἤνοιξα")]
    [InlineData(118, 133, "τὰ διαβήματά μου κατεύθυνον")]
    [InlineData(119, 4, "τὰ βέλη τοῦ δυνατοῦ")]
    public void TheVerseOpensAsThePagePrintsIt(int chapter, int verse, string opening) =>
        Swete.Text(swete.Verse(Psalms, chapter, verse)).Should().StartWith(opening);

    [Fact]
    public void TheDiapsalmaStandsAfterVerse46AsThePagePrintsIt()
    {
        Swete.Text(swete.Verse(Psalms, 88, 46)).Should().EndWith("αἰσχύνην. διάψαλμα.");
        Swete.Text(swete.Verse(Psalms, 88, 48)).Should().NotContain("διάψαλμα");
    }

    /// <summary>
    /// The verses Swete's apparatus gives from the other manuscripts and his text does not print: 115:5 is not
    /// numbered on the page at all. Every other number of every psalm is a verse.
    /// </summary>
    [Fact]
    public void TheOnlyNumbersSkippedAreTheOnesThePageSkips()
    {
        var skipped = swete.Book(Psalms).Chapters
            .SelectMany(chapter => Enumerable.Range(1, chapter.Verses.Max(verse => verse.Number))
                .Except(chapter.Verses.Select(verse => verse.Number))
                .Select(verse => $"{chapter.Number}:{verse}"));

        skipped.Should().BeEquivalentTo("43:8", "54:6", "63:3", "115:5", "117:4", "118:51", "135:23");
    }
}
