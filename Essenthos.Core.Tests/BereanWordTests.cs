using Essenthos.Core.Berean;
using Essenthos.Core.Loading;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// How the Berean's published verses become words, and how its tables' phrases are read.
///
/// The tokenisation is the load-bearing part: the table's phrases are matched against these words
/// one at a time, so a word this splits differently from the way the file counts them takes the
/// whole verse out of alignment from that point on.
/// </summary>
public class BereanWordTests
{
    private static string Rebuild(string verse) =>
        string.Concat(BereanWords.Split(verse).Select(word => word.Surface + word.Trailer));

    [Theory]
    [InlineData("In the beginning God created the heavens and the earth.")]
    [InlineData("“Men of Israel,” he said, “consider carefully what you are about to do.")]
    [InlineData("and he—Jerubbaal son of Joash—returned home and settled down.")]
    [InlineData("Jesus wept.")]
    public void RebuildsTheVerseCharacterForCharacter(string verse) =>
        Rebuild(verse).Should().Be(verse);

    /// <summary>
    /// An em dash joins two words into one printed token and they are still two words. A corpus that
    /// stored <em>seas—no</em> as one could not link the second of them to anything.
    /// </summary>
    [Fact]
    public void CountsTwoWordsJoinedByAnEmDashAsTwo()
    {
        var words = BereanWords.Split("the seas—no wonder");
        words.Select(word => word.Surface).Should().Equal("the", "seas", "no", "wonder");
        words[1].Trailer.Should().Be("—");
    }

    /// <summary>A hyphen and an apostrophe stand inside a word, unlike an em dash.</summary>
    [Theory]
    [InlineData("God's people", 2)]
    [InlineData("thirty-two men", 2)]
    [InlineData("the LORD's anointed one", 4)]
    public void KeepsAWordThatHasPunctuationInsideIt(string verse, int words) =>
        BereanWords.Split(verse).Should().HaveCount(words);

    /// <summary>
    /// A verse opening with a quotation mark has nowhere to put it but the first word, and losing it
    /// would be losing part of the text.
    /// </summary>
    [Fact]
    public void KeepsWhateverStandsBeforeTheFirstWord()
    {
        var words = BereanWords.Split("“Come and see.");
        words[0].Surface.Should().Be("“Come");
        Rebuild("“Come and see.").Should().Be("“Come and see.");
    }

    [Fact]
    public void HasNoWordsForAVerseThatPrintsNone() =>
        BereanWords.Split(string.Empty).Should().BeEmpty();

    /// <summary>
    /// The file's own notation. Brackets and braces mark English the Greek only implies, and
    /// <c>vvv</c> marks a rendering that stands elsewhere in the verse — none of the three is a word.
    /// </summary>
    [Theory]
    [InlineData(" [This is the] record ", new[] { "This", "is", "the", "record" })]
    [InlineData(" {at once} ", new[] { "at", "once" })]
    [InlineData(" the bull vvv ", new[] { "the", "bull" })]
    [InlineData(" - ", new string[0])]
    [InlineData(" . . . ", new string[0])]
    [InlineData("", new string[0])]
    public void ReadsAPhraseAsTheWordsItRenders(string phrase, string[] words) =>
        BereanWords.Rendering(phrase).Should().Equal(words);

    [Theory]
    [InlineData("God", "God,", true)]
    [InlineData("“Come", "come", true)]
    [InlineData("heavens", "heaven", false)]
    public void ComparesTwoWordsAsAReaderWould(string left, string right, bool same) =>
        BereanWords.Same(left, right).Should().Be(same);
}

/// <summary>
/// Resolving <c>1 Samuel 3:4</c> and its neighbours, which is the one place a book name with a space
/// in it can quietly become the wrong book.
/// </summary>
public class BereanReferenceTests
{
    [Theory]
    [InlineData("Genesis 1:1", 1, 1, 1)]
    [InlineData("1 Samuel 3:4", 9, 3, 4)]
    [InlineData("Song of Solomon 2:1", 22, 2, 1)]
    [InlineData("Revelation 22:21", 66, 22, 21)]
    [InlineData("Matthew 1:1", 40, 1, 1)]
    public void ReadsAReference(string reference, int book, int chapter, int verse)
    {
        BereanTextSource.Address(reference, out var b, out var c, out var v).Should().BeTrue();
        (b, c, v).Should().Be((book, chapter, verse));
    }

    [Theory]
    [InlineData("Verse")]
    [InlineData("Genesis")]
    [InlineData("Nowhere 1:1")]
    public void RefusesWhatIsNotAReference(string reference) =>
        BereanTextSource.Address(reference, out _, out _, out _).Should().BeFalse();
}
