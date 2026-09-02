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
