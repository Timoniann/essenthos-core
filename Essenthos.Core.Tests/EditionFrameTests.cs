using System.Collections.Concurrent;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading;
using Essenthos.Core.Loading.Frame;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// The versification data's tests, on an edition made up here so each one can be asked on its own.
/// </summary>
[Trait(TestCategory.Name, TestCategory.Corpus)]
public class VersificationTestTests
{
    private static readonly EditionShape Edition = EditionShape.Of(
    [
        (1, 1, 1, string.Empty, 40),
        (1, 1, 2, string.Empty, 10),
        (1, 1, 2, "b", 30),
        (1, 1, 3, string.Empty, 90),
        (17, 1, 1, string.Empty, 40),
    ]);

    [Theory]
    [InlineData("Gen.1:1=Exist", true)]
    [InlineData("Gen.1:9=Exist", false)]
    [InlineData("Gen.1:9=NotExist", true)]
    [InlineData("Gen.1:3=Last", true)]
    [InlineData("Gen.1:1=Last", false)]
    [InlineData("Gen.1:2.2=Exist", true)]
    [InlineData("Gen.1:1.2=Exist", false)]
    [InlineData("Est.1:1.1=Exist", false)]
    [InlineData("Gen.1:1=Exist & Gen.1:3=Last", true)]
    [InlineData("Gen.1:1=Exist & Gen.1:1=Last", false)]
    public void AConditionIsAnsweredByTheEdition(string cell, bool expected)
    {
        var conditions = VersificationTest.ParseAll(cell);

        conditions.Should().NotBeNull();
        conditions!.Answer(Edition).Should().Be(expected);
    }

    [Fact]
    public void EnglishEstherDoesNotReceiveAdditionsItDoesNotPrint()
    {
        var frame = TvtmsReader.Read(TestResources.Tvtms).Frame(Versification.English, Edition);

        frame.Resolve(17, 1, 1).Should().Equal(new CanonicalReference(17, 1, 1));
    }

    /// <summary>
    /// The Reina-Valera numbers Numbers 30 as the Hebrew does and stops a verse short of it. That
    /// fails one of the Hebrew column's tests and every one of the English column's, and the edition
    /// is still the Hebrew's: its 30:1 is the English 29:40.
    /// </summary>
    [Fact]
    public void AnEditionAVerseShortOfASchemeStillFollowsIt()
    {
        var numbers = EditionShape.Of(
        [
            .. Enumerable.Range(1, 39).Select(verse => (4, 29, verse, string.Empty, 40)),
            .. Enumerable.Range(1, 16).Select(verse => (4, 30, verse, string.Empty, 40)),
        ]);

        var frame = TvtmsReader.Read(TestResources.Tvtms).Frame(Versification.English, numbers);

        frame.Resolve(4, 30, 1).Should().Equal(new CanonicalReference(4, 29, 40));
        frame.Resolve(4, 30, 16).Should().Equal(new CanonicalReference(4, 30, 15));
    }

    /// <summary>
    /// A verse is compared with another by how much text stands in it, which is how the data tells
    /// apart the two editions that both print an address and differ in which of them holds the
    /// material.
    /// </summary>
    [Theory]
    [InlineData("Gen.1:1<Gen.1:3", true)]
    [InlineData("Gen.1:1>Gen.1:3", false)]
    [InlineData("Gen.1:1*2>Gen.1:3", false)]
    [InlineData("Gen.1:3>Gen.1:1*2", true)]
    public void ALengthComparisonReadsTheTextThatIsThere(string cell, bool expected)
    {
        VersificationTest.ParseAll(cell)!.Answer(Edition).Should().Be(expected);
    }

    /// <summary>A cell with nothing in it this corpus can read says nothing about any edition.</summary>
    [Theory]
    [InlineData("Sir.1:13=Exist & Sir.1:30=Last")]
    [InlineData("Psa.9:TextBeforeV1=NotExist")]
    [InlineData("")]
    public void ACellThisCorpusCannotReadAtAllIsNotAnswered(string cell)
    {
        VersificationTest.ParseAll(cell).Should().BeNull();
    }

    /// <summary>
    /// A cell part of which is about Sirach is answered on the rest of it — but only far enough to
    /// fail. A scheme is never chosen on the strength of half a condition, and a condition that
    /// plainly fails is a failure whatever else the cell asks about.
    /// </summary>
    [Theory]
    [InlineData("Gen.1:1=Exist & Tob.1:22=Last", null)]
    [InlineData("Gen.1:1=Last & Tob.1:22=Last", false)]
    public void ACellHalfAboutABookThisCorpusHasNotIsAnsweredOnlyFarEnoughToFail(string cell, bool? expected)
    {
        VersificationTest.ParseAll(cell)!.Answer(Edition).Should().Be(expected);
    }
}

