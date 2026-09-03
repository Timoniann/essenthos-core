using Essenthos.Core.XmlBible;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

public class VerseWordsTests
{
    // Psalm 23:1 as bible4u writes it: the Hebrew verse number in brackets, then the
    // superscription between its markers, then the verse.
    private const string Psalm231 = "(22-1) ^^Псалом Давида.^^ Господь--Пастырь мой; я ни в чем не буду нуждаться:";

    [Fact]
    public void CrossNumberingAndSuperscriptionMarkersAreNotWords()
    {
        VerseWords.Parse(Psalm231).Select(w => w.Word).Should().Equal(
            "Псалом", "Давида", "Господь", "Пастырь", "мой", "я", "ни", "в", "чем", "не", "буду", "нуждаться");
    }

    [Fact]
    public void TheVerseStillReadsAsItDidWithoutTheMarkup()
    {
        var rebuilt = string.Concat(VerseWords.Parse(Psalm231).Select(w => w.Word + w.Trailer));
        rebuilt.Should().Be("Псалом Давида. Господь--Пастырь мой; я ни в чем не буду нуждаться:");
    }

    [Fact]
    public void NoWordIsEmptyExceptPunctuationThatOpensTheVerse()
    {
        VerseWords.Parse("«Авраам» сказал: вот.").Select(w => w.Word).Should().Equal("", "Авраам", "сказал", "вот");
        VerseWords.Parse("Он сказал [это] нам.").Select(w => w.Word).Should().Equal("Он", "сказал", "это", "нам");
    }

    [Fact]
    public void TheSourcesOwnSpacingIsKept()
    {
        VerseWords.Parse("Он сказал «это» нам.").Select(w => w.Trailer).Should().Equal(" ", " «", "» ", ".");
    }

    /// <summary>
    /// The brackets the Synodal marks its supplied words with are markup, not text: they leave the
    /// surface and the words they covered say which span they were in.
    /// </summary>
    [Fact]
    public void ABracketIsNotACharacterOfTheVerse()
    {
        var words = VerseWords.Parse("Он сказал [это] нам.");

        string.Concat(words.Select(w => w.Word + w.Trailer)).Should().Be("Он сказал это нам.");
        words.Select(w => w.SuppliedSpan).Should().Equal(null, null, 1, null);
    }

    /// <summary>
    /// A verse that opens with a bracket used to begin with a word that was only the bracket. With
    /// the bracket gone there is nothing left of it, and the verse starts with its first word.
    /// </summary>
    [Fact]
    public void ABracketOpeningAVerseLeavesNoWordBehind()
    {
        var words = VerseWords.Parse("[Победители] взяли все.");

        words.Select(w => w.Word).Should().Equal("Победители", "взяли", "все");
        words[0].SuppliedSpan.Should().Be(1);
    }

    /// <summary>
    /// 1 Kings 5:7 and 78 other verses write two brackets side by side. They are two statements by
    /// the edition, and a span number keeps them apart where a flag would not.
    /// </summary>
    [Fact]
    public void TwoBracketsSideBySideAreTwoSpans()
    {
        var words = VerseWords.Parse("мудрого [для] [управления] этим");

        words.Select(w => w.Word).Should().Equal("мудрого", "для", "управления", "этим");
        words.Select(w => w.SuppliedSpan).Should().Equal(null, 1, 2, null);
    }

    [Fact]
    public void ABracketOverSeveralWordsIsOneSpan()
    {
        VerseWords.Parse("и [прибьешь ее гвоздем] к колоде")
            .Select(w => w.SuppliedSpan)
            .Should().Equal(null, 1, 1, 1, null, null);
    }

    [Fact]
    public void ANumberThatIsNotACrossReferenceStays()
    {
        VerseWords.Parse("жил 930 лет").Select(w => w.Word).Should().Equal("жил", "930", "лет");
    }

    [Fact]
    public void AFootnoteMarkerIsNotAWord()
    {
        VerseWords.StripMarkup("похули Бога и умри. (1)").Should().Be("похули Бога и умри.");
    }

