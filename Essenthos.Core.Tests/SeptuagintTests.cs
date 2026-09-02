using Essenthos.Core.Loading;
using Essenthos.Core.Septuagint;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

public class UsfmReaderTests
{
    private const string Genesis =
        """
        \id GEN - Brenton Greek Text
        \h ΓΕΝΕΣΙΣ
        \toc1 ΓΕΝΕΣΙΣ
        \mt1 ΓΕΝΕΣΙΣ
        \c 1
        \p
        \v 1 ἘΝ ἀρχῇ ἐποίησεν ὁ Θεὸς τὸν οὐρανὸν καὶ τὴν γῆν.
        \v 2 Ἡ δὲ γῆ ἦν ἀόρατος καὶ ἀκατασκεύαστος.
        \c 2
        \p
        \v 1 Καὶ συνετελέσθησαν ὁ οὐρανὸς καὶ ἡ γῆ.
        """;

    [Fact]
    public void ReadsTheBookTheChaptersAndTheVerses()
    {
        var book = UsfmReader.Read(Genesis);

        book.Book.Should().Be("GEN");
        book.Chapters.Should().HaveCount(2);
        book.Chapters[0]!.Verses.Should().HaveCount(2);
        book.Chapters[1]!.Verses.Should().ContainSingle();
    }

    [Fact]
    public void PunctuationIsATrailerRatherThanPartOfTheWord()
    {
        // The one thing that decides whether this witness can be compared with the others at all:
        // γῆν. and γῆν are the same word, and a corpus that thinks otherwise cannot align them.
        var words = UsfmReader.Read(Genesis).Chapters[0]!.Verses[0]!.Words;

        words.Should().HaveCount(10);
        words[0]!.Surface.Should().Be("ἘΝ");
        words[^1]!.Surface.Should().Be("γῆν");
        words[^1]!.Trailer.Should().Be(". ");
    }

    [Fact]
    public void TitlesAreNotVerses()
    {
        // \h, \toc1 and \mt1 all carry the book's name. Read as text they would put ΓΕΝΕΣΙΣ three
        // times at the head of Genesis 1:1.
        UsfmReader.Read(Genesis).Chapters[0]!.Verses[0]!.Words
            .Should().NotContain(word => word.Surface == "ΓΕΝΕΣΙΣ");
    }

    [Fact]
    public void APsalmSuperscriptionBelongsToItsPsalm()
    {
        const string psalm =
            """
            \id PSA
            \c 3
            \d Ψαλμὸς τῷ Δαυίδ
            \v 1 Κύριε τί ἐπληθύνθησαν
            """;

        var verse = UsfmReader.Read(psalm).Chapters[0]!.Verses[0]!;

        verse.Words.Select(w => w.Surface).Should().StartWith(["Ψαλμὸς", "τῷ", "Δαυίδ"]);
    }

    [Fact]
    public void AMarkerItHasNotBeenToldAboutIsAnError()
    {
        // Not a silent skip. A marker treated as matter drops everything after it, and a verse
        // quietly three words short is the kind of defect nothing ever catches.
        const string unknown =
            """
            \id GEN
            \c 1
            \v 1 alpha
            \zz beta
            """;

        var act = () => UsfmReader.Read(unknown);

        act.Should().Throw<InvalidOperationException>().WithMessage("*zz*");
    }

    [Fact]
    public void AFileWithNoIdCannotBePlaced()
    {
        const string headless =
            """
            \c 1
            \v 1 alpha
            """;

        var act = () => UsfmReader.Read(headless);

        act.Should().Throw<InvalidOperationException>().WithMessage("*id*");
    }
}

public class SeptuagintCanonTests
{
    [Theory]
    [InlineData("GEN", 1)]
    [InlineData("MAL", 39)]
    [InlineData("TOB", 70)]
    [InlineData("SIR", 72)]
    [InlineData("LJE", 76)]
    [InlineData("3MA", 80)]
    [InlineData("4MA", 81)]
    public void EveryBookBrentonPrintsHasAPlace(string code, int ordinal)
    {
        SeptuagintTextSource.Canonical(code).Should().Be(ordinal);
    }

    [Theory]
    [InlineData("ESG", 17)]
    [InlineData("DAG", 27)]
    public void TheGreekEstherAndDanielAreEstherAndDaniel(string code, int ordinal)
    {
        // Longer, not different. Giving them ordinals of their own would put one book in the canon
        // twice under two names, and the model exists precisely so it does not have to.
        SeptuagintTextSource.Canonical(code).Should().Be(ordinal);
    }

