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
        VerseWords.Parse("[Авраам] сказал: вот.").Select(w => w.Word).Should().Equal("", "Авраам", "сказал", "вот");
        VerseWords.Parse("Он сказал [это] нам.").Select(w => w.Word).Should().Equal("Он", "сказал", "это", "нам");
    }

    [Fact]
    public void TheSourcesOwnSpacingIsKept()
    {
        VerseWords.Parse("Он сказал [это] нам.").Select(w => w.Trailer).Should().Equal(" ", " [", "] ", ".");
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
}