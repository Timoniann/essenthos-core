using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading;
using Essenthos.Core.Loading.Frame;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// The versification data's tests, on an edition made up here so each one can be asked on its own.
/// </summary>
public class VersificationTestTests
{
    private static readonly EditionShape Edition = EditionShape.Of(
    [
        (1, 1, 1, string.Empty, 40),
        (1, 1, 2, string.Empty, 10),
        (1, 1, 2, "b", 30),
        (1, 1, 3, string.Empty, 90),
    ]);

    [Theory]
    [InlineData("Gen.1:1=Exist", true)]
    [InlineData("Gen.1:9=Exist", false)]
    [InlineData("Gen.1:9=NotExist", true)]
    [InlineData("Gen.1:3=Last", true)]
    [InlineData("Gen.1:1=Last", false)]
    [InlineData("Gen.1:2.2=Exist", true)]
    [InlineData("Gen.1:1.2=Exist", false)]
    [InlineData("Gen.1:1=Exist & Gen.1:3=Last", true)]
    [InlineData("Gen.1:1=Exist & Gen.1:1=Last", false)]
    public void AConditionIsAnsweredByTheEdition(string cell, bool expected)
    {
        var conditions = VersificationTest.ParseAll(cell);

        conditions.Should().NotBeNull();
        conditions!.Answer(Edition).Should().Be(expected);
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

public class EditionFrameTests(BrentonEdition brenton) : IClassFixture<BrentonEdition>
{
    private const int Genesis = 1;

    private const int Exodus = 2;

    private const int Leviticus = 3;

    private const int Deuteronomy = 5;

    private const int Nehemiah = 16;

    private const int Esther = 17;

    private const int Jeremiah = 24;

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
    /// What the whole change amounts to. Reading the conditions moves 398 of Brenton's 28,597
    /// verses and leaves the other 99% exactly where the tradition put them — which is the shape
    /// this should have: the schemes agree almost everywhere, and the passages where they do not
    /// are the passages a reader is comparing.
    /// </summary>
    [Fact]
    public void ReadingTheConditionsMovesTheVersesTheSchemesDisagreeAbout()
    {
        var moved = brenton.Verses.Count(verse =>
            brenton.Tradition.Resolve(verse.Book, verse.Chapter, verse.Number, verse.Label.Length > 0)[0] !=
            brenton.Edition.Resolve(verse.Book, verse.Chapter, verse.Number, verse.Label.Length > 0)[0]);

        brenton.Verses.Should().HaveCount(28_597);
        moved.Should().Be(398);
    }

    private static CanonicalReference Placed(VersificationFrame frame, int book, int chapter, int verse) =>
        frame.Resolve(book, chapter, verse)[0];
}