    [Fact]
    public void ABookWithNoPlaceSaysSoRatherThanLoadingSomewhereWrong()
    {
        var act = () => SeptuagintTextSource.Canonical("ODA2");

        act.Should().Throw<InvalidOperationException>().WithMessage("*ODA2*");
    }
}

/// <summary>Read once; fifty-two USFM files is about a second.</summary>
public sealed class Brenton
{
    internal TextSource Source { get; } = SeptuagintTextSource.Read(TestResources.SeptuagintFolder);

    internal BookDraft Book(int canonical) => Source.Books.Single(book => book.CanonicalOrdinal == canonical);
}

/// <summary>
/// Esdras B, which Brenton prints as one book of twenty-three chapters and the canon knows as two.
/// The second half is Nehemiah, and until it was split it had no canonical address at all: its
/// verses sat at Ezra 11:1 to 23:47, chapters no versification has, and a request for Nehemiah in
/// this text was a 404 over Greek that was loaded and complete.
/// </summary>
public class SeptuagintSecondEsdrasTests(Brenton brenton) : IClassFixture<Brenton>
{
    private const int Ezra = 15;

    private const int Nehemiah = 16;

    [Fact]
    public void EsdrasBIsReadAsTwoBooks()
    {
        brenton.Book(Ezra).Chapters.Should().HaveCount(10);
        brenton.Book(Nehemiah).Chapters.Should().HaveCount(13);
    }

    /// <summary>
    /// The versification data numbers Greek Nehemiah from one — it says Greek Nehemiah 3:33 is the
    /// standard 4:1 — so a verse that called itself Ezra 13:33 could match no rule in it.
    /// </summary>
    [Fact]
    public void NehemiahIsNumberedFromOne()
    {
        var nehemiah = brenton.Book(Nehemiah);

        nehemiah.Chapters.Select(chapter => chapter.Number).Should().Equal(Enumerable.Range(1, 13));
        nehemiah.Chapters[0]!.Verses.Should().HaveCount(11);
        nehemiah.Chapters[2]!.Verses.Should().HaveCount(37);
    }

    [Fact]
    public void TheTwoStandWhereBrentonPrintsThem()
    {
        var ezra = brenton.Book(Ezra);
        var nehemiah = brenton.Book(Nehemiah);

        nehemiah.Position.Should().Be(ezra.Position + 1);
        brenton.Source.Books.Select(book => book.CanonicalOrdinal).Should().OnlyHaveUniqueItems();
        brenton.Source.Books.Select(book => book.Position).Should().OnlyHaveUniqueItems();
    }

    /// <summary>Nothing is lost in the split: the twenty-three chapters are all still there.</summary>
    [Fact]
    public void EveryVerseOfEsdrasBSurvives()
    {
        var verses = brenton.Book(Ezra).Chapters.Sum(chapter => chapter.Verses.Count)
                     + brenton.Book(Nehemiah).Chapters.Sum(chapter => chapter.Verses.Count);

        verses.Should().Be(669);
    }
}

/// <summary>
/// The lettered verses, which are how the Greek numbers material the Hebrew does not have. The
/// reader keeps them apart; what follows is that the frame has to as well.
/// </summary>
public class SeptuagintLetteredVerseTests(Brenton brenton) : IClassFixture<Brenton>
{
    [Fact]
    public void TheEditionsLetteredVersesAreRead()
    {
        var lettered = brenton.Source.Books
            .SelectMany(book => book.Chapters)
            .SelectMany(chapter => chapter.Verses)
            .Count(verse => verse.Label.Length > 0);

        lettered.Should().Be(317);
    }

    /// <summary>
    /// Twenty-four verses at one address: 12:24 and the additions a to z, which Brenton letters in
    /// the classical Latin alphabet — no j, no v, no w.
    /// </summary>
    [Fact]
    public void ThirdKingdomsTwelveTwentyFourIsTwentyFourVerses()
    {
        var at = brenton.Book(11).Chapters
            .Single(chapter => chapter.Number == 12).Verses
            .Where(verse => verse.Number == 24)
            .ToList();

        at.Should().HaveCount(24);
        at.Select(verse => verse.Label).Should().OnlyHaveUniqueItems();
        at.Count(verse => verse.Label.Length == 0).Should().Be(1);
    }
}
