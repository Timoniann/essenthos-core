using Essenthos.Core.Database.Entities;
using Essenthos.Core.Loading.Encyclopedia;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// The parts of the Annals load that decide what a row claims, tested against the real file.
///
/// Every one of these is a way of being confidently wrong rather than a way of failing: a citation
/// that resolves to the wrong testament, a year the source contradicts twice over, and a title that
/// reads as a dead man's words. None of them throws, and none is visible in a count.
/// </summary>
public sealed class UssherAnnalsTests
{
    private static readonly Lazy<BibleDataLoader.ReferenceTable> Frame =
        new(() => BibleDataLoader.ReferenceTable.Read(Folder));

    private static string Folder =>
        Path.GetDirectoryName(TestResources.Path("BibleData2026", "BibleData-Book.csv"))!;

    private static (int Book, int Chapter, int Verse, string Cited)? Anchor(string paragraph) =>
        UssherAnnalsLoader.Anchor(paragraph, Frame.Value, new Dictionary<string, int>(StringComparer.Ordinal));

    /// <summary>
    /// The Annals write <c>JUD</c> for Judges, and the shared table reads it as Jude. Jude has one
    /// chapter and Judges 1 is a chapter, so nine such citations do not fail — they resolve, into
    /// the New Testament, and would anchor a paragraph dated 1445 BC to an epistle. Read as Judges
    /// they anchor nothing, because nothing outside the New Testament does.
    /// </summary>
    [Fact]
    public void JudgesIsNotReadAsJude() =>
        Anchor("Hebron was taken by the tribe of Judah. (JUD 1:10)").Should().BeNull();

    /// <summary>
    /// Judith is <c>JUD</c> too, and its chapters are chapters Judges has. The marker beside the
    /// citation is the only thing that separates them, so a marked one is counted as a book the
    /// corpus cannot reach rather than resolved into one it can.
    /// </summary>
    [Fact]
    public void ACitationMarkedApocryphalIsNotReadAsTheBookSharingItsCode()
    {
        var unreached = new Dictionary<string, int>(StringComparer.Ordinal);

        UssherAnnalsLoader.Anchor(
            "A fuller description of the construction of it is in {Apc (JUD 1:1-16) where it is said",
            Frame.Value,
            unreached);

        unreached.Should().ContainKey("JUD");
    }

    [Fact]
    public void ARangeIsAnchoredOnItsFirstVerseAndQuotedWhole()
    {
        var anchor = Anchor("he went into Capernaum and healed the centurion's servant. (LUK 7:1-10)");

        anchor.Should().NotBeNull();
        (anchor!.Value.Book, anchor.Value.Chapter, anchor.Value.Verse).Should().Be((42, 7, 1));
        anchor.Value.Cited.Should().Be("LUK 7:1-10");
    }

    /// <summary>
    /// Two citations of one book run together read as one range unless the tail stops at a second
    /// chapter: <c>ACT 8:1, 11:19</c> is Acts 8:1 and Acts 11:19, not Acts 8 verses 1 to 11.
    /// </summary>
    [Fact]
    public void ASecondChapterDoesNotBecomeTheTailOfARange() =>
        Anchor("There arose a great persecution. (ACT 8:1, 11:19)")!.Value.Cited.Should().Be("ACT 8:1");

    [Fact]
    public void ACitationOfAVerseNobodyHasReadAnchorsNothing() =>
        Anchor("and so it happened. (MAT 28:99)").Should().BeNull();

    /// <summary>
    /// Ussher's anno mundi year begins in the autumn, so a Julian year falls in one of two of them
    /// and both readings are his.
    /// </summary>
    [Theory]
    [InlineData(4033)]
    [InlineData(4034)]
    public void EitherHalfOfTheStraddleIsHisYear(int anno) =>
        UssherAnnalsLoader.Reckoned(Row(anno), 30, 4003).Should().Be(anno);

    /// <summary>
    /// 118 paragraphs dated AD 33 carry anno mundi 4046 where the Gregorian and Julian Period
    /// columns beside them both give 4036. Writing 4046 dates the passion ten years late and
    /// writing 4036 is a repair nobody made, so neither is written.
    /// </summary>
    [Fact]
    public void AYearItsOwnColumnsContradictIsNotWritten() =>
        UssherAnnalsLoader.Reckoned(Row(4046), 33, 4003).Should().BeNull();

    private static Dictionary<string, string> Row(int anno) =>
        new(StringComparer.Ordinal) { ["am_year_only"] = anno.ToString() };

    [Fact]
    public void ATitleIsHisOwnOpeningSentenceWithTheClosingCitationTakenOff()
    {
        var (name, provenance, madeBy) = UssherAnnalsLoader.Title(
            "John's disciples and the Jews had a discussion about purifying. (JHN 3:25)", "6310", Titles());

        name.Should().Be("John's disciples and the Jews had a discussion about purifying.");
        provenance.Should().Be(EventNames.Quoted);
        madeBy.Should().BeNull();
    }

    /// <summary>
    /// A cut quotation is still a quotation, and the ellipsis says where his words stop.
    /// </summary>
    [Fact]
    public void ALongSentenceIsCutRatherThanRephrased()
    {
        var sentence = string.Join(" and ", Enumerable.Repeat("Antipater sent word to Herod at Rome", 8)) + ".";

        var (name, provenance, _) = UssherAnnalsLoader.Title(sentence, "1", Titles());

        name.Should().EndWith("…");
        name.Length.Should().BeLessThanOrEqualTo(121);
        sentence.Should().StartWith(name[..^1]);
        provenance.Should().Be(EventNames.Quoted);
    }

    /// <summary>
    /// A title written for the corpus is marked as such and says what wrote it, because a summary
    /// that reads as Ussher's own heading is a claim he never made.
    /// </summary>
    [Fact]
    public void AWrittenTitleSaysItIsNotHis()
    {
        var (name, provenance, madeBy) = UssherAnnalsLoader.Title(
            "John's disciples and the Jews had a discussion about purifying.", "6310",
            Titles(("6310", "A dispute about purifying")));

        name.Should().Be("A dispute about purifying");
        provenance.Should().Be(EventNames.Generated);
        madeBy.Should().NotBeNull().And.Contain("not Ussher's").And.Contain("a model on 2026-09-04");
    }

    private static Dictionary<string, (string Title, string By)> Titles(
        params (string Number, string Title)[] written) =>
        written.ToDictionary(w => w.Number, w => (w.Title, "a model on 2026-09-04"), StringComparer.Ordinal);
}