/// <summary>
/// The frame for one edition rather than for its tradition.
///
/// The versification data describes twelve Greek numbering schemes and states, beside every rule,
/// the condition that says which of them it is about. Taking the one called <c>Greek</c> everywhere
/// placed Brenton's Exodus 22 one verse too high and moved its Leviticus 7 ten verses for a rule
/// about a Greek Bible this is not — and neither is visible in the text, because the wrong verse is
/// still a verse.
/// </summary>
public sealed class BrentonEdition
{
    private readonly VersificationRules rules = TvtmsReader.Read(TestResources.Tvtms);

    private readonly Lazy<VersificationFrame> tradition;

    private readonly Lazy<VersificationFrame> edition;

    public BrentonEdition()
    {
        tradition = new Lazy<VersificationFrame>(() => rules.Frame(Versification.Septuagint));
        edition = new Lazy<VersificationFrame>(
            () => rules.Frame(Versification.Septuagint, EditionShape.Of(Verses)));
    }

    /// <summary>Every verse this edition prints, read once: the fifty-two files are not quick.</summary>
    internal IReadOnlyList<(int Book, int Chapter, int Number, string Label, int Length)> Verses { get; } =
    [
        .. from book in SeptuagintTextSource.Read(TestResources.SeptuagintFolder).Books
           from chapter in book.Chapters
           from verse in chapter.Verses
           select (book.CanonicalOrdinal, chapter.Number, verse.Number, verse.Label,
               verse.Words.Sum(word => word.Surface.Length)),
    ];

    internal VersificationRules Rules => rules;

    internal VersificationFrame Tradition => tradition.Value;

    internal VersificationFrame Edition => edition.Value;
}

[Trait(TestCategory.Name, TestCategory.Corpus)]
public class EditionFrameTests(BrentonEdition brenton) : IClassFixture<BrentonEdition>
{
    private const int Genesis = 1;

    private const int Exodus = 2;

    private const int Leviticus = 3;

    private const int Deuteronomy = 5;

    private const int Nehemiah = 16;

    private const int Proverbs = 20;

    private const int Esther = 17;

    private const int Jeremiah = 24;

    private const int Daniel = 27;

    private const int Malachi = 39;

    private const int Sirach = 72;

    /// <summary>
    /// Brenton's Exodus 21 runs to verse 37, which is the condition the data writes against the
    /// Hebrew column and against no Greek one. Every verse of its Exodus 22 is therefore one lower
    /// than the frame, and the whole chapter was laid against the wrong Hebrew verse.
    /// </summary>
    [Theory]
    [InlineData(15, 16)]
    [InlineData(18, 19)]
    [InlineData(25, 26)]
    [InlineData(30, 31)]
    public void ExodusTwentyTwoIsNumberedAsTheHebrewNumbersIt(int verse, int standard)
    {
        Placed(brenton.Tradition, Exodus, 22, verse).Should().Be(new CanonicalReference(Exodus, 22, verse));
        Placed(brenton.Edition, Exodus, 22, verse).Should().Be(new CanonicalReference(Exodus, 22, standard));
    }

    /// <summary>
    /// The same failure in the other direction, and the one no count catches: a rule was applied
    /// that describes a Greek Bible dividing Leviticus 6 and 7 as this one does not, so verses that
    /// stood at their own address were moved ten away from it.
    /// </summary>
    [Theory]
    [InlineData(3)]
    [InlineData(15)]
    public void ARuleForAGreekEditionThisIsNotIsNotApplied(int verse)
    {
        Placed(brenton.Tradition, Leviticus, 7, verse).Should().NotBe(new CanonicalReference(Leviticus, 7, verse));
        Placed(brenton.Edition, Leviticus, 7, verse).Should().Be(new CanonicalReference(Leviticus, 7, verse));
    }

    /// <summary>
    /// Brenton's Leviticus 6 begins where the Hebrew's sixth chapter begins its eighth verse, so its
    /// verses stand seven higher than the frame throughout.
    /// </summary>
    [Theory]
    [InlineData(16, 23)]
    [InlineData(17, 24)]
    public void LeviticusSixIsSevenVersesAheadOfTheFrame(int verse, int standard)
    {
        Placed(brenton.Edition, Leviticus, 6, verse).Should().Be(new CanonicalReference(Leviticus, 6, standard));
    }

    [Theory]
    [InlineData(Genesis, 32, 15, 32, 14)]
    [InlineData(Deuteronomy, 13, 8, 13, 7)]
    [InlineData(Nehemiah, 10, 18, 10, 17)]
    public void AChapterThisEditionNumbersLikeTheHebrewIsPlacedLikeTheHebrew(
        int book,
        int chapter,
        int verse,
        int standardChapter,
        int standardVerse)
    {
        Placed(brenton.Edition, book, chapter, verse)
            .Should().Be(new CanonicalReference(book, standardChapter, standardVerse));
    }

