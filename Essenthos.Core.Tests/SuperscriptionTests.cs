using Essenthos.Core.Loading;
using Essenthos.Core.XmlBible;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// Which verses of the two Slavic translations hold a psalm's superscription, which is the one
/// judgement the whole of the cross-verse work turns on.
///
/// <para>
/// That a psalm is numbered one verse apart in the Hebrew is a fact about the Hebrew and says
/// nothing about what any Russian verse holds, so the answer is read out of what the two files say
/// about themselves. Both write the address their own pages give what follows a marker — the
/// Synodal's Psalm 3:1 opens with the superscription and only then writes "(3-2)" — and the Synodal
/// additionally wraps a superscription in "^^". Neither is a formula anybody matched against the
/// text.
/// </para>
///
/// <para>
/// The counts below are the measurement. They are asserted here so that a change to the tokeniser
/// or to either file is reported as a change to the corpus rather than discovered as a psalm
/// quietly losing its title.
/// </para>
/// </summary>
public class SuperscriptionTests
{
    private const int Psalms = 19;

    /// <summary>
    /// The Synodal's Psalm 3:1, which holds the superscription the Hebrew numbers 3:1 and the body
    /// the Hebrew numbers 3:2, and says so by writing "(3-2)" between them.
    /// </summary>
    private const string Psalm31 =
        "^^Псалом Давида, когда он бежал от Авессалома, сына своего.^^ (3-2) Господи! как умножились " +
        "враги мои! Многие восстают на меня";

    [Fact]
    public void AVerseOpeningWithASuperscriptionOpensBeforeTheAddressItStates()
    {
        VerseWords.OpensBeforeItsStatedAddress(Psalm31).Should().BeTrue();
        VerseWords.MarksASuperscription(Psalm31).Should().BeTrue();
    }

    /// <summary>
    /// The Synodal's Psalm 115:1 continues the psalm it numbers 113 and opens with "(113-9)". The
    /// number is greater than one and there is nothing before the marker, so the verse begins where
    /// the edition's own verse begins and holds no title.
    /// </summary>
    [Fact]
    public void AVerseOpeningWithItsAddressHoldsNothingBeforeIt()
    {
        VerseWords.OpensBeforeItsStatedAddress("(113-9) Господь помнит нас, благословляет нас")
            .Should().BeFalse();
    }

    /// <summary>
    /// Psalm 119:1 of the Synodal states its own address and nothing stands before it either — the
    /// whole verse is the edition's 118:1. A marker naming verse one can never have an earlier
    /// verse of the same psalm in front of it.
    /// </summary>
    [Fact]
    public void AnAddressNamingTheFirstVerseLeavesNoRoomForATitle()
    {
        VerseWords.OpensBeforeItsStatedAddress("(118-1) Блаженны непорочные в пути.")
            .Should().BeFalse();
    }

    [Fact]
    public void AVerseTheEditionSaysNothingAboutOpensNowhereElse()
    {
        VerseWords.OpensBeforeItsStatedAddress("Блажен муж, который не ходит на совет нечестивых")
            .Should().BeFalse();
        VerseWords.MarksASuperscription("Блажен муж, который не ходит на совет нечестивых")
            .Should().BeFalse();
    }

    /// <summary>
    /// The Synodal marks a superscription in 120 places and every one of them is the first verse of
    /// a psalm. Fifty-seven of those psalms keep the title inside verse one exactly as the Hebrew
    /// does and need nothing said about them; the rest are the ones this work is about.
    /// </summary>
    [Fact]
    public void TheSynodalMarksItsSuperscriptionsAndOnlyInAPsalmsFirstVerse()
    {
        var marked = Marked("RUSV", verse => verse.MarksASuperscription);

        marked.Should().HaveCount(120);
        marked.Should().OnlyContain(place => place.Book == Psalms && place.Verse == 1);
    }

    /// <summary>
    /// Ohienko's Ukrainian carries the marker nowhere, so it is silent on this and the addresses it
    /// states are all there is to go on.
    /// </summary>
    [Fact]
    public void TheUkrainianMarksNoSuperscriptionAtAll()
    {
        Marked("UKR", verse => verse.MarksASuperscription).Should().BeEmpty();
    }

    /// <summary>
    /// Sixty-two psalms of the Synodal and sixty-one of the Ukrainian open before the address they
    /// state, and in both files every psalm that does is a first verse. The three verses elsewhere
    /// are ones the publisher merged in the middle of a chapter — 1 Samuel 20:42 and 3 John 1:14 in
    /// the Synodal, 3 John 1:14 and the Ukrainian's Psalm 65:13 — and no title stands anywhere near
    /// them. It is the frame that rules those out, by holding a title verse for a psalm and for
    /// nothing else.
    /// </summary>
    [Theory]
    [InlineData("RUSV", 64, 62)]
    [InlineData("UKR", 63, 61)]
    public void AVerseOpeningBeforeItsAddressIsAlmostAlwaysAPsalmsFirst(string translation, int all, int psalms)
    {
        var opening = Marked(translation, verse => verse.OpensBeforeItsStatedAddress);

        opening.Should().HaveCount(all);
        opening.Where(place => place.Book == Psalms && place.Verse == 1).Should().HaveCount(psalms);
    }

    /// <summary>
    /// The two signals against each other, on the one file that carries both: every psalm whose
    /// first verse opens before the address it states is also a psalm the Synodal marks a
    /// superscription in. Two statements the file makes independently, agreeing 62 times out of 62.
    ///
    /// That is what stands in for a precision measurement here. There is no gold file saying which
    /// Russian verses hold a title, so the check is that the file's two accounts of itself do not
    /// contradict each other — and the frame is a third: all 62 are psalms the Hebrew numbers the
    /// superscription as a verse of its own.
    /// </summary>
    [Fact]
    public void TheTwoSignalsAgreeWhereverBothSpeak()
    {
        var marked = Marked("RUSV", verse => verse.MarksASuperscription).ToHashSet();
        var opening = Marked("RUSV", verse => verse.OpensBeforeItsStatedAddress)
            .Where(place => place.Book == Psalms && place.Verse == 1);

        opening.Should().OnlyContain(place => marked.Contains(place));
    }

    private static List<(int Book, int Chapter, int Verse)> Marked(
        string translation,
        Func<VerseDraft, bool> holds) =>
    [
        .. Bible4uTextSource.Read(TestResources.Bible4u(translation), translation).Books
            .SelectMany(book => book.Chapters
                .SelectMany(chapter => chapter.Verses
                    .Where(holds)
                    .Select(verse => (book.CanonicalOrdinal, chapter.Number, verse.Number)))),
    ];
}
