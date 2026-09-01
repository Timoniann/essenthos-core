using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Frame;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>Read once; the file is 5.8 MB and 22,875 rules.</summary>
public sealed class VersificationFrames
{
    internal IReadOnlyDictionary<Versification, VersificationFrame> All { get; } =
        TvtmsReader.Read(TestResources.Tvtms);

    internal VersificationFrame Hebrew => All[Versification.Original];

    internal VersificationFrame English => All[Versification.English];
}

public class CanonicalReferenceTests
{
    [Theory]
    [InlineData("Gen.2:25", 1, 2, 25)]
    [InlineData("Jol.4:21", 29, 4, 21)]
    [InlineData("Mal.3:19", 39, 3, 19)]
    [InlineData("3Jn.1:15", 64, 1, 15)]
    [InlineData("Rev.22:21", 66, 22, 21)]
    public void APlainReferenceParses(string value, int book, int chapter, int verse)
    {
        CanonicalReference.TryParse(value, out var reference).Should().BeTrue();
        reference.Should().Be(new CanonicalReference(book, chapter, verse));
    }

    /// <summary>
    /// A psalm's superscription is a verse in Hebrew and not a numbered verse in English. It is
    /// verse zero, which keeps the address an integer triple and sorts the title before verse one.
    /// </summary>
    [Fact]
    public void AChapterTitleIsVerseZero()
    {
        CanonicalReference.TryParse("Psa.51:Title", out var reference).Should().BeTrue();
        reference.Verse.Should().Be(CanonicalReference.TitleVerse).And.Be(0);
    }

    /// <summary>This address space has no verse parts; half a verse still sits at that verse.</summary>
    [Fact]
    public void AVersePartResolvesToItsVerse()
    {
        CanonicalReference.TryParse("Exo.28:29!a", out var reference).Should().BeTrue();
        reference.Should().Be(new CanonicalReference(2, 28, 29));
    }

    [Fact]
    public void ARangeWithinAChapterIsExpanded()
    {
        CanonicalReference.ParseAll("1Ki.22:43-44").Should().Equal(
            new CanonicalReference(11, 22, 43),
            new CanonicalReference(11, 22, 44));
    }

    /// <summary>
    /// The verses between two chapters cannot be named without knowing how long the first one is,
    /// so a range across a boundary keeps its ends and says nothing about the middle.
    /// </summary>
    [Fact]
    public void ARangeAcrossAChapterKeepsItsEnds()
    {
        CanonicalReference.ParseAll("Gen.2:25-3:1").Should().Equal(
            new CanonicalReference(1, 2, 25),
            new CanonicalReference(1, 3, 1));
    }

    [Fact]
    public void AListCarriesForwardWhatItDoesNotRepeat()
    {
        CanonicalReference.ParseAll("Gen.5:32; 6:1").Should().Equal(
            new CanonicalReference(1, 5, 32),
            new CanonicalReference(1, 6, 1));
    }

    [Fact]
    public void ABookOutsideTheCanonDoesNotParse()
    {
        CanonicalReference.TryParse("Tob.4:1", out _).Should().BeFalse();
        BookCodes.IsBeyondTheCanon("Tob").Should().BeTrue();
    }
}

/// <summary>
/// The frame against the places the traditions actually disagree. These are the verses a reader
/// opens two texts to compare, and they are the only places where getting this wrong is visible —
/// which is why a frame worked out from the texts themselves would have passed every other test.
/// </summary>
public class CanonicalFrameTests(VersificationFrames frames) : IClassFixture<VersificationFrames>
{
    /// <summary>
    /// Hebrew Joel has four chapters and English has three: the Hebrew fourth chapter is the English
    /// third, and the Hebrew third is the tail of the English second.
    /// </summary>
    [Theory]
    [InlineData(3, 1, 2, 28)]
    [InlineData(3, 5, 2, 32)]
    [InlineData(4, 1, 3, 1)]
    [InlineData(4, 21, 3, 21)]
    public void HebrewJoelIsAChapterAheadOfEnglishJoel(int chapter, int verse, int toChapter, int toVerse)
    {
        Primary(frames.Hebrew, 29, chapter, verse).Should().Be(new CanonicalReference(29, toChapter, toVerse));
    }

    /// <summary>Hebrew Malachi has three chapters where English has four.</summary>
    [Theory]
    [InlineData(3, 19, 4, 1)]
    [InlineData(3, 24, 4, 6)]
    public void HebrewMalachiHasNoFourthChapter(int chapter, int verse, int toChapter, int toVerse)
    {
        Primary(frames.Hebrew, 39, chapter, verse).Should().Be(new CanonicalReference(39, toChapter, toVerse));
    }

    /// <summary>
    /// The superscription of Psalm 51 is two verses in Hebrew and no numbered verse at all in
    /// English, so both land on the title and the Hebrew third verse is the English first.
    /// </summary>
    [Fact]
    public void APsalmSuperscriptionLandsOnTheTitle()
    {
        Primary(frames.Hebrew, 19, 51, 1).Verse.Should().Be(CanonicalReference.TitleVerse);
        Primary(frames.Hebrew, 19, 51, 2).Verse.Should().Be(CanonicalReference.TitleVerse);
        Primary(frames.Hebrew, 19, 51, 3).Should().Be(new CanonicalReference(19, 51, 1));
    }

    /// <summary>
    /// The frame holds only the differences. Genesis 1:1 is the same verse in every tradition, and a
    /// verse nobody wrote a rule about is where it says it is — which is why placing the whole
    /// Hebrew Bible is five thousand rules rather than twenty-three thousand.
    /// </summary>
    [Fact]
    public void AVerseNobodyWroteARuleAboutStaysWhereItIs()
    {
        Primary(frames.Hebrew, 1, 1, 1).Should().Be(new CanonicalReference(1, 1, 1));
        frames.Hebrew.RuleCount.Should().BeLessThan(10_000);
        frames.Hebrew.RuleCount.Should().BeGreaterThan(1_000);
    }

    /// <summary>
    /// The English tradition is the frame itself, so nothing in it moves. A rule that moved an
    /// English verse would mean the frame had been read as one of the traditions it measures.
    /// </summary>
    [Fact]
    public void TheEnglishTraditionIsTheFrameAndDoesNotMove()
    {
        foreach (var (book, chapter, verse) in new[] { (1, 1, 1), (29, 3, 1), (39, 4, 1), (19, 51, 1) })
        {
            Primary(frames.English, book, chapter, verse)
                .Should().Be(new CanonicalReference(book, chapter, verse));
        }
    }

    [Fact]
    public void EveryTraditionTheCorpusUsesIsCovered()
    {
        frames.All.Keys.Should().Contain([
            Versification.Original, Versification.English, Versification.Septuagint, Versification.Vulgate,
        ]);
    }

    internal static CanonicalReference Primary(VersificationFrame frame, int book, int chapter, int verse) =>
        frame.Resolve(book, chapter, verse)[0];
}