    /// <summary>
    /// The Septuagint's Jeremiah is a different book in a different order, and the rules that say so
    /// are the largest thing the frame does. Their conditions fail here — Brenton divides Jeremiah's
    /// chapters as no scheme the data names does — and where nothing can be decided the tradition's
    /// own scheme still stands, so 38:31 is Jeremiah 31:31 as it always was.
    /// </summary>
    [Theory]
    [InlineData(38, 31, 31, 31)]
    [InlineData(38, 40, 31, 40)]
    [InlineData(36, 24, 29, 24)]
    public void WhereNothingCanBeDecidedTheTraditionStillPlacesTheVerse(
        int chapter,
        int verse,
        int standardChapter,
        int standardVerse)
    {
        Placed(brenton.Edition, Jeremiah, chapter, verse)
            .Should().Be(new CanonicalReference(Jeremiah, standardChapter, standardVerse));
    }

    /// <summary>
    /// Greek Esther is described by a scheme that runs the additions into the numbering, and this
    /// edition prints them as lettered verses instead. No Greek column answers to it, and nothing
    /// else is offered, so its numbered verses stay where they are rather than being renumbered by
    /// a rule about a book printed differently.
    /// </summary>
    [Fact]
    public void GreekEstherIsNotRenumberedByASchemeThatIntegratesItsAdditions()
    {
        Placed(brenton.Edition, Esther, 1, 21).Should().Be(new CanonicalReference(Esther, 1, 21));
        Placed(brenton.Edition, Esther, 1, 22).Should().Be(new CanonicalReference(Esther, 1, 22));
    }

    /// <summary>
    /// The Septuagint ends Malachi with Elijah, the turning of hearts and then the law of Moses, which
    /// the Hebrew puts first. Brenton numbers them 3:22 to 3:24, and each belongs beside the Hebrew
    /// verse that says the same thing — not beside the one after it, where the data's own rows put it.
    /// </summary>
    [Theory]
    [InlineData(22, 5)]
    [InlineData(23, 6)]
    [InlineData(24, 4)]
    public void MalachiEndsWithMosesAndEachVerseStandsBesideItsOwnWords(int verse, int standard)
    {
        Placed(brenton.Edition, Malachi, 3, verse).Should().Be(new CanonicalReference(Malachi, 4, standard));
    }

    /// <summary>
    /// What the whole change amounts to. Reading the conditions, and the passages written down for
    /// this edition where the data describes none, moves 583 of Brenton's 28,597 verses and leaves
    /// the other 98% exactly where the tradition put them — which is the shape this should have: the
    /// schemes agree almost everywhere, and the passages where they do not are the passages a reader
    /// is comparing.
    /// </summary>
    [Fact]
    public void ReadingTheConditionsMovesTheVersesTheSchemesDisagreeAbout()
    {
        var moved = brenton.Verses.Count(verse =>
            brenton.Tradition.Resolve(verse.Book, verse.Chapter, verse.Number, verse.Label.Length > 0)[0] !=
            brenton.Edition.Resolve(verse.Book, verse.Chapter, verse.Number, verse.Label.Length > 0)[0]);

        brenton.Verses.Should().HaveCount(28_597);
        moved.Should().Be(583);
    }

    /// <summary>
    /// Brenton leaves out Exodus 25:6, 28:23-28 and 40:7, 11 and 28, and Deuteronomy 14:18, and
    /// numbers the rest of each passage as the Hebrew does. Every test the Hebrew column writes holds
    /// except the ones asking for the verses it leaves out, and every test of the Greek column fails;
    /// taking the Greek anyway, because it is the tradition's own, put each verse beside the Hebrew of
    /// the next one or the one after.
    /// </summary>
    [Theory]
    [InlineData(Exodus, 25, 10)]
    [InlineData(Exodus, 28, 35)]
    [InlineData(Exodus, 40, 20)]
    [InlineData(Deuteronomy, 14, 21)]
    public void AnEditionThatLeavesOutVersesFollowsTheSchemeItOtherwiseAnswersTo(int book, int chapter, int verse)
    {
        Placed(brenton.Edition, book, chapter, verse).Should().Be(new CanonicalReference(book, chapter, verse));
    }

