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

[Trait(TestCategory.Name, TestCategory.Corpus)]
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
        CanonicalReference.TryParse("Oda.4:1", out _).Should().BeFalse();
        BookCodes.IsBeyondTheCanon("Oda").Should().BeTrue();
    }
}

/// <summary>
/// The frame against the places the traditions actually disagree. These are the verses a reader
/// opens two texts to compare, and they are the only places where getting this wrong is visible —
/// which is why a frame worked out from the texts themselves would have passed every other test.
/// </summary>
[Trait(TestCategory.Name, TestCategory.Corpus)]
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
[Trait(TestCategory.Name, TestCategory.Corpus)]
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

/// <summary>
/// The Greek editions letter the additions to Esther each in a way of their own, and each lettered
/// verse stands where the Latin and English Bibles print the verse of the addition it holds.
/// </summary>
[Trait(TestCategory.Name, TestCategory.Corpus)]
public class LetteredEditionFrameTests
{
    private const int Esther = 17;

    private const int Sirach = 72;

    private const int Daniel = 27;

    private const int FirstSamuel = 9;

    private static readonly VersificationRules Rules = TvtmsReader.Read(TestResources.Tvtms);

    private static readonly VersificationFrame Brenton = Rules.Frame(Versification.Septuagint, EditionShape.Of(
    [
        .. Pieces(1, 1, "bcdefghiklmnopqrs"),
        .. Pieces(3, 13, "abcdefg"),
        .. Pieces(4, 17, "abcdefghiklmnopqrstuwxyz"),
        .. Pieces(5, 1, "abcdef"),
        .. Pieces(5, 2, "ab"),
        .. Pieces(10, 3, "abcdefghikl"),
    ]));

    private static readonly VersificationFrame Swete = Rules.Frame(Versification.Septuagint, EditionShape.Of(
    [
        .. Pieces(1, 17, "a"),
        (Esther, 4, 1, string.Empty, 10),
        (Esther, 4, 1, "a", 10),
        (Esther, 4, 1, "a1", 10),
        (Esther, 4, 1, "b", 10),
        .. Pieces(4, 17, string.Empty),
        .. Enumerable.Range(1, 11).Select(verse => (Esther, 10, verse, string.Empty, 20)),
        (Esther, 10, 1, "a", 10),
    ]));

    [Fact]
    public void AdditionVersesAreReadFromTheLatinRows()
    {
        Rules.Additions[new AdditionVerse(Esther, 'A', 1)].Should().Be(new CanonicalReference(Esther, 11, 2));
        Rules.Additions[new AdditionVerse(Esther, 'F', 11)].Should().Be(new CanonicalReference(Esther, 11, 1));
        Rules.Additions.Should().HaveCount(17 + 7 + 30 + 16 + 24 + 11);
    }

    /// <summary>
    /// Brenton's unlettered 1:1 is the first verse of Mordecai's dream, and his 1:1s the verse the
    /// Hebrew opens with.
    /// </summary>
    [Theory]
    [InlineData(1, 1, "", 11, 2)]
    [InlineData(1, 1, "b", 11, 3)]
    [InlineData(1, 1, "r", 12, 6)]
    [InlineData(1, 1, "s", 1, 1)]
    [InlineData(3, 13, "a", 13, 1)]
    [InlineData(4, 17, "z", 14, 19)]
    [InlineData(10, 3, "l", 11, 1)]
    public void BrentonsLetteredEstherStandsAtTheAddressOfTheVerseItPrints(
        int chapter, int verse, string label, int toChapter, int toVerse)
    {
        Brenton.Resolve(Esther, chapter, verse, lettered: true, label)[0]
            .Should().Be(new CanonicalReference(Esther, toChapter, toVerse));
    }

    [Fact]
    public void AVerseBrentonRunsTogetherSpansTheVersesItJoins()
    {
        Brenton.Resolve(Esther, 4, 17, lettered: true, "c")
            .Should().Equal(new CanonicalReference(Esther, 13, 10), new CanonicalReference(Esther, 13, 11));
        Brenton.Resolve(Esther, 5, 1, lettered: true, string.Empty)
            .Should().Equal(new CanonicalReference(Esther, 5, 1), new CanonicalReference(Esther, 15, 1));
    }

