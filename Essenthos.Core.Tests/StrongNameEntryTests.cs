using Essenthos.Core.Strong;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// Whom Strong heads a name for, read off his part of speech and his numbered clauses.
///
/// Every entry here is copied out of the lexicon this corpus loads, because the cases worth testing
/// are his own irregularities and not shapes anybody would invent: one entry that heads a man and
/// the valley named after him with a heading over each, one that heads a man and a city with no
/// heading at all, and two he parts as ordinary words while the encyclopedia files a person under
/// them.
///
/// The refusals are the tests worth having. A parse that reads a clause too generously does not
/// fail — it says a page is the man a dictionary numbered when the dictionary numbered four, which
/// no reader can see and every reader will repeat.
/// </summary>
public sealed class StrongNameEntryTests
{
    [Fact]
    public void OneManUnderOneNameIsOneNumberedClause()
    {
        StrongNameEntries.HeadsAName("n-pr-m", Sheshan).Should().BeTrue();

        var bearers = StrongNameEntries.Bearers("n-pr-m", Sheshan);

        bearers.Should().ContainSingle().Which.Kind.Should().Be(StrongNameKind.Person);
        bearers[0].Item.Should().Be(1);
        bearers[0].Says.Should().StartWith("a Judaite of the families of Hezron and Jerahmeel");
    }

    /// <summary>
    /// The headword line is the name and its meaning, not a bearer and not a heading. Folding it
    /// into the first clause would put <em>Sheshan = "noble"</em> into the sentence a reader is
    /// shown as the reason the page exists.
    /// </summary>
    [Fact]
    public void TheHeadwordLineBelongsToNoBearer() =>
        StrongNameEntries.Bearers("n-pr-m", Sheshan)[0].Says.Should().NotContain("noble");

    /// <summary>
    /// One entry, a man and a valley, and a heading over each. This is the case the whole parse is
    /// built for: the part of speech says <c>n-pr-m n-pr-loc</c> and decides nothing on its own,
    /// and the two words Strong writes between the clauses decide it.
    /// </summary>
    [Fact]
    public void AHeadingOverAClauseSaysWhatTheClauseIs()
    {
        var bearers = StrongNameEntries.Bearers("n-pr-m n-pr-loc", Berachah);

        bearers.Should().HaveCount(2);
        bearers[0].Kind.Should().Be(StrongNameKind.Person);
        bearers[1].Kind.Should().Be(StrongNameKind.Place);
    }

    /// <summary>
    /// A heading between two clauses closes the first one, and the second one closing must not
    /// empty it again. The clause is what a reader is shown in place of the parse, so a page whose
    /// claim quotes nothing is a page that says its record rests on an empty pair of quotes.
    /// </summary>
    [Fact]
    public void AHeadingBetweenTwoClausesLeavesTheFirstOneItsWords() =>
        StrongNameEntries.Bearers("n-pr-m n-pr-loc", Berachah)[0].Says
            .Should().Be("a Benjamite,  one of David's warriors");

    /// <summary>
    /// Adam the first man and Adam the city in the Jordan valley, both under <c>n-pr-m</c> and
    /// neither with a heading. Strong's tag says both are men and his prose says one is a town, and
    /// a rule that read the prose would have to decide which of his two statements to believe. So
    /// the entry counts two bearers of this kind, which is a refusal for anyone asking for one.
    /// </summary>
    [Fact]
    public void ThePartOfSpeechCarriesEveryClauseUntilAHeadingSaysOtherwise()
    {
        var bearers = StrongNameEntries.Bearers("n-pr-m", Adam);

        bearers.Should().HaveCount(2);
        bearers.Should().OnlyContain(bearer => bearer.Kind == StrongNameKind.Person);
    }

    /// <summary>
    /// Two kinds in the part of speech, no heading over the clause, and therefore no answer.
    /// <em>Anamim</em> would be reached by guessing and Rekem would be reached by guessing; null is
    /// what the entry actually says (RUL-0024).
    /// </summary>
    [Fact]
    public void TwoKindsAndNoHeadingLeavesTheClauseUndecided() =>
        StrongNameEntries.Bearers("n-pr-m n-pr-loc", "Rekem = \"variegation\"\n1) a king of Midian")
            .Should().ContainSingle().Which.Kind.Should().BeNull();

    /// <summary>
    /// Strong parts <em>Anamim</em> an ordinary noun and <em>Arvadites</em> an adjective, so
    /// neither entry heads a name at all — and the encyclopedia files a son of Mizraim under the
    /// first and a son of Canaan under the second. The part of speech is the only field that says
    /// so: both clauses read exactly like a clause about a person.
    /// </summary>
    [Theory]
    [InlineData("n", "Anamim = \"affliction of the waters\"\n1) a tribe of Egyptians")]
    [InlineData("a", "Arvadites = \"I shall break loose\"\n1) the descendants of Arvad,  a son of Canaan")]
    public void AWordStrongPartsAsAWordHeadsNoName(string morphology, string definition) =>
        StrongNameEntries.HeadsAName(morphology, definition).Should().BeFalse();

    /// <summary>
    /// A gentilic is a name of a kind and never of a person, and it is tagged as one. Reading it as
    /// a person would put the Levite clans on the men they descend from.
    /// </summary>
    [Fact]
    public void AGentilicIsAPeopleAndNotAMan() =>
        StrongNameEntries.Bearers("n-pr-gent", "Ludim = \"to the firebrands\"\n1) a people")
            .Should().ContainSingle().Which.Kind.Should().Be(StrongNameKind.People);

    /// <summary>
    /// A god is not a person of this encyclopedia, and a register of men that quietly gained one
    /// would be asserting something about the text nobody asked it.
    /// </summary>
    [Fact]
    public void ADeityIsNeitherAManNorAPlace() =>
        StrongNameEntries.Bearers("n-pr-deity", "Dagon = \"a fish\"\n1) a Philistine god")
            .Should().ContainSingle().Which.Kind.Should().Be(StrongNameKind.Deity);

    /// <summary>
    /// A clause running to a second line is one clause. Strong's own words are what a reader is
    /// shown in place of the parse, so half of a sentence is worse than none.
    /// </summary>
    [Fact]
    public void AClauseCarriedOverALineBreakIsOneClause() =>
        StrongNameEntries
            .Bearers("n-pr-m", "Ahiah = \"brother of Jehovah\"\n1) son of Ahitub,\nthe priest at Shiloh")
            .Should().ContainSingle().Which.Says.Should().Be("son of Ahitub, the priest at Shiloh");

    /// <summary>
    /// The Greek half of the lexicon carries neither a part of speech nor a numbered definition on
    /// any of its 5,523 entries, so this answers nothing about a Greek name rather than answering
    /// wrongly. Every caller has to say so instead of reading an empty list as an entry with no
    /// bearers.
    /// </summary>
    [Fact]
    public void AnEntryWithNeitherFieldYieldsNothing()
    {
        StrongNameEntries.HeadsAName(null, null).Should().BeFalse();
        StrongNameEntries.Bearers(null, null).Should().BeEmpty();
    }

    private const string Sheshan =
        "Sheshan = \"noble\"\n"
        + "1) a Judaite of the families of Hezron and Jerahmeel,  son of Ishi and father of Ahlai";

    private const string Berachah =
        "Berachah = \"blessing\"\n"
        + "n pr m\n"
        + "1) a Benjamite,  one of David's warriors\n"
        + "n pr loc\n"
        + "2) a valley in the wilderness near Tekoa where Jehoshaphat and his people assembled to "
        + "bless Jehovah after the overthrow of the hosts of the Moabites";

    private const string Adam = "Adam = \"red\"\n1) first man\n2) city in Jordan valley";
}