    /// <summary>
    /// Brenton's chapter 30 of Jeremiah runs Edom, Ammon, Kedar and Damascus; it ends Elam at 25:20
    /// with the date the Hebrew opens it with; and its chapter 32 is the cup of wrath under the
    /// Hebrew's own verse numbers. No scheme of the data divides the book that way, so each of these
    /// stood beside a Hebrew verse about another nation.
    /// </summary>
    [Theory]
    [InlineData(30, 1, 49, 7)]
    [InlineData(30, 16, 49, 22)]
    [InlineData(30, 17, 49, 1)]
    [InlineData(30, 23, 49, 28)]
    [InlineData(30, 29, 49, 23)]
    [InlineData(25, 15, 49, 35)]
    [InlineData(25, 20, 49, 34)]
    [InlineData(32, 15, 25, 15)]
    [InlineData(32, 38, 25, 38)]
    public void BrentonsOwnDivisionOfJeremiahStandsBesideItsWords(
        int chapter,
        int verse,
        int standardChapter,
        int standardVerse)
    {
        Placed(brenton.Edition, Jeremiah, chapter, verse)
            .Should().Be(new CanonicalReference(Jeremiah, standardChapter, standardVerse));
    }

    /// <summary>
    /// Agur's numerical sayings and the words of Lemuel's mother stand in chapter 24 of the Greek, and
    /// Brenton numbers them on from 24:35 where Rahlfs keeps the Hebrew's numbers.
    /// </summary>
    [Theory]
    [InlineData(35, 30, 15)]
    [InlineData(53, 30, 33)]
    [InlineData(54, 31, 1)]
    [InlineData(62, 31, 9)]
    public void BrentonsProverbsTwentyFourStandsBesideTheHebrewItCarries(
        int verse,
        int standardChapter,
        int standardVerse)
    {
        Placed(brenton.Edition, Proverbs, 24, verse)
            .Should().Be(new CanonicalReference(Proverbs, standardChapter, standardVerse));
    }

    /// <summary>
    /// Brenton runs the killing of the first ram into the laying on of hands, divides the burning of
    /// it differently and splits the anointing at 8:30, so the verses between stand one ahead.
    /// </summary>
    [Fact]
    public void BrentonsLeviticusEightIsPlacedByItsOwnDivision()
    {
        brenton.Edition.Resolve(Leviticus, 8, 18).Should()
            .Equal(new CanonicalReference(Leviticus, 8, 18), new CanonicalReference(Leviticus, 8, 19));
        Placed(brenton.Edition, Leviticus, 8, 25).Should().Be(new CanonicalReference(Leviticus, 8, 26));
        Placed(brenton.Edition, Leviticus, 8, 29).Should().Be(new CanonicalReference(Leviticus, 8, 30));
        Placed(brenton.Edition, Leviticus, 8, 30).Should().Be(new CanonicalReference(Leviticus, 8, 30));
    }

    /// <summary>
    /// The Greek names the ostrich, the owl and the gull before the raven, which the Hebrew names
    /// first, and Brenton puts the raven with the hawk. Both chapters hold 47 verses, so only the
    /// birds say the verses stand apart. The raven and the hawk stand with the other birds, where
    /// Swete's hawk stands too, and cover the Hebrew's raven.
    /// </summary>
    [Fact]
    public void BrentonsBirdsStandBesideTheHebrewThatNamesThem()
    {
        brenton.Edition.Resolve(Leviticus, 11, 15).Should().Equal(new CanonicalReference(Leviticus, 11, 16));
        brenton.Edition.Resolve(Leviticus, 11, 16).Should()
            .Equal(new CanonicalReference(Leviticus, 11, 16), new CanonicalReference(Leviticus, 11, 15));
        Placed(brenton.Edition, Leviticus, 11, 17).Should().Be(new CanonicalReference(Leviticus, 11, 17));
    }

    /// <summary>
    /// Brenton's Greek is recognised as his, and stands its Sirach and its song where the words are:
    /// his Sirach 34 is the standard's 31, his 33:7 spans the standard's 36:6 and 36:7, and his 3:72a
    /// is the song's forty-fifth verse, after the thirty of Daniel 3.
    /// </summary>
    [Fact]
    public void BrentonsSirachAndSongStandWhereTheirWordsAre()
    {
        brenton.Edition.Resolve(Sirach, 34, 1, lettered: false, string.Empty).Should()
            .Equal(new CanonicalReference(Sirach, 31, 1));
        brenton.Edition.Resolve(Sirach, 33, 7, lettered: false, string.Empty).Should()
            .Equal(new CanonicalReference(Sirach, 36, 7), new CanonicalReference(Sirach, 36, 6));
        brenton.Edition.Resolve(Daniel, 3, 72, lettered: true, "a").Should()
            .Equal(new CanonicalReference(Daniel, 3, 75));
        Placed(brenton.Edition, Daniel, 3, 91).Should().Be(new CanonicalReference(Daniel, 3, 24));
    }

    private static CanonicalReference Placed(VersificationFrame frame, int book, int chapter, int verse) =>
        frame.Resolve(book, chapter, verse)[0];
}

