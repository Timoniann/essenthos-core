using Essenthos.Core.Loading.Links;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// The mapping file's English against the King James as loaded: two digitisations of one text,
/// each case one way the verses the word-for-word reading refused part from each other.
/// </summary>
public sealed class EnglishWordJoinTests
{
    /// <summary>Genesis 14:5, where bible4u prints <em>Emins</em> and the file the 1769's <em>Emims</em>.</summary>
    [Fact]
    public void ANameSpeltOtherwiseIsTheSameWord()
    {
        var joined = EnglishWordJoin.Of(
            ["and", "the", "Emims", "in", "Shaveh", "Kiriathaim"],
            ["and", "the", "Emins", "in", "Shaveh", "Kiriathaim"]);

        joined.Should().NotBeNull();
        joined![2].Should().Equal(2);
    }

    /// <summary>Joshua 19:19: a verse of names, two of them spelt otherwise, has few words to pair.</summary>
    [Fact]
    public void AVerseOfNamesSpeltOtherwiseIsStillTheSameVerse()
    {
        EnglishWordJoin.Of(
                ["And", "Hapharaim", "and", "Shion", "and", "Anaharath"],
                ["And", "Haphraim", "and", "Shihon", "and", "Anaharath"])
            .Should().NotBeNull();
    }

    [Fact]
    public void APossessiveTheLoadedTextPrintsWithAnApostropheIsTheSameWord()
    {
        EnglishWordJoin.Of(["shall", "be", "theirs"], ["shall", "be", "their's"])
            .Should().NotBeNull();
    }

    /// <summary>The file's <em>firstborn</em> is bible4u's <em>first born</em>: one file word, two loaded.</summary>
    [Fact]
    public void AWordTheLoadedTextWritesAsTwoIsBothOfThem()
    {
        var joined = EnglishWordJoin.Of(
            ["Sidon", "his", "firstborn", "and", "Heth"],
            ["Sidon", "his", "first", "born", "and", "Heth"]);

        joined![2].Should().Equal(2, 3);
        joined[3].Should().Equal(4);
    }

    /// <summary>The file's <em>any thing</em> is bible4u's <em>anything</em>: two file words, one loaded.</summary>
    [Fact]
    public void TwoWordsTheLoadedTextWritesAsOneAreThatWord()
    {
        var joined = EnglishWordJoin.Of(
            ["any", "thing", "till", "thou"],
            ["anything", "till", "thou"]);

        joined![0].Should().Equal(0);
        joined[1].Should().Equal(0);
        joined[2].Should().Equal(1);
    }

    /// <summary>The file heads Psalm 119:1 with <em>ALEPH</em>, which the loaded verse does not carry.</summary>
    [Fact]
    public void AWordOnlyTheFileHasIsNoLoadedWord()
    {
        var joined = EnglishWordJoin.Of(
            ["ALEPH", "Blessed", "are", "the", "undefiled"],
            ["Blessed", "are", "the", "undefiled"]);

        joined![0].Should().BeEmpty();
        joined[1].Should().Equal(0);
    }

    /// <summary>A word the loaded text holds with no letter in it is not a word the file can name.</summary>
    [Fact]
    public void ALoadedWordWithNoLettersIsPassedOver()
    {
        var joined = EnglishWordJoin.Of(["Now", "the", "Amalekites"], ["", "Now", "the", "Amalekites"]);

        joined![0].Should().Equal(1);
    }

    /// <summary>
    /// Two words in a row that pair with nothing are not a spelling: a word dropped here and another
    /// added there shifts everything between them, and pairing by position would name the wrong words.
    /// </summary>
    [Fact]
    public void WordsThatShiftAgainstEachOtherAreNotTheSameVerse()
    {
        EnglishWordJoin.Of(
                ["In", "the", "beginning", "God", "created"],
                ["In", "a", "start", "made", "created"])
            .Should().BeNull();
    }

    /// <summary>
    /// A different word, as against a respelt one, counts against the verse, and a verse of three
    /// words has too few others to outweigh it.
    /// </summary>
    [Fact]
    public void AVerseTooShortToOutweighAWordPrintedOtherwiseIsNotTheSameVerse()
    {
        EnglishWordJoin.Of(["the", "start", "created"], ["the", "beginning", "created"])
            .Should().BeNull();
    }
}
