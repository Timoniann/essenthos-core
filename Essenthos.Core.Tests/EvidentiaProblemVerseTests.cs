using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Links.Evidentia;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// The owner's loop starts from this list, so its order is the claim: the verse a person should
/// look at first comes first, and the one flag that means "check the pairing, not the words" is
/// raised only where the evidence points at another verse.
/// </summary>
public sealed class EvidentiaProblemVerseTests
{
    private static readonly EvidentiaAddress Good = new(1, 1, 1);
    private static readonly EvidentiaAddress Weak = new(1, 1, 2);
    private static readonly EvidentiaAddress Elsewhere = new(1, 1, 3);
    private static readonly EvidentiaAddress Short = new(1, 1, 4);

    [Fact]
    public void AVerseWhoseWordsPointAtAnotherVerseIsFlaggedAndComesFirst()
    {
        var ranked = EvidentiaProblemVerses.Rank(
        [
            .. Words(Good, safe: 5),
            .. Words(Weak, unplaced: 5, review: 1),
            .. Words(Elsewhere, safe: 1, unplaced: 4, pulled: 4),
        ]);

        ranked.Select(verse => verse.Address).Should().Equal(Elsewhere, Weak, Good);
        ranked[0].MayNotCorrespond.Should().BeTrue();
        ranked[1].MayNotCorrespond.Should().BeFalse(
            "a verse the rules read badly is weak; nothing says its parallel is another verse");
        ranked[1].Badness.Should().BeApproximately((5 + EvidentiaProblemVerses.ReviewTierWeight) / 6, 1e-9);
        ranked[2].Badness.Should().Be(0);
    }

    [Fact]
    public void AReviewerMovesAVerseUpOrDownTheList()
    {
        var approved = Words(Weak, review: 4).Select(word => word with { Verdict = EvidentiaVerdict.Approved });
        var rejected = Words(Good, safe: 4).Select(word => word with { Verdict = EvidentiaVerdict.Rejected });

        var ranked = EvidentiaProblemVerses.Rank([.. approved, .. rejected]);

        ranked.Single(verse => verse.Address == Weak).Should().Match<EvidentiaProblemVerse>(verse =>
            verse.Confirmed == 4 && verse.Badness == 0);
        ranked.Single(verse => verse.Address == Good).Should().Match<EvidentiaProblemVerse>(verse =>
            verse.Unplaced == 4 && verse.Confirmed == 0 && verse.Badness == 1);
    }

    [Fact]
    public void AVerseTooShortToJudgeIsLeftOut()
    {
        var ranked = EvidentiaProblemVerses.Rank([.. Words(Short, unplaced: 2), .. Words(Weak, unplaced: 3)]);

        ranked.Select(verse => verse.Address).Should().Equal(Weak);
    }

    [Fact]
    public void AFunctionWordDoesNotCountAgainstItsVerse()
    {
        var function = new EvidentiaVerseWord(1, Good, Content: false, Proposed: false, Safe: false,
            EvidentiaAbstention.NoCandidate, PulledElsewhere: false, Verdict: null);

        var ranked = EvidentiaProblemVerses.Rank([.. Words(Good, safe: 3), function]);

        ranked.Single().Should().Match<EvidentiaProblemVerse>(verse => verse.ContentWords == 3 && verse.Badness == 0);
    }

    private static IEnumerable<EvidentiaVerseWord> Words(
        EvidentiaAddress address, int safe = 0, int review = 0, int unplaced = 0, int pulled = 0)
    {
        for (var i = 0; i < safe; i++)
        {
            yield return new EvidentiaVerseWord(1, address, true, true, true, null, false, null);
        }

        for (var i = 0; i < review; i++)
        {
            yield return new EvidentiaVerseWord(1, address, true, true, false, null, false, null);
        }

        for (var i = 0; i < unplaced; i++)
        {
            yield return new EvidentiaVerseWord(1, address, true, false, false, EvidentiaAbstention.Declined, i < pulled, null);
        }
    }
}