/// <summary>
/// The same end of Malachi in Swete, who numbers it 4:4 to 4:6 under another of the data's schemes
/// and prints it in the same order as Brenton.
/// </summary>
[Trait(TestCategory.Name, TestCategory.Corpus)]
public class SweteMalachiTests
{
    private const int Malachi = 39;

    [Fact]
    public void EachVerseStandsBesideItsOwnWords()
    {
        var malachi = SweteTextSource.Read(TestResources.SweteFolder).Books
            .Single(book => book.CanonicalOrdinal == Malachi);
        var frame = TvtmsReader.Read(TestResources.Tvtms).Frame(
            Versification.Septuagint,
            EditionShape.Of(
            [
                .. from chapter in malachi.Chapters
                   from verse in chapter.Verses
                   select (Malachi, chapter.Number, verse.Number, verse.Label,
                       verse.Words.Sum(word => word.Surface.Length)),
            ]));

        frame.Resolve(Malachi, 4, 4)[0].Should().Be(new CanonicalReference(Malachi, 4, 5));
        frame.Resolve(Malachi, 4, 5)[0].Should().Be(new CanonicalReference(Malachi, 4, 6));
        frame.Resolve(Malachi, 4, 6)[0].Should().Be(new CanonicalReference(Malachi, 4, 4));
    }

    /// <summary>A correction that no longer finds its row is about a rule the data has stopped stating.</summary>
    [Fact]
    public void EveryCorrectionStillFindsTheRuleItCorrects()
    {
        TvtmsReader.Read(TestResources.Tvtms).Corrected.Should().BeEquivalentTo(TvtmsCorrections.All);
    }

    /// <summary>
    /// A supplement that stands apart from the data's passage for the same verses competes with it
    /// rather than replacing it, and the verses end up placed twice.
    /// </summary>
    [Fact]
    public void EverySupplementJoinsThePassageItWasWrittenFor()
    {
        TvtmsReader.Read(TestResources.Tvtms).Supplemented.Should()
            .BeEquivalentTo(TvtmsSupplements.All.Where(supplement => supplement.Joins is not null));
    }

    /// <summary>
    /// A supplement that stands apart from a passage of the data placing the same verses is chosen
    /// separately from it, and the verse is given the addresses of both.
    /// </summary>
    [Fact]
    public void NoSupplementStandsApartFromAPassageThatPlacesTheSameVerses()
    {
        TvtmsReader.Read(TestResources.Tvtms).Competing.Should().BeEmpty();
    }
}

/// <summary>Swete's whole Old Testament, read once.</summary>
public sealed class SweteEdition
{
    private readonly Lazy<VersificationFrame> edition;

    public SweteEdition()
    {
        edition = new Lazy<VersificationFrame>(() => TvtmsReader.Read(TestResources.Tvtms)
            .Frame(Versification.Septuagint, EditionShape.Of(Verses)));
    }

    internal IReadOnlyList<(int Book, int Chapter, int Number, string Label, int Length)> Verses { get; } =
    [
        .. from book in SweteTextSource.Read(TestResources.SweteFolder).Books
           from chapter in book.Chapters
           from verse in chapter.Verses
           select (book.CanonicalOrdinal, chapter.Number, verse.Number, verse.Label,
               verse.Words.Sum(word => word.Surface.Length)),
    ];

    internal VersificationFrame Edition => edition.Value;
}

/// <summary>
/// Swete, placed passage by passage where its words are. Each of these stood beside a Hebrew verse
/// that says something else.
/// </summary>
[Trait(TestCategory.Name, TestCategory.Corpus)]
public class SweteFrameTests(SweteEdition swete) : IClassFixture<SweteEdition>
{
    private const int Exodus = 2;

    private const int Leviticus = 3;

    private const int Joshua = 6;

    private const int SecondSamuel = 10;

    private const int FirstChronicles = 13;

    private const int Nehemiah = 16;

    private const int Proverbs = 20;

    private const int Jeremiah = 24;

    private const int Daniel = 27;

    private const int Sirach = 72;

    /// <summary>
    /// The data writes the undivided form of a scheme as the rows that differ from the divided one,
    /// so an edition that prints Exodus 38:27 whole answers to three rows about that verse and to the
    /// Greek scheme's renumbering of chapter 40 as well. Taking the three alone left the chapter at
    /// its own numbers, two verses before its words.
    /// </summary>
    [Theory]
    [InlineData(13, 15)]
    [InlineData(32, 38)]
    public void AnUndividedSchemeIsTheDividedOneWithItsOwnRowsInstead(int verse, int standard)
    {
        Placed(Exodus, 40, verse).Should().Be(new CanonicalReference(Exodus, 40, standard));
    }

