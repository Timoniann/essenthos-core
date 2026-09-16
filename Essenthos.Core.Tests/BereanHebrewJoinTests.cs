using Essenthos.Core.Berean;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Links;
using Essenthos.Core.Utils;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// How the Berean tables' Hebrew words become BHSA's, which is by the letters and nothing else.
///
/// The two editions divide the same letters differently in both directions, and a verse the join
/// cannot account for is refused whole — so a division it does not understand loses every link of
/// the verse, silently. Ruth 1:1 and 1:2 were lost that way for want of <em>Bethlehem</em>.
/// </summary>
public class BereanHebrewJoinTests
{
    private static List<List<int>>? Join(string[] berean, string[] bhsa) =>
        BereanLinkLoader.Letters(
            [.. berean.Select(HebrewLetters.Of)],
            [.. bhsa.Select(HebrewLetters.Of)]);

    /// <summary>The division the join was written for: one Berean word, several BHSA words.</summary>
    [Fact]
    public void GivesOneBereanWordEveryBhsaWordItsLettersFallIn()
    {
        var runs = Join(["בָּאָ֑רֶץ", "וַיֵּ֨לֶךְ"], ["בָּ", "", "אָ֑רֶץ", "וַ", "יֵּ֨לֶךְ"]);

        runs.Should().BeEquivalentTo(new List<List<int>> { new() { 0, 1, 2 }, new() { 3, 4 } },
            options => options.WithStrictOrdering());
    }

    /// <summary>
    /// The other direction. BHSA writes בֵּית לֶחֶם as one word where the Westminster edition writes
    /// two, so both Berean words render that one word, and the preposition joined to the first stays
    /// with the first. This is Ruth 1:1.
    /// </summary>
    [Fact]
    public void GivesOneBhsaWordToEveryBereanWordThatSharesItsLetters()
    {
        var runs = Join(
            ["אִ֜ישׁ", "מִבֵּ֧ית", "לֶ֣חֶם", "יְהוּדָ֗ה"],
            ["אִ֜ישׁ", "מִ", "בֵּ֧ית לֶ֣חֶם", "יְהוּדָ֗ה"]);

        runs.Should().BeEquivalentTo(
            new List<List<int>> { new() { 0 }, new() { 1, 2 }, new() { 2 }, new() { 3 } },
            options => options.WithStrictOrdering());
    }

    /// <summary>
    /// The Westminster edition glues its paragraph marks to the word a section ends on, with a space
    /// or without one. The mark is not text and the verse is the same verse.
    /// </summary>
    [Theory]
    [InlineData("יִשְׂרָאֵֽ֑ל פ")]
    [InlineData("ה֑וּאס")]
    public void SetsAsideAParagraphMarkGluedInsideTheVerse(string marked)
    {
        var word = marked.StartsWith('י') ? "יִשְׂרָאֵֽל" : "הוּא";

        var runs = Join(["וַיִּשְׁמַ֖ע", marked, "וַיִּֽהְי֥וּ"], ["וַ", "יִּשְׁמַ֖ע", word, "וַ", "יִּֽהְי֥וּ"]);

        runs.Should().BeEquivalentTo(
            new List<List<int>> { new() { 0, 1 }, new() { 2 }, new() { 3, 4 } },
            options => options.WithStrictOrdering());
    }

    /// <summary>A samekh that is part of the word stays part of it.</summary>
    [Fact]
    public void KeepsAFinalSamekhThatIsALetter() =>
        Join(["סוּס", "וְרֹכְבוֹ"], ["סוּס", "וְ", "רֹכְבוֹ"]).Should().HaveCount(2);

    /// <summary>
    /// A ketiv against a qere is a different text, and nothing after the difference can be trusted to
    /// line up. The whole verse is refused rather than joined as far as it goes.
    /// </summary>
    [Fact]
    public void RefusesAVerseWhoseLettersDiffer() =>
        Join(["עֶ֥שֶׂר", "אַמֹּ֖ות", "אֹ֣רֶךְ"], ["עֶ֥שֶׂר", "אַמֹּ֖ת", "אֹ֣רֶךְ"]).Should().BeNull();

    /// <summary>A letterless word left at the end of the verse belongs to no Berean word.</summary>
    [Fact]
    public void LeavesATrailingLetterlessWordUnclaimed() =>
        Join(["בָּאָ֑רֶץ"], ["בָּ", "אָ֑רֶץ", ""])!.Single().Should().Equal(0, 1);

    /// <summary>
    /// Where two Berean words share one BHSA word and neither renders it, it is unrendered once. Where
    /// one of them renders it, it is not unrendered at all. Written twice, the corpus holds two links
    /// naming the same word, which the integrity check counts.
    /// </summary>
    [Theory]
    [InlineData(" - ", " - ", 1)]
    [InlineData(" Bethlehem ", " - ", 0)]
    public void SaysAWordTwoBereanWordsShareIsUnrenderedOnceOrNotAtAll(string first, string second, int omits)
    {
        var rows = new[] { Row(1, 1, first), Row(2, 2, second) };
        var ours = first.Trim() == "-" ? new List<BereanLinkLoader.Word>() : [new(100, 1, "Bethlehem", null)];
        List<List<long>> witness = [[7], [7]];
        var drafts = new List<BereanLinkLoader.Draft>();
        int absent = 0, moved = 0;

        BereanLinkLoader.Pair(rows, ours, witness, drafts, ref absent, ref moved).Should().BeTrue();

        drafts.Count(draft => draft.Relation == LinkRelation.Omits).Should().Be(omits);
    }

    private static BereanRow Row(double order, int english, string rendering) =>
        new(order, english, 1, "Hebrew", "בֵּית", "בֵּית", "H1035", "Ruth 1:1", rendering);
}