    [Fact]
    public void TheHebrewVerseBesideTheLettersStaysWhereItIs()
    {
        Brenton.Resolve(Esther, 4, 17, lettered: true, string.Empty)
            .Should().Equal(new CanonicalReference(Esther, 4, 17));
    }

    /// <summary>
    /// Swete numbers each addition from one under the chapter it follows, so his 1:17a is the
    /// dream's last verse and not a second Esther 1:17.
    /// </summary>
    [Theory]
    [InlineData(1, 17, "a", 12, 6)]
    [InlineData(4, 1, "a", 13, 8)]
    [InlineData(4, 1, "a1", 13, 18)]
    [InlineData(4, 1, "b", 15, 1)]
    [InlineData(10, 1, "", 10, 4)]
    [InlineData(10, 11, "", 11, 1)]
    [InlineData(10, 1, "a", 10, 1)]
    public void SwetesAdditionsStandAtTheAddressOfTheVerseTheyPrint(
        int chapter, int verse, string label, int toChapter, int toVerse)
    {
        Swete.Resolve(Esther, chapter, verse, label.Length > 0, label)
            .Should().Equal(new CanonicalReference(Esther, toChapter, toVerse));
    }

    /// <summary>
    /// Swete's Letter of Jeremiah stands on the standard's seventy-three verses, one behind them
    /// for most of its length.
    /// </summary>
    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 3)]
    [InlineData(44, 44)]
    [InlineData(72, 73)]
    public void SwetesLetterOfJeremiahStandsOnTheStandardsVerses(int verse, int standard)
    {
        Swete.Resolve(76, 1, verse, lettered: false, string.Empty)[0]
            .Should().Be(new CanonicalReference(76, 1, standard));
    }

    /// <summary>An edition lettered like neither is placed exactly as before.</summary>
    [Fact]
    public void AnotherEditionIsUnaffected()
    {
        var other = Rules.Frame(Versification.Septuagint, EditionShape.Of(
            [.. Pieces(1, 1, "abcdefghiklmnopqr")]));

        other.Resolve(Esther, 1, 1, lettered: true, "b").Should().Equal(new CanonicalReference(Esther, 1, 1));
    }

    /// <summary>
    /// Brenton prints Sirach 30:25-36:16 in the Greek manuscripts' order under his own chapter
    /// numbers, and each of those verses stands at the standard verse it prints.
    /// </summary>
    [Theory]
    [InlineData(30, 24, "", 30, 24)]
    [InlineData(30, 13, "a", 30, 11)]
    [InlineData(30, 25, "", 33, 16)]
    [InlineData(30, 40, "", 33, 31)]
    [InlineData(31, 1, "", 34, 1)]
    [InlineData(32, 20, "", 35, 20)]
    [InlineData(33, 11, "", 36, 11)]
    [InlineData(33, 12, "", 30, 25)]
    [InlineData(34, 1, "", 31, 1)]
    [InlineData(35, 24, "", 32, 24)]
    [InlineData(36, 15, "", 33, 15)]
    [InlineData(36, 17, "", 36, 12)]
    [InlineData(36, 31, "", 36, 26)]
    [InlineData(37, 1, "", 37, 1)]
    public void BrentonsSirachStandsAtTheStandardVerseItPrints(
        int chapter, int verse, string label, int toChapter, int toVerse)
    {
        Brenton.Resolve(Sirach, chapter, verse, label.Length > 0, label)[0]
            .Should().Be(new CanonicalReference(Sirach, toChapter, toVerse));
    }

    /// <summary>
    /// A verse at a seam of the misplaced quires prints the end of one standard verse and the start of
    /// another, and stands at both, the one it prints most of first.
    /// </summary>
    [Fact]
    public void BrentonsSeamVersesStandAtBothHalves()
    {
        Brenton.Resolve(Sirach, 36, 16, lettered: false, string.Empty)
            .Should().Equal(new CanonicalReference(Sirach, 36, 11), new CanonicalReference(Sirach, 33, 16));
        Brenton.Resolve(Sirach, 30, 26, lettered: false, string.Empty)
            .Should().Equal(new CanonicalReference(Sirach, 33, 17), new CanonicalReference(Sirach, 33, 16));
    }

    /// <summary>Brenton's Greek runs the end of the standard's 36:6 into his 33:7; his English does not.</summary>
    [Fact]
    public void BrentonsGreekAndEnglishDivideSirach33Apart()
    {
        var greek = Rules.Frame(Versification.Septuagint, EditionShape.Of(
        [
            .. Pieces(1, 1, "bcdefghiklmnopqrs"),
            .. Pieces(4, 17, "abcdefghiklmnopqrstuwxyz"),
            .. Pieces(10, 3, "abcdefghikl"),
            (FirstSamuel, 17, 11, string.Empty, 40),
        ]));

        greek.Resolve(Sirach, 33, 7, lettered: false, string.Empty)
            .Should().Equal(new CanonicalReference(Sirach, 36, 7), new CanonicalReference(Sirach, 36, 6));
        greek.Resolve(Sirach, 34, 1, lettered: false, string.Empty)
            .Should().Equal(new CanonicalReference(Sirach, 31, 1));
        Brenton.Resolve(Sirach, 33, 7, lettered: false, string.Empty)
            .Should().Equal(new CanonicalReference(Sirach, 36, 7));
    }

    /// <summary>
    /// Swete prints Sirach in the standard's order, numbering 34-36 as the NRSV does and 33:16-31 with
    /// the Greek manuscripts' verse numbers.
    /// </summary>
    [Theory]
    [InlineData(30, 13, "b", 30, 25)]
    [InlineData(31, 31, "", 31, 31)]
    [InlineData(33, 15, "", 33, 15)]
    [InlineData(33, 16, "a", 33, 16)]
    [InlineData(33, 25, "", 33, 16)]
    [InlineData(33, 40, "", 33, 31)]
    [InlineData(34, 9, "", 34, 9)]
    [InlineData(34, 11, "", 34, 10)]
    [InlineData(34, 31, "", 34, 26)]
    [InlineData(35, 26, "", 35, 20)]
    [InlineData(36, 13, "a", 36, 11)]
    [InlineData(36, 16, "b", 36, 11)]
    [InlineData(36, 31, "", 36, 26)]
    public void SwetesSirachStandsAtTheStandardVerseItPrints(
        int chapter, int verse, string label, int toChapter, int toVerse)
    {
        Swete.Resolve(Sirach, chapter, verse, label.Length > 0, label)[0]
            .Should().Be(new CanonicalReference(Sirach, toChapter, toVerse));
    }

    /// <summary>
    /// Both Greek editions print the song in Vaticanus's order, which the versification data's columns
    /// do not number, so each of their verses stands at the verse of the song it prints.
    /// </summary>
    [Theory]
    [InlineData("Brenton", 54, "", 32)]
    [InlineData("Brenton", 55, "", 33)]
    [InlineData("Brenton", 58, "", 36)]
    [InlineData("Brenton", 72, "a", 45)]
    [InlineData("Brenton", 72, "b", 50)]
    [InlineData("Brenton", 77, "", 56)]
    [InlineData("Swete", 77, "", 55)]
    [InlineData("Swete", 80, "", 58)]
    [InlineData("Swete", 67, "", 47)]
    [InlineData("Swete", 69, "", 45)]
    [InlineData("Swete", 70, "", 50)]
    public void TheGreekEditionsSongStandsAtTheVerseItPrints(string edition, int verse, string label, int standard)
    {
        (edition == "Brenton" ? Brenton : Swete).Resolve(Daniel, 3, verse, label.Length > 0, label)
            .Should().Equal(new CanonicalReference(Daniel, 3, 30 + standard));
    }

    private static IEnumerable<(int, int, int, string, int)> Pieces(int chapter, int verse, string letters) =>
        [(Esther, chapter, verse, string.Empty, 40), .. letters.Select(letter => (Esther, chapter, verse, letter.ToString(), 40))];
}