    /// <summary>
    /// Where no scheme's tests can all be satisfied, the edition follows the one whose tests it
    /// answers most: Swete's 1 Chronicles 6 is the English chapter, its Nehemiah 4 the second Greek
    /// scheme and its Daniel 3:98-100 the Latin, each failing a test the others fail too.
    /// </summary>
    [Theory]
    [InlineData(FirstChronicles, 6, 2, 6, 2)]
    [InlineData(Nehemiah, 4, 1, 4, 1)]
    [InlineData(Daniel, 3, 98, 4, 1)]
    public void WhereNoSchemeHoldsTheOneThatHoldsMostIsTaken(
        int book,
        int chapter,
        int verse,
        int standardChapter,
        int standardVerse)
    {
        Placed(book, chapter, verse).Should().Be(new CanonicalReference(book, standardChapter, standardVerse));
    }

    [Fact]
    public void AmmonIsJeremiahThirtyOneToFive()
    {
        Placed(Jeremiah, 30, 1).Should().Be(new CanonicalReference(Jeremiah, 49, 1));
        Placed(Jeremiah, 30, 5).Should().Be(new CanonicalReference(Jeremiah, 49, 5));
        Placed(Jeremiah, 30, 6).Should().Be(new CanonicalReference(Jeremiah, 49, 28));
    }

    [Theory]
    [InlineData(18, 23, 19, 3)]
    [InlineData(19, 1, 19, 4)]
    [InlineData(19, 26, 19, 29)]
    [InlineData(20, 10, 20, 20)]
    [InlineData(20, 13, 20, 10)]
    [InlineData(20, 24, 20, 30)]
    [InlineData(24, 24, 30, 1)]
    [InlineData(24, 38, 24, 23)]
    [InlineData(24, 50, 30, 15)]
    [InlineData(24, 77, 31, 9)]
    [InlineData(29, 28, 31, 10)]
    [InlineData(29, 43, 31, 26)]
    [InlineData(29, 44, 31, 25)]
    public void SwetesProverbsStandBesideTheHebrewTheyCarry(
        int chapter,
        int verse,
        int standardChapter,
        int standardVerse)
    {
        Placed(Proverbs, chapter, verse)
            .Should().Be(new CanonicalReference(Proverbs, standardChapter, standardVerse));
    }

    [Theory]
    [InlineData(SecondSamuel, 18, 33, 18, 33)]
    [InlineData(SecondSamuel, 19, 1, 19, 1)]
    [InlineData(SecondSamuel, 19, 42, 19, 42)]
    [InlineData(Joshua, 9, 2, 9, 2)]
    [InlineData(Joshua, 9, 3, 8, 30)]
    [InlineData(Joshua, 9, 9, 9, 3)]
    [InlineData(Joshua, 9, 33, 9, 27)]
    public void SwetesOwnNumberingIsPlacedByItsWords(
        int book,
        int chapter,
        int verse,
        int standardChapter,
        int standardVerse)
    {
        swete.Edition.Resolve(book, chapter, verse).Should()
            .Equal(new CanonicalReference(book, standardChapter, standardVerse));
    }

    /// <summary>
    /// Swete's song stands after the thirty verses of Daniel 3 by the verse of it each prints, and his
    /// Sirach 33-36 at the standard's verses.
    /// </summary>
    [Theory]
    [InlineData(Daniel, 3, 24, 3, 31)]
    [InlineData(Daniel, 3, 69, 3, 75)]
    [InlineData(Daniel, 3, 91, 3, 24)]
    [InlineData(Sirach, 33, 25, 33, 16)]
    [InlineData(Sirach, 34, 11, 34, 10)]
    [InlineData(Sirach, 36, 31, 36, 26)]
    public void SwetesSongAndSirachStandWhereTheirWordsAre(
        int book,
        int chapter,
        int verse,
        int standardChapter,
        int standardVerse)
    {
        swete.Edition.Resolve(book, chapter, verse, lettered: false, string.Empty)[0]
            .Should().Be(new CanonicalReference(book, standardChapter, standardVerse));
    }

    /// <summary>Swete prints no raven, so nothing of it stands beside the Hebrew's.</summary>
    [Fact]
    public void SwetesBirdsStandBesideTheHebrewThatNamesThem()
    {
        swete.Edition.Resolve(Leviticus, 11, 15).Should().Equal(new CanonicalReference(Leviticus, 11, 16));
        swete.Edition.Resolve(Leviticus, 11, 16).Should().Equal(new CanonicalReference(Leviticus, 11, 16));
    }

    private CanonicalReference Placed(int book, int chapter, int verse) =>
        swete.Edition.Resolve(book, chapter, verse)[0];
}

/// <summary>
/// The Reina-Valera as eBible publishes it: the Spanish division, numbered to the English count, so
/// the verse the Spanish moved to the next chapter is left empty and that chapter runs early.
/// </summary>
[Trait(TestCategory.Name, TestCategory.Corpus)]
public class ReinaValeraFrameTests(Ebible ebible) : IClassFixture<Ebible>
{
    private const int Numbers = 4;

