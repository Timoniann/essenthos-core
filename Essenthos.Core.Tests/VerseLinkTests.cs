using Essenthos.Core.Loading.Links;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// Which verse of one text is which verse of another.
///
/// The whole decision is in the grouping: two verses correspond when they stand at the same
/// canonical address, and that relation is transitive, so a passage the two texts divide
/// differently is one statement about a set of verses rather than several statements that
/// contradict each other about which verse is which.
/// </summary>
public class VerseLinkTests
{
    private static Dictionary<(int, int, int), List<int>> At(params (int Verse, int[] Ids)[] rows) =>
        rows.ToDictionary(row => (1, 1, row.Verse), row => row.Ids.ToList());

    [Fact]
    public void PairsAVerseWithTheVerseAtItsAddress()
    {
        var alone = 0;
        var links = VerseLinkLoader.Components(
            At((1, [11]), (2, [12])), At((1, [21]), (2, [22])), ref alone);

        links.Should().HaveCount(2);
        links.Should().OnlyContain(link => link.From.Count == 1 && link.To.Count == 1);
        alone.Should().Be(0);
    }

    /// <summary>
    /// One text says in two verses what the other says in one. Both of the first text's verses
    /// stand at the second's single address, so the three belong to one statement.
    /// </summary>
    [Fact]
    public void JoinsAPassageTheTwoTextsDivideDifferently()
    {
        var alone = 0;
        var links = VerseLinkLoader.Components(
            At((1, [11, 12])), At((1, [21])), ref alone);

        links.Should().ContainSingle();
        links[0].From.Should().BeEquivalentTo([11, 12]);
        links[0].To.Should().BeEquivalentTo([21]);
    }

    /// <summary>
    /// The case a pairwise join gets wrong.
    ///
    /// Verse 11 stands at both addresses 1 and 2; verse 22 stands at address 2 alone. Taken two at
    /// a time this reads as *11 is 21*, *11 is 22* and *12 is 22* — three links that each say
    /// something the others deny. It is one statement about four verses.
    /// </summary>
    [Fact]
    public void FollowsTheCorrespondenceWhereItIsTransitive()
    {
        var alone = 0;
        var links = VerseLinkLoader.Components(
            new Dictionary<(int, int, int), List<int>>
            {
                [(1, 1, 1)] = [11],
                [(1, 1, 2)] = [11, 12],
            },
            At((1, [21]), (2, [22])),
            ref alone);

        links.Should().ContainSingle();
        links[0].From.Should().BeEquivalentTo([11, 12]);
        links[0].To.Should().BeEquivalentTo([21, 22]);
    }

    /// <summary>
    /// A verse the other text has nothing at is counted and not written. Writing it as a link would
    /// claim the other text omits the verse, and the frame states no such thing — the text may
    /// simply not have been placed there, as the deuterocanon is not.
    /// </summary>
    [Fact]
    public void CountsAVerseWithNoCounterpartRatherThanClaimingAnAbsence()
    {
        var alone = 0;
        var links = VerseLinkLoader.Components(
            At((1, [11]), (2, [12])), At((1, [21])), ref alone);

        links.Should().ContainSingle();
        links[0].From.Should().BeEquivalentTo([11]);
        alone.Should().Be(1);
    }

    [Fact]
    public void CountsAnUnansweredVerseOnEitherSide()
    {
        var alone = 0;
        VerseLinkLoader.Components(At((1, [11])), At((1, [21]), (9, [29])), ref alone);
        alone.Should().Be(1);
    }

    /// <summary>
    /// Philippians 1:16 and 1:17 stand in the opposite order in Nestle and in the Textus Receptus,
    /// so each text's verse 16 is at the other's 17. That is one statement about four verses, and
    /// it is what makes the 27 word links crossing that boundary legitimate rather than faults.
    /// </summary>
    [Fact]
    public void HoldsATranspositionAsOneStatement()
    {
        var alone = 0;
        var links = VerseLinkLoader.Components(
            At((16, [116]), (17, [117])),
            new Dictionary<(int, int, int), List<int>>
            {
                [(1, 1, 16)] = [216, 217],
                [(1, 1, 17)] = [216, 217],
            },
            ref alone);

        links.Should().ContainSingle();
        links[0].From.Should().BeEquivalentTo([116, 117]);
        links[0].To.Should().BeEquivalentTo([216, 217]);
        alone.Should().Be(0);
    }
}
