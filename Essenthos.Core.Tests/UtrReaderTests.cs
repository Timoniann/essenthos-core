using Essenthos.Core.Loading;
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

        (verse.Words[1] with { Segment = 0 }).Should().Be(new UtrWord("egennhsen", "1080", "5656", "V-AAI-3S"));
        (verse.Words[2] with { Segment = 0 }).Should().Be(new UtrWord("ton", "3588", null, "T-ASM"));
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
        (verse.Words[1] with { Segment = 0 }).Should().Be(new UtrWord(surface, "3762", null, "A-NSN-N"));
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

        (verse.Words.Single() with { Segment = 0 }).Should().Be(new UtrWord("simewn", "4826", null, "N-PRI"));
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

/// <summary>
/// A verse row means the text has that verse. Stephanus 1550 omits Luke 17:36 and the composite
/// still carries the number, with the words on Scrivener's side of the group and nothing on
/// Stephanus's — so the edition that does not print the verse must not get a verse.
/// </summary>
public class TextusReceptusOmissionTests
{
    private const int Luke = 42;

    [Theory]
    [InlineData(Edition.Stephanus1550, false)]
    [InlineData(Edition.Scrivener1894, true)]
    public void OnlyTheEditionThatPrintsLukeSeventeenThirtySixHasIt(Edition edition, bool present)
    {
        var chapter = SeventeenthOfLuke(edition);

        chapter.Verses.Any(verse => verse.Number == 36).Should().Be(present);
        chapter.Verses.Should().HaveCount(present ? 37 : 36);
    }

    /// <summary>
    /// The one wordless verse in the corpus was here, and it made the database and the API
    /// disagree about how many verses Stephanus has: the reader filtered it out and the row stayed.
    /// </summary>
    [Fact]
    public void NoEditionCarriesAVerseWithNoWords()
    {
        foreach (var edition in new[] { Edition.Stephanus1550, Edition.Scrivener1894 })
        {
            TextusReceptusTextSource.Read(TestResources.TextusReceptusFolder, edition).Books
                .SelectMany(book => book.Chapters)
                .SelectMany(chapter => chapter.Verses)
                .Should().NotContain(verse => verse.Words.Count == 0);
        }
    }

    /// <summary>
    /// The composite writes the other edition's verse number inline where the two divide a verse
    /// differently. The outer loop already skipped it; inside a variant group it was read as a
    /// word, so Stephanus's Matthew 23:13 opened with "(23:14)". PRB-0094.
    /// </summary>
    [Theory]
    [InlineData(Edition.Stephanus1550)]
    [InlineData(Edition.Scrivener1894)]
    public void NoWordIsAVerseNumberInBrackets(Edition edition)
    {
        var words = TextusReceptusTextSource.Read(TestResources.TextusReceptusFolder, edition).Books
            .SelectMany(book => book.Chapters)
            .SelectMany(chapter => chapter.Verses)
            .SelectMany(verse => verse.Words);

        words.Should().NotContain(word => word.Surface.StartsWith('(') && word.Surface.EndsWith(')'));
    }

    /// <summary>
    /// A variant group whose two alternatives are a parse and nothing else is the parse of the word
    /// before it, not a word. Colossians 4:10 writes "barnaba 921 | {N-GSM} | {N-DSM} |" because
    /// Stephanus reads Βαρναβᾶ as a genitive and Scrivener as a dative. Read as a word it put
    /// "{N-GSM}" into the text and left Βαρναβᾶ with no parse. PRB-0094.
    /// </summary>
    [Theory]
    [InlineData(Edition.Stephanus1550, "N-GSM")]
    [InlineData(Edition.Scrivener1894, "N-DSM")]
    public void AGroupOfParsesBelongsToTheWordBeforeIt(Edition edition, string expected)
    {
        var words = UtrReader.Read(File.ReadAllText(TestResources.TextusReceptus("COL")), edition)
            .Single(verse => verse is { Chapter: 4, Number: 10 })
            .Words;

        words.Should().NotContain(word => word.Surface.StartsWith('{'));
        words.Single(word => word.Surface == "barnaba").Morphology.Should().Be(expected);
    }

    /// <summary>
    /// No word of either edition is a brace: a parse is a word's attribute and never a word. This
    /// is the assertion the two above are instances of, over the whole corpus.
    /// </summary>
    [Theory]
    [InlineData(Edition.Stephanus1550)]
    [InlineData(Edition.Scrivener1894)]
    public void NoWordIsAParse(Edition edition)
    {
        TextusReceptusTextSource.Read(TestResources.TextusReceptusFolder, edition).Books
            .SelectMany(book => book.Chapters)
            .SelectMany(chapter => chapter.Verses)
            .SelectMany(verse => verse.Words)
            .Should().NotContain(word => word.Surface.StartsWith('{'));
    }

    private static ChapterDraft SeventeenthOfLuke(Edition edition) =>
        TextusReceptusTextSource.Read(TestResources.TextusReceptusFolder, edition).Books
            .Single(book => book.CanonicalOrdinal == Luke).Chapters
            .Single(chapter => chapter.Number == 17);
}