    private const int FirstSamuel = 9;

    private const int FirstKings = 11;

    private const int Job = 18;

    private const int Jonah = 32;

    [Theory]
    [InlineData(Numbers, 13, 1, 12, 16)]
    [InlineData(Numbers, 13, 2, 13, 1)]
    [InlineData(Numbers, 30, 1, 29, 40)]
    [InlineData(FirstSamuel, 24, 1, 23, 29)]
    [InlineData(FirstKings, 22, 44, 22, 43)]
    [InlineData(Job, 39, 1, 38, 39)]
    [InlineData(Job, 40, 1, 40, 6)]
    [InlineData(Jonah, 2, 1, 1, 17)]
    public void AChapterThatOpensEarlyStandsBesideItsWords(
        int book,
        int chapter,
        int verse,
        int standardChapter,
        int standardVerse)
    {
        EnglishEditions.Frame(ebible.ReinaValera).Resolve(book, chapter, verse)[0].Should()
            .Be(new CanonicalReference(book, standardChapter, standardVerse));
    }

    /// <summary>
    /// The chapter's last verse holds what the English count had no room for, and stands at every
    /// address it carries rather than being split by this project.
    /// </summary>
    [Fact]
    public void TheLastVerseOfTheChapterSpansWhatItHolds()
    {
        var edition = EnglishEditions.Frame(ebible.ReinaValera);

        edition.Resolve(Numbers, 30, 16).Should()
            .Equal(new CanonicalReference(Numbers, 30, 15), new CanonicalReference(Numbers, 30, 16));
        edition.Resolve(Job, 39, 30).Should().Equal(
        [
            .. Enumerable.Range(27, 4).Select(verse => new CanonicalReference(Job, 39, verse)),
            .. Enumerable.Range(1, 5).Select(verse => new CanonicalReference(Job, 40, verse)),
        ]);
    }

    /// <summary>Luther comes from the same publisher in the English numbering, and nothing here moves it.</summary>
    [Theory]
    [InlineData(Numbers, 13, 1)]
    [InlineData(Job, 39, 30)]
    [InlineData(Jonah, 2, 1)]
    public void AnEditionInTheEnglishNumberingIsNotMoved(int book, int chapter, int verse)
    {
        EnglishEditions.Frame(ebible.Luther).Resolve(book, chapter, verse).Should()
            .Equal(new CanonicalReference(book, chapter, verse));
    }
}

/// <summary>
/// Kulish's Bible, which divides some forty chapters in its own way while declaring the English
/// numbering. Each of these stood beside an English and a Hebrew verse that says something else.
/// </summary>
[Trait(TestCategory.Name, TestCategory.Corpus)]
public class KulishFrameTests(Kulish kulish) : IClassFixture<Kulish>
{
    private const int Genesis = 1;

    private const int Leviticus = 3;

    private const int Judges = 7;

    private const int SecondSamuel = 10;

    private const int Job = 18;

    private const int Psalms = 19;

    [Theory]
    [InlineData(Genesis, 3, 2, 3, 3)]
    [InlineData(Genesis, 3, 23, 3, 24)]
    [InlineData(Leviticus, 5, 20, 6, 1)]
    [InlineData(Leviticus, 6, 1, 6, 8)]
    [InlineData(Judges, 20, 22, 20, 23)]
    [InlineData(Judges, 20, 23, 20, 22)]
    [InlineData(SecondSamuel, 2, 6, 2, 5)]
    [InlineData(Job, 39, 31, 40, 1)]
    [InlineData(Job, 40, 20, 41, 1)]
    [InlineData(Job, 41, 26, 41, 34)]
    [InlineData(Psalms, 60, 3, 60, 1)]
    public void AVerseKulishNumbersApartStandsBesideItsWords(
        int book,
        int chapter,
        int verse,
        int standardChapter,
        int standardVerse)
    {
        EnglishEditions.Frame(kulish.Source).Resolve(book, chapter, verse)[0].Should()
            .Be(new CanonicalReference(book, standardChapter, standardVerse));
    }

    /// <summary>
    /// Two English verses printed as one stand at both addresses, and Psalm 60's two title verses
    /// are the title, as the Hebrew counts them.
    /// </summary>
    [Fact]
    public void AVerseThatHoldsMoreThanOneStandsAtEach()
    {
        var edition = EnglishEditions.Frame(kulish.Source);

        edition.Resolve(Genesis, 3, 1).Should()
            .Equal(new CanonicalReference(Genesis, 3, 1), new CanonicalReference(Genesis, 3, 2));
        edition.Resolve(Leviticus, 6, 22).Should()
            .Equal(new CanonicalReference(Leviticus, 6, 29), new CanonicalReference(Leviticus, 6, 30));
        edition.Resolve(Psalms, 60, 2).Should()
            .Equal(new CanonicalReference(Psalms, 60, CanonicalReference.TitleVerse));
    }
}

