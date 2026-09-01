using Essenthos.Core.TextusReceptus;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// Robinson's composite, read as the two editions it holds. Every fixture here is copied from the
/// files rather than invented, because the shapes that break a reader are exactly the ones a
/// description of the format leaves out.
/// </summary>
public class UtrReaderTests
{
    private const string Genealogy =
        """
        1:1 biblov 976 {N-NSF} genesewv 1078 {N-GSF} ihsou 2424 {N-GSM}
         cristou 5547 {N-GSM} uiou 5207 {N-GSM} dabid 1138 {N-PRI}
        1:2 abraam 11 {N-PRI} egennhsen 1080 5656 {V-AAI-3S} ton 3588 {T-ASM}
        """;

    [Fact]
    public void AVerseIsReadAcrossTheLinesItWrapsOnto()
    {
        var verses = UtrReader.Read(Genealogy, Edition.Scrivener1894);

        verses.Should().HaveCount(2);
        verses[0].Words.Should().HaveCount(6);
        verses[0].Words[5].Surface.Should().Be("dabid");
    }

    /// <summary>
    /// A verb carries its Strong number and then a five-digit tense-voice-mood code. Read as two
    /// words' numbers, every verb in the New Testament is misparsed and the error is silent.
    /// </summary>
    [Fact]
    public void AVerbsInflectionCodeIsNotASecondWordsStrongNumber()
    {
        var verse = UtrReader.Read(Genealogy, Edition.Scrivener1894)[1];

        verse.Words[1].Should().Be(new UtrWord("egennhsen", "1080", "5656", "V-AAI-3S"));
        verse.Words[2].Should().Be(new UtrWord("ton", "3588", null, "T-ASM"));
    }

    /// <summary>The first alternative is Stephanus and the second is Scrivener, in every group.</summary>
    [Theory]
    [InlineData(Edition.Stephanus1550, "epaggelia", "1860")]
    [InlineData(Edition.Scrivener1894, "aggelia", "31")]
    public void EachEditionIsOneSideOfEveryVariantGroup(Edition edition, string surface, string strong)
    {
        var verse = UtrReader.Read(
            "1:5 kai 2532 {CONJ} | epaggelia 1860 {N-NSF} | aggelia 31 {N-NSF} | estin 2076 {V-PXI-3S}",
            edition)[0];

        verse.Words.Should().HaveCount(3);
        verse.Words[1].Surface.Should().Be(surface);
        verse.Words[1].Strong.Should().Be(strong);
        verse.Words[2].Surface.Should().Be("estin");
    }

    /// <summary>
    /// The shape no description mentions: one word spelt two ways, with the Strong number and parse
    /// standing after the closing pipe and belonging to both. A reader expecting tags inside every
    /// alternative drops the word entirely — this is how Nazareth vanishes from Matthew 2:23.
    /// </summary>
    [Theory]
    [InlineData(Edition.Stephanus1550, "ouqen")]
    [InlineData(Edition.Scrivener1894, "ouden")]
    public void TheSpellingsOnlyShapeKeepsItsWordAndTakesTheTagsFromAfterTheGroup(Edition edition, string surface)
    {
        var verse = UtrReader.Read("2:9 kai 2532 {CONJ} | ouqen | ouden | 3762 {A-NSN-N} eti 2089 {ADV}", edition)[0];

        verse.Words.Should().HaveCount(3);
        verse.Words[1].Should().Be(new UtrWord(surface, "3762", null, "A-NSN-N"));
        verse.Words[2].Surface.Should().Be("eti");
    }

    [Fact]
    public void AGroupThisReaderHasNotSeenIsRefusedRatherThanGuessedAt()
    {
        var third = () => UtrReader.Read("1:1 | a 1 {N} | b 2 {N} | c 3 {N} |", Edition.Scrivener1894);

        third.Should().Throw<InvalidOperationException>().WithMessage("*1:1*");
    }

    /// <summary>
    /// Zero is not a Strong number. Robinson writes it before the number of a proper name the
    /// concordance lists elsewhere; taken literally it makes the word unresolvable and pushes the
    /// real number into the slot a verb's inflection code uses.
    /// </summary>
    [Fact]
    public void AWordMarkedUnnumberedTakesTheNumberThatFollows()
    {
        var verse = UtrReader.Read("2:25 simewn 0 4826 {N-PRI}", Edition.Scrivener1894)[0];

        verse.Words.Single().Should().Be(new UtrWord("simewn", "4826", null, "N-PRI"));
    }

    /// <summary>
    /// A crasis carries the number of each half — eanper is ean and per — and neither is an
    /// inflection code.
    /// </summary>
    [Fact]
    public void ACrasisKeepsBothOfItsNumbers()
    {
        var verse = UtrReader.Read("3:6 eanper 0 1437 4007 {COND}", Edition.Scrivener1894)[0];

        var word = verse.Words.Single();
        word.Strong.Should().Be("1437");
        word.Inflection.Should().BeNull();
        word.Alternatives.Should().Equal("4007");
    }

    [Fact]
    public void AVerseWithNoVariantsReadsTheSameInBothEditions()
    {
        const string plain = "3:16 outwv 3779 {ADV} gar 1063 {CONJ} hgaphsen 25 5656 {V-AAI-3S}";

        UtrReader.Read(plain, Edition.Stephanus1550).Should()
            .BeEquivalentTo(UtrReader.Read(plain, Edition.Scrivener1894));
    }
}
