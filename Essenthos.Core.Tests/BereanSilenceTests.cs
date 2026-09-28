using Essenthos.Core.Berean;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Links;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// The rows of the Berean tables with no English of their own, and what each says. A dash is a word
/// the English does not render; <c>vvv</c> is a word rendered together with the word whose English
/// follows, as the <em>not</em> of <em>does not love</em> is the <em>μὴ</em> before
/// <em>ἀγαπῶν</em>; a row with no English says nothing.
/// </summary>
public class BereanSilenceTests
{
    /// <summary>1 John 4:8, as the table has it: <em>ὁ μὴ ἀγαπῶν οὐκ ἔγνω τὸν Θεόν</em>.</summary>
    [Fact]
    public void AWordMarkedVvvStandsInTheLinkOfTheWordWhoseEnglishFollows()
    {
        var rows = new[]
        {
            Row(1, 1, "ὁ", " Whoever "),
            Row(2, 2, "μὴ", " vvv "),
            Row(3, 3, "ἀγαπῶν", " does not love "),
            Row(4, 4, "οὐκ", " vvv "),
            Row(5, 5, "ἔγνω", " does not know "),
            Row(6, 6, "τὸν", " - "),
            Row(7, 7, "Θεόν", " God "),
        };
        var silences = new BereanLinkLoader.Silences();

        var drafts = Pair(rows, "Whoever does not love does not know God", silences);

        drafts.Should().ContainSingle(draft => draft.Relation == LinkRelation.Omits)
            .Which.To.Should().Equal(6);
        drafts.Where(draft => draft.Relation == LinkRelation.Renders)
            .Select(draft => string.Join(',', draft.To.Order()))
            .Should().Equal("1", "2,3", "4,5", "7");
        (silences.Joined, silences.Absent, silences.Moved, silences.Unmarked).Should().Be((2, 1, 0, 0));
    }

    /// <summary>
    /// The English order decides which word a <c>vvv</c> joins, and a run of them joins the same one:
    /// Luke 22:68's <em>οὐ μὴ ἀποκριθῆτε</em> is <em>you will not answer</em>, and a word whose
    /// English comes first in the sentence can be the one joined though it stands later in the Greek.
    /// </summary>
    [Fact]
    public void ARunOfVvvJoinsTheNextRenderingInTheEnglishOrder()
    {
        var rows = new[]
        {
            Row(1, 3, "ἀποκριθῆτε", " you will not answer "),
            Row(2, 1, "οὐ", " vvv "),
            Row(3, 2, "μὴ", " vvv "),
        };
        var silences = new BereanLinkLoader.Silences();

        var drafts = Pair(rows, "you will not answer", silences);

        drafts.Should().ContainSingle().Which.To.Order().Should().Equal(1, 2, 3);
        silences.Joined.Should().Be(2);
    }

    /// <summary>
    /// A <c>vvv</c> with no rendering after it to join is rendered somewhere the table does not say,
    /// like an ellipsis; a row with no English at all states nothing. Neither is an absence.
    /// </summary>
    [Fact]
    public void NeitherAnUnjoinedVvvNorAnEmptyRowIsAnAbsence()
    {
        var rows = new[]
        {
            Row(1, 1, "λέγει", " says "),
            Row(2, 2, "αὐτῷ", string.Empty),
            Row(3, 3, "δὲ", " vvv "),
        };
        var silences = new BereanLinkLoader.Silences();

        var drafts = Pair(rows, "says", silences);

        drafts.Should().ContainSingle(draft => draft.Relation == LinkRelation.Renders);
        drafts.Should().NotContain(draft => draft.Relation == LinkRelation.Omits);
        (silences.Moved, silences.Unmarked, silences.Absent).Should().Be((1, 1, 0));
    }

    private static List<BereanLinkLoader.Draft> Pair(BereanRow[] rows, string english, BereanLinkLoader.Silences silences)
    {
        var ours = english.Split(' ')
            .Select((word, at) => new BereanLinkLoader.Word(100 + at, 1, word, null))
            .ToList();
        List<List<long>> witness = [.. rows.Select(row => new List<long> { (long)row.OriginalOrder })];
        var drafts = new List<BereanLinkLoader.Draft>();

        BereanLinkLoader.Pair(rows, ours, witness, drafts, silences).Should().BeTrue();
        return drafts;
    }

    private static BereanRow Row(double order, int english, string greek, string rendering) =>
        new(order, english, 1, "Greek", greek, greek, string.Empty, "1 John 4:8", rendering);
}