    /// <summary>
    /// Ukrainian writes an apostrophe inside a word. Splitting сім'я gives сім and я, which mean
    /// other things, and it did that 3,726 times in the loaded corpus.
    /// </summary>
    [Theory]
    [InlineData("сім'я", "сім'я")]
    [InlineData("wife's name", "wife's")]
    [InlineData("из-за того", "из-за")]
    public void AnApostropheOrHyphenInsideAWordBelongsToIt(string verse, string firstWord)
    {
        VerseWords.Parse(verse)[0].Word.Should().Be(firstWord);
    }

    /// <summary>
    /// The same character opens and closes a quotation, and there it is punctuation. What decides
    /// is whether a letter stands on both sides of it.
    /// </summary>
    [Theory]
    [InlineData("'quoted' word", "quoted")]
    [InlineData("said: 'yes'", "said")]
    [InlineData("a — dash", "a")]
    public void AnApostropheOrDashBetweenWordsIsPunctuation(string verse, string firstWord)
    {
        VerseWords.Parse(verse).First(w => w.Word.Length > 0).Word.Should().Be(firstWord);
    }

    [Fact]
    public void TheVerseStillRebuildsFromItsWords()
    {
        const string verse = "And Adam called his wife's name Eve; сім'я, из-за.";

        string.Concat(VerseWords.Parse(verse).Select(w => w.Word + w.Trailer)).Should().Be(verse);
    }

    /// <summary>
    /// The King James file writes "Thus saith the Lord , Behold" wherever the small-caps divine
    /// name meets punctuation, and the reader used to store that space as part of the trailer.
    /// PRB-0151.
    /// </summary>
    [Theory]
    [InlineData("Thus saith the Lord , Behold", "Lord, ")]
    [InlineData("I am the Lord .", "Lord.")]
    [InlineData("the Lord ; and", "Lord; ")]
    [InlineData("the Lord : behold", "Lord: ")]
    [InlineData("the Lord ? Nay", "Lord? ")]
    [InlineData("the Lord ! Nay", "Lord! ")]
    [InlineData("(I am the Lord ) instead", "Lord) ")]
    public void TheSpaceBeforeClosingPunctuationIsClosedUp(string verse, string expected)
    {
        var rebuilt = string.Concat(VerseWords.Parse(verse).Select(w => w.Word + w.Trailer));

        rebuilt.Should().Contain(expected);
        rebuilt.Should().NotContain(" ,").And.NotContain(" .").And.NotContain(" )");
    }

    /// <summary>
    /// The marks that legitimately follow a space are left alone: a parenthesis and a low
    /// quotation mark open that way, a dash stands between clauses, and an apostrophe is a
    /// character of the word beside it. Listing the closing marks rather than taking a Unicode
    /// category is what keeps these out.
    /// </summary>
    [Theory]
    [InlineData("the Levites (I am he) instead")]
    [InlineData("сказав „так“ йому")]
    [InlineData("a - dash between")]
    public void PunctuationThatOpensSomethingKeepsItsSpace(string verse)
    {
        string.Concat(VerseWords.Parse(verse).Select(w => w.Word + w.Trailer)).Should().Be(verse);
    }

    /// <summary>
    /// The normalisation is part of the stripped form, which is what the corpus round trip compares
    /// against. If it were applied only in <c>Parse</c>, the round trip would fail on every one of
    /// the 2,632 verses; if it were applied only in <c>StripMarkup</c>, the corpus would keep the
    /// space and the test would still pass. Both come from one call for that reason.
    /// </summary>
    [Fact]
    public void TheStrippedFormCarriesTheSameNormalisation()
    {
        const string verse = "Thus saith the Lord , Behold";

        VerseWords.StripMarkup(verse).Should().Be("Thus saith the Lord, Behold");
        string.Concat(VerseWords.Parse(verse).Select(w => w.Word + w.Trailer))
            .Should().Be(VerseWords.StripMarkup(verse));
    }
}