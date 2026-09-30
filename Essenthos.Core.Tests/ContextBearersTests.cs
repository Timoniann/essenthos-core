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

    private const int SamuelsSon = 6;

    private const string Long = "H3083";

    private const string Short = "H3129";

    private static readonly HashSet<int> Jonathans = [SaulsSon, AbiatharsSon];

    /// <summary>Saul's son held under the short number, Abiathar's under the long, both called Jonathan.</summary>
    private static readonly NameBearers Names = new([
        new BearerName(SaulsSon, EntityKind.Person, Short, "Jonathan"),
        new BearerName(AbiatharsSon, EntityKind.Person, Long, "Jonathan"),
        new BearerName(Hezekiah, EntityKind.Person, "H2396", "Hezekiah"),
        new BearerName(SamuelsSon, EntityKind.Person, "H3100", "Joel"),
        new BearerName(SamuelsSon, EntityKind.Person, Short, "Jonathan"),
    ]);

    private static Attestation At(int entity, int chapter, int verse, int book = FirstSamuel, string number = Short) =>
        new(entity, new Address(book, chapter, verse), number);

    private static ContextBearers Context(params Attestation[] attestations) => new(attestations, Names);

    /// <summary>1 Samuel 20: Saul's son is named twice a few verses off and nobody else of his name in the book.</summary>
    [Fact]
    public void TheOneBearerNamedTwiceNearbyIsTheWord()
    {
        var context = Context(At(SaulsSon, 20, 1), At(SaulsSon, 20, 4));

        context.Bearer(new Address(FirstSamuel, 20, 9), Jonathans, Long).Should().Be(SaulsSon);
    }

    /// <summary>Once nearby is not enough on its own.</summary>
    [Fact]
    public void OneNearbyAttestationIsNotEnough()
    {
        var context = Context(At(SaulsSon, 20, 1));

        context.Bearer(new Address(FirstSamuel, 20, 9), Jonathans, Long).Should().BeNull();
    }

    /// <summary>Named often in the book, and once in the chapter next door, settles it without a near verse.</summary>
    [Fact]
    public void OftenInTheBookAndOnceNextDoorIsEnough()
    {
        var context = Context(
            At(SaulsSon, 13, 2), At(SaulsSon, 14, 1), At(SaulsSon, 14, 3), At(SaulsSon, 14, 4), At(SaulsSon, 19, 1));

        context.Bearer(new Address(FirstSamuel, 20, 30), Jonathans, Long).Should().Be(SaulsSon);
        context.Bearer(new Address(FirstSamuel, 31, 2), Jonathans, Long).Should().BeNull();
    }

    /// <summary>
    /// 1 Samuel writes Saul's son under one number in chapters 13 and 14 and under the other from
    /// chapter 18: named often enough in the book, and nobody else of the name, he is the word
    /// however far off; one short of that he is not.
    /// </summary>
    [Fact]
    public void NamedOftenEnoughInTheBookIsEnoughHoweverFar()
    {
        var often = Enumerable.Range(1, ContextBearers.AloneInBook).Select(verse => At(SaulsSon, 14, verse)).ToArray();

        Context(often).Bearer(new Address(FirstSamuel, 20, 30), Jonathans, Long).Should().Be(SaulsSon);
        Context(often[1..]).Bearer(new Address(FirstSamuel, 20, 30), Jonathans, Long).Should().BeNull();
    }

    /// <summary>
    /// Samuel's son is a candidate because he is called Jonathan too, and the book names him Joel:
    /// that is not a second Jonathan in the book, so Saul's son is still the word. Named as
    /// Jonathan, he is one.
    /// </summary>
    [Fact]
    public void ACandidateNamedByAnotherOfHisNamesIsNoRival()
    {
        HashSet<int> candidates = [SaulsSon, AbiatharsSon, SamuelsSon];
        var word = new Address(FirstSamuel, 20, 9);

        Context(At(SaulsSon, 20, 1), At(SaulsSon, 20, 4), At(SamuelsSon, 8, 2, number: "H3100"))
            .Bearer(word, candidates, Long).Should().Be(SaulsSon);
        Context(At(SaulsSon, 20, 1), At(SaulsSon, 20, 4), At(SamuelsSon, 8, 2))
            .Bearer(word, candidates, Long).Should().BeNull();
    }

    /// <summary>Nor is he support: two words that name a candidate by another of his names settle nothing.</summary>
    [Fact]
    public void ACandidateNamedByAnotherOfHisNamesIsNoSupport()
    {
        HashSet<int> candidates = [AbiatharsSon, SamuelsSon];

        Context(At(SamuelsSon, 20, 1, number: "H3100"), At(SamuelsSon, 20, 4, number: "H3100"))
            .Bearer(new Address(FirstSamuel, 20, 9), candidates, Long).Should().BeNull();
    }

    /// <summary>A book that names two of the bearers anywhere has not said which a bare name is.</summary>
    [Fact]
    public void ABookNamingTwoBearersSaysNothing()
    {
        var context = Context(At(SaulsSon, 20, 1), At(SaulsSon, 20, 4), At(AbiatharsSon, 2, 1, number: Long));

        context.Bearer(new Address(FirstSamuel, 20, 9), Jonathans, Long).Should().BeNull();
    }

    /// <summary>The word's own verse is never evidence for it, which is what makes the rule measurable.</summary>
    [Fact]
    public void TheWordsOwnVerseIsLeftOut()
    {
        var context = Context(At(SaulsSon, 20, 9), At(SaulsSon, 20, 9), At(SaulsSon, 20, 1));

        context.Bearer(new Address(FirstSamuel, 20, 9), Jonathans, Long).Should().BeNull();
    }

    /// <summary>Another book's words, and records that are not candidates, say nothing.</summary>
    [Fact]
    public void OnlyTheBooksOwnCandidatesCount()
    {
        var context = Context(
            At(AbiatharsSon, 20, 1, book: 10, number: Long), At(AbiatharsSon, 20, 4, book: 10, number: Long),
            At(Hezekiah, 20, 1, number: "H2396"), At(SaulsSon, 20, 2), At(SaulsSon, 20, 3));

        context.Bearer(new Address(FirstSamuel, 20, 9), Jonathans, Long).Should().Be(SaulsSon);
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