/// <summary>The Synodal and the King James as bible4u publishes them, read once.</summary>
public sealed class Bible4u
{
    internal TextSource Synodal { get; } =
        Bible4uTextSource.Read(TestResources.Bible4u("RUSV"), "RUSV");

    internal TextSource KingJames { get; } =
        Bible4uTextSource.Read(TestResources.Bible4u("KJV"), "KJV");
}

/// <summary>
/// The Synodal, renumbered by bible4u to the English chapters and verse counts, where it keeps its
/// own division inside a chapter. Each of these stood beside an English and a Hebrew verse that says
/// something else.
/// </summary>
[Trait(TestCategory.Name, TestCategory.Corpus)]
public class SynodalFrameTests(Bible4u bible4u) : IClassFixture<Bible4u>
{
    private const int Esther = 17;

    private const int Psalms = 19;

    private const int SongOfSongs = 22;

    private const int Isaiah = 23;

    private const int Revelation = 66;

    [Theory]
    [InlineData(SongOfSongs, 1, 1, 1, 2)]
    [InlineData(SongOfSongs, 1, 15, 1, 16)]
    [InlineData(Psalms, 90, 3, 90, 2)]
    [InlineData(Esther, 1, 7, 1, 6)]
    [InlineData(Isaiah, 3, 20, 3, 21)]
    [InlineData(Isaiah, 3, 24, 3, 25)]
    [InlineData(Revelation, 20, 8, 20, 9)]
    public void AVerseTheSynodalDividesApartStandsBesideItsWords(
        int book,
        int chapter,
        int verse,
        int standardChapter,
        int standardVerse)
    {
        EnglishEditions.Frame(bible4u.Synodal).Resolve(book, chapter, verse)[0].Should()
            .Be(new CanonicalReference(book, standardChapter, standardVerse));
    }

    /// <summary>
    /// A verse holding two English verses stands at both, two verses holding one stand at it, and
    /// Psalm 90's title, printed as a verse of its own, stands with the verse the Hebrew prints it in.
    /// </summary>
    [Fact]
    public void AVerseThatHoldsMoreOrLessThanOneStandsWhereItsWordsAre()
    {
        var edition = EnglishEditions.Frame(bible4u.Synodal);

        edition.Resolve(Isaiah, 3, 19).Should()
            .Equal(new CanonicalReference(Isaiah, 3, 19), new CanonicalReference(Isaiah, 3, 20));
        edition.Resolve(Revelation, 20, 7).Should()
            .Equal(new CanonicalReference(Revelation, 20, 7), new CanonicalReference(Revelation, 20, 8));
        edition.Resolve(SongOfSongs, 1, 16).Should().Equal(new CanonicalReference(SongOfSongs, 1, 17));
        edition.Resolve(SongOfSongs, 1, 17).Should().Equal(new CanonicalReference(SongOfSongs, 1, 17));
        edition.Resolve(Psalms, 90, 1).Should().Equal(new CanonicalReference(Psalms, 90, 1));
        edition.Resolve(Psalms, 90, 2).Should().Equal(new CanonicalReference(Psalms, 90, 1));
    }

    /// <summary>The King James comes from the same publisher and file format, and nothing here moves it.</summary>
    [Theory]
    [InlineData(SongOfSongs, 1, 1)]
    [InlineData(Psalms, 90, 2)]
    [InlineData(Isaiah, 3, 20)]
    [InlineData(Revelation, 20, 8)]
    public void TheKingJamesIsNotMoved(int book, int chapter, int verse)
    {
        EnglishEditions.Frame(bible4u.KingJames).Resolve(book, chapter, verse).Should()
            .Equal(new CanonicalReference(book, chapter, verse));
    }
}

/// <summary>An English-numbered edition's frame, built once: the whole Bible's shape is read for it.</summary>
internal static class EnglishEditions
{
    private static readonly Lazy<VersificationRules> Rules = new(() => TvtmsReader.Read(TestResources.Tvtms));

    private static readonly ConcurrentDictionary<TextSource, VersificationFrame> Frames = new();

    public static VersificationFrame Frame(TextSource source) => Frames.GetOrAdd(source, Place);

    private static VersificationFrame Place(TextSource source) =>
        Rules.Value.Frame(
            Versification.English,
            EditionShape.Of(
            [
                .. from book in source.Books
                   from chapter in book.Chapters
                   from verse in chapter.Verses
                   select (book.CanonicalOrdinal, chapter.Number, verse.Number, verse.Label,
                       verse.Words.Sum(word => word.Surface.Length)),
            ]));
}
