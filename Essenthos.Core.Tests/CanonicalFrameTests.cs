using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Frame;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>Read once; the file is 5.8 MB and 22,875 rules.</summary>
public sealed class VersificationFrames
{
    internal VersificationRules Rules { get; } = TvtmsReader.Read(TestResources.Tvtms);

    internal VersificationFrame Hebrew => Rules.Frame(Versification.Original);

    internal VersificationFrame English => Rules.Frame(Versification.English);

    internal VersificationFrame Greek => Rules.Frame(Versification.Septuagint);
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
        foreach (var tradition in new[]
                 {
                     Versification.Original, Versification.English, Versification.Septuagint, Versification.Vulgate,
                 })
        {
            frames.Rules.Covers(tradition).Should().BeTrue();
        }
    }

    internal static CanonicalReference Primary(VersificationFrame frame, int book, int chapter, int verse) =>
        frame.Resolve(book, chapter, verse)[0];
}

/// <summary>
/// The lettered verses, which are the Septuagint's way of numbering material the Hebrew does not
/// have. A rule written for the address they share describes the undivided complex, and an edition
/// that prints them apart must not have it applied to each piece.
/// </summary>
public class LetteredVerseFrameTests(VersificationFrames frames) : IClassFixture<VersificationFrames>
{
    private const int FirstKings = 11;

    private const int Esther = 17;

    /// <summary>
    /// The worst case in the corpus. Brenton prints twenty-four verses at 3 Kingdoms 12:24, and the
    /// undivided rule names thirty-six addresses spread over three chapters — which, given to each
    /// of them, is 864 references saying every piece is every place.
    /// </summary>
    [Fact]
    public void TheUndividedRuleForThirdKingdomsTwelveTwentyFourNamesTheWholeComplex()
    {
        var whole = frames.Greek.Resolve(FirstKings, 12, 24);

        whole.Should().HaveCount(36);
        whole.Should().Contain(new CanonicalReference(FirstKings, 11, 19));
        whole.Should().Contain(new CanonicalReference(FirstKings, 14, 18));
    }

    [Fact]
    public void AnEditionThatPrintsThemApartPlacesEachWhereItPrintsIt()
    {
        frames.Greek.Resolve(FirstKings, 12, 24, lettered: true)
            .Should().Equal(new CanonicalReference(FirstKings, 12, 24));
    }

    /// <summary>
    /// The same for Esther, where the undivided rule reaches into the addition chapters the Greek
    /// carries as 1:1b to 1:1s.
    /// </summary>
    [Fact]
    public void TheSameHoldsForGreekEsther()
    {
        frames.Greek.Resolve(Esther, 1, 1).Should().HaveCountGreaterThan(1);
        frames.Greek.Resolve(Esther, 1, 1, lettered: true)
            .Should().Equal(new CanonicalReference(Esther, 1, 1));
    }

    /// <summary>
    /// Nothing else changes. A verse at an address the edition does not letter resolves exactly as
    /// it did, which is what keeps this from being a change to the whole frame.
    /// </summary>
    [Fact]
    public void AnAddressWithNoLettersIsUnaffected()
    {
        CanonicalFrameTests.Primary(frames.Hebrew, 29, 4, 1).Should().Be(new CanonicalReference(29, 3, 1));
        frames.Hebrew.Resolve(19, 51, 1).Should().HaveCount(1);
    }

    /// <summary>
    /// Greek Nehemiah is numbered from one in the versification data, so a text that keeps Esdras B
    /// whole and asks about its chapter 13 asks about Ezra 13, which does not exist.
    /// </summary>
    [Fact]
    public void GreekNehemiahIsAddressedAsNehemiah()
    {
        const int Nehemiah = 16;

        CanonicalFrameTests.Primary(frames.Greek, Nehemiah, 3, 33)
            .Should().Be(new CanonicalReference(Nehemiah, 4, 1));
    }
}