/// <summary>
/// The Greek and the Latin print the Prayer of Azariah and the Song of the Three inside Daniel 3, and
/// number the Hebrew's 3:24-30 after it; the song stands after the chapter's thirty verses rather
/// than on them.
/// </summary>
[Trait(TestCategory.Name, TestCategory.Corpus)]
public class SongOfTheThreeFrameTests(VersificationFrames frames) : IClassFixture<VersificationFrames>
{
    private const int Daniel = 27;

    [Fact]
    public void TheStandardsSongVersesAreReadIntoDaniel3()
    {
        SongOfTheThree.TryParseAll("S3Y.1:29-30", out var joined).Should().BeTrue();
        joined.Should().Equal(new CanonicalReference(Daniel, 3, 59), new CanonicalReference(Daniel, 3, 60));
        SongOfTheThree.TryParseAll("Dan.3:24", out _).Should().BeFalse();
    }

    [Theory]
    [InlineData(23, 23)]
    [InlineData(24, 31)]
    [InlineData(54, 63)]
    [InlineData(55, 62)]
    [InlineData(80, 88)]
    [InlineData(90, 98)]
    [InlineData(91, 24)]
    [InlineData(97, 30)]
    public void AGreekEditionsSongStandsAfterTheChapter(int verse, int standard)
    {
        Greek().Resolve(Daniel, 3, verse, lettered: false)[0]
            .Should().Be(new CanonicalReference(Daniel, 3, standard));
    }

