using Essenthos.Core.Loading;
using Essenthos.Core.Usfm;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Essenthos.Core.Tests.Witnesses;

/// <summary>
/// Reading the head of a psalm out of a complete edition: the <c>\d</c> line USFM writes a
/// superscription on, which the reader that loads a text deliberately folds into the first verse
/// and which this has to be able to get at separately.
/// </summary>
public class PsalmSuperscriptionReaderTests
{
    /// <summary>
    /// Psalm 3 as eBible's <c>eng-kjv2006</c> writes it, shortened. The title is a line of its own
    /// before the first verse, its words are tagged with the Hebrew they render, and the divine
    /// name is marked for small capitals.
    /// </summary>
    private const string Psalm3 =
        """
        \id PSA Psalms
        \c 3
        \d A \w Psalm|strong="H4210"\w* of \w David|strong="H1732"\w*, when he \w fled|strong="H1272"\w*.
        \q1
        \v 1 \nd \+w LORD|strong="H3068"\+w*\nd*, how are they \w increased|strong="H7231"\w*?
        """;

    [Fact]
    public void TheSuperscriptionIsReadApartFromTheVerseItStandsBefore()
    {
        var title = UsfmReader.Superscriptions(Psalm3)[3];

        title.Select(word => word.Surface).Should().Equal("A", "Psalm", "of", "David", "when", "he", "fled");
        title.Select(word => word.StrongNumber).Should()
            .Equal(null, "H4210", null, "H1732", null, null, "H1272");
    }

    /// <summary>
    /// The same words are still in verse 1 for a text loaded through the ordinary reader, which is
    /// what the American Standard, the Berean, Geneva, the JPS TaNaKH, the World English Bible and
    /// Young's all hold today. Nothing about reading them separately changes that.
    /// </summary>
    [Fact]
    public void TheOrdinaryReaderStillHandsThemToTheFirstVerse() =>
        UsfmReader.Read(Psalm3).Chapters[0]!.Verses[0]!.Words.Select(word => word.Surface)
            .Should().StartWith(["A", "Psalm", "of", "David", "when", "he", "fled"]);

    /// <summary>
    /// The divine name in small capitals is typography over a word, not a note about it, so the
    /// marker goes and the letters stay as the edition sets them.
    /// </summary>
    [Fact]
    public void TheDivineNameKeepsItsLettersAndLosesItsMarker() =>
        UsfmReader.Read(Psalm3).Chapters[0]!.Verses[0]!.Words.Select(word => word.Surface)
            .Should().Contain("LORD");

    /// <summary>
    /// Psalm 119 heads each of its twenty-two stanzas with a Hebrew letter, and eBible writes those
    /// the same way a superscription is written. A title after the chapter's first verse is a
    /// heading inside the psalm and not the psalm's own.
    /// </summary>
    [Fact]
    public void AHeadingInsideThePsalmIsNotItsSuperscription() =>
        UsfmReader.Superscriptions(
            """
            \id PSA Psalms
            \c 119
            \d ALEPH.
            \v 1 Blessed are the undefiled in the way.
            \d BETH.
            \v 9 Wherewithal shall a young man cleanse his way?
            """)[119].Select(word => word.Surface).Should().Equal("ALEPH");
}
