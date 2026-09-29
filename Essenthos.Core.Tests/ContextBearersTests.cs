using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Encyclopedia;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// Which of a name's bearers a book means, read off the words of the book already settled: each rule
/// the reading has to keep, one case each.
/// </summary>
public sealed class ContextBearersTests
{
    private const int FirstSamuel = 9;

    private const int SaulsSon = 1;

    private const int AbiatharsSon = 2;

    private const int Hezekiah = 3;

    private static readonly HashSet<int> Jonathans = [SaulsSon, AbiatharsSon];

    private static Attestation At(int entity, int chapter, int verse, int book = FirstSamuel) =>
        new(entity, new Address(book, chapter, verse));

    /// <summary>1 Samuel 20: Saul's son is named twice a few verses off and nobody else of his name in the book.</summary>
    [Fact]
    public void TheOneBearerNamedTwiceNearbyIsTheWord()
    {
        var context = new ContextBearers([At(SaulsSon, 20, 1), At(SaulsSon, 20, 4)]);

        context.Bearer(new Address(FirstSamuel, 20, 9), Jonathans).Should().Be(SaulsSon);
    }

    /// <summary>Once nearby is not enough on its own.</summary>
    [Fact]
    public void OneNearbyAttestationIsNotEnough()
    {
        var context = new ContextBearers([At(SaulsSon, 20, 1)]);

        context.Bearer(new Address(FirstSamuel, 20, 9), Jonathans).Should().BeNull();
    }

    /// <summary>Named often in the book, and once in the chapter next door, settles it without a near verse.</summary>
    [Fact]
    public void OftenInTheBookAndOnceNextDoorIsEnough()
    {
        var context = new ContextBearers([
            At(SaulsSon, 13, 2), At(SaulsSon, 14, 1), At(SaulsSon, 14, 3), At(SaulsSon, 14, 4), At(SaulsSon, 19, 1),
        ]);

        context.Bearer(new Address(FirstSamuel, 20, 30), Jonathans).Should().Be(SaulsSon);
        context.Bearer(new Address(FirstSamuel, 31, 2), Jonathans).Should().BeNull();
    }

    /// <summary>A book that names two of the bearers anywhere has not said which a bare name is.</summary>
    [Fact]
    public void ABookNamingTwoBearersSaysNothing()
    {
        var context = new ContextBearers([At(SaulsSon, 20, 1), At(SaulsSon, 20, 4), At(AbiatharsSon, 2, 1)]);

        context.Bearer(new Address(FirstSamuel, 20, 9), Jonathans).Should().BeNull();
    }

    /// <summary>The word's own verse is never evidence for it, which is what makes the rule measurable.</summary>
    [Fact]
    public void TheWordsOwnVerseIsLeftOut()
    {
        var context = new ContextBearers([At(SaulsSon, 20, 9), At(SaulsSon, 20, 9), At(SaulsSon, 20, 1)]);

        context.Bearer(new Address(FirstSamuel, 20, 9), Jonathans).Should().BeNull();
    }

    /// <summary>Another book's words, and records that are not candidates, say nothing.</summary>
    [Fact]
    public void OnlyTheBooksOwnCandidatesCount()
    {
        var context = new ContextBearers([
            At(AbiatharsSon, 20, 1, book: 10), At(AbiatharsSon, 20, 4, book: 10), At(Hezekiah, 20, 1), At(SaulsSon, 20, 2),
            At(SaulsSon, 20, 3),
        ]);

        context.Bearer(new Address(FirstSamuel, 20, 9), Jonathans).Should().Be(SaulsSon);
    }

    /// <summary>
    /// Saul's son under the number Strong heads him by and the Jonathans under the other: a word of
    /// either number may be any of them, because they are called by one name.
    /// </summary>
    [Fact]
    public void TheCandidatesAreEveryRecordCalledByTheNumbersName()
    {
        var bearers = new NameBearers([
            new BearerName(SaulsSon, EntityKind.Person, "H3129", "Jonathan"),
            new BearerName(AbiatharsSon, EntityKind.Person, "H3083", "Jonathan"),
            new BearerName(Hezekiah, EntityKind.Person, "H2396", "Hezekiah"),
        ]);

        bearers.Of("H3083", "pers").Should().BeEquivalentTo([SaulsSon, AbiatharsSon]);
        bearers.Of("H2396", null).Should().BeEquivalentTo([Hezekiah]);
        bearers.Of("H9999", "pers").Should().BeEmpty();
    }

    /// <summary>BHSA's marking narrows the candidates to the kinds it allows; the Greek, marking nothing, keeps them all.</summary>
    [Fact]
    public void TheMarkingNarrowsTheKinds()
    {
        const int Hebron = 4;
        const int HebronTheMan = 5;
        var bearers = new NameBearers([
            new BearerName(Hebron, EntityKind.Place, "H2275", "Hebron"),
            new BearerName(HebronTheMan, EntityKind.Person, "H2275", "Hebron"),
        ]);

        bearers.Of("H2275", "topo").Should().BeEquivalentTo([Hebron]);
        bearers.Of("H2275", "pers,topo").Should().BeEquivalentTo([Hebron, HebronTheMan]);
        bearers.Of("H2275", null).Should().BeEquivalentTo([Hebron, HebronTheMan]);
    }
}