    [Theory]
    [InlineData(24, 3, 31)]
    [InlineData(77, 3, 85)]
    [InlineData(80, 3, 88)]
    [InlineData(91, 3, 24)]
    [InlineData(98, 4, 1)]
    public void TheLatinsSongStandsAfterTheChapterAndItsEndInChapter4(int verse, int chapter, int standard)
    {
        frames.Rules.Frame(Versification.Vulgate, Daniel3(100)).Resolve(Daniel, 3, verse, lettered: false)[0]
            .Should().Be(new CanonicalReference(Daniel, chapter, standard));
    }

    /// <summary>
    /// The Ge'ez has the depths and the heavens first, as Vaticanus has them, and the Latin's six lines
    /// of weather in an order of its own.
    /// </summary>
    [Theory]
    [InlineData(54, 62)]
    [InlineData(59, 67)]
    [InlineData(67, 77)]
    [InlineData(68, 76)]
    [InlineData(71, 75)]
    [InlineData(72, 80)]
    [InlineData(81, 89)]
    public void TheGeezSongStandsAtTheVerseItPrints(int verse, int standard)
    {
        var geez = frames.Rules.Frame(Versification.Septuagint, EditionShape.Of(
        [
            .. Enumerable.Range(1, 100).Select(number => (Daniel, 3, number, string.Empty, 60)),
            (Daniel, 13, 1, string.Empty, 60),
        ]));

        geez.Resolve(Daniel, 3, verse, lettered: false, string.Empty)
            .Should().Equal(new CanonicalReference(Daniel, 3, standard));
    }

    /// <summary>The verse the standard divides in two stands at both halves.</summary>
    [Fact]
    public void Verse52StandsAtBothVersesTheStandardMakesOfIt()
    {
        Greek().Resolve(Daniel, 3, 52, lettered: false)
            .Should().Equal(new CanonicalReference(Daniel, 3, 59), new CanonicalReference(Daniel, 3, 60));
    }

    [Fact]
    public void NoVerseOfTheChapterSharesARowWithAnother()
    {
        var frame = Greek();
        var rows = Enumerable.Range(1, 97).Select(verse => frame.Resolve(Daniel, 3, verse, lettered: false)[0]).ToList();

        rows.Should().OnlyHaveUniqueItems();
        rows.Count(row => row.Verse is >= 24 and <= 30).Should().Be(7);
    }

    /// <summary>An English Daniel 3, which has no song, is placed exactly as before.</summary>
    [Fact]
    public void TheEnglishChapterIsUnaffected()
    {
        frames.English.Resolve(Daniel, 3, 24, lettered: false).Should().Equal(new CanonicalReference(Daniel, 3, 24));
    }

    private VersificationFrame Greek() => frames.Rules.Frame(Versification.Septuagint, Daniel3(97));

    private static EditionShape Daniel3(int last) =>
        EditionShape.Of(Enumerable.Range(1, last).Select(verse => (Daniel, 3, verse, string.Empty, 60)));
}
