using Essenthos.Core.Utils;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// Reducing a Hebrew word to its consonants, which is the only thing two editions of the Masoretic
/// text preserve identically. Their tokenisation differs, their Strong numbering differs; the
/// letters do not.
/// </summary>
public class HebrewLetterTests
{
    [Theory]
    [InlineData("בְּרֵאשִׁית", "בראשית")]
    [InlineData("אֱלֹהִים", "אלהימ")]
    [InlineData("הָאָֽרֶץ", "הארצ")]
    public void DropsThePointingAndTheCantillation(string written, string letters) =>
        HebrewLetters.Of(written).Should().Be(letters);

    /// <summary>
    /// A letter that ends one edition's word stands inside the other's joined word, so the five
    /// final forms have to fold or every word ending in one of them fails to match.
    /// </summary>
    [Theory]
    [InlineData("מַיִם", "מימ")]
    [InlineData("אֶרֶץ", "ארצ")]
    [InlineData("מֶלֶךְ", "מלכ")]
    public void FoldsAFinalFormToTheOrdinaryOne(string written, string letters) =>
        HebrewLetters.Of(written).Should().Be(letters);

    /// <summary>
    /// The bug this pins, and it was worth thirteen points. The Westminster edition prints its
    /// paragraph markers after the verse-end mark and glues them to the last word, so Genesis 1:5
    /// ends *אחד ׃ פ* where BHSA ends *אחד*. Both markers are ordinary Hebrew letters, so they
    /// cannot be dropped by letter — only by where they stand.
    /// </summary>
    [Theory]
    [InlineData("אֶחָֽד׃ פ", "אחד")]
    [InlineData("שֵׁנִֽי׃ ס", "שני")]
    [InlineData("הָאָֽרֶץ׃", "הארצ")]
    public void DropsWhateverFollowsTheVerseEndMark(string written, string letters) =>
        HebrewLetters.Of(written).Should().Be(letters);

    /// <summary>
    /// BHSA records 6,488 morphemes that print no letters at all — an article that has assimilated
    /// into the preposition before it. They contribute nothing to a comparison, which is right, and
    /// must not break one.
    /// </summary>
    [Fact]
    public void HasNothingToSayAboutAWordThatPrintsNoLetters() =>
        HebrewLetters.Of("").Should().BeEmpty();

    /// <summary>
    /// The whole point: BHSA's three words and the Westminster edition's one are the same letters.
    /// </summary>
    [Fact]
    public void MakesTheThreeWordsOfOneJoinedWordConcatenate()
    {
        var split = HebrewLetters.Of("לְ") + HebrewLetters.Of("") + HebrewLetters.Of("אוֹר");
        split.Should().Be(HebrewLetters.Of("לָאֹור"));
    }
}
