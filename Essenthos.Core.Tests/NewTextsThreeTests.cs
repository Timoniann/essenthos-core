using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Door43;
using Essenthos.Core.Loading;
using Essenthos.Core.Loading.Links;
using Essenthos.Core.Loading.Frame;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>Read once: the Almeida is parsed from a five-megabyte book, the ULT from 56 aligned files.</summary>
public sealed class ThirdBatchTexts
{
    internal TextSource Almeida { get; } = AlmeidaTextSource.Read(TestResources.Folder(AlmeidaTextSource.Folder));

    internal TextSource Literal { get; } =
        UnfoldingWordTextSource.Read(TestResources.Folder(Path.Combine("Door43", UnfoldingWordTextSource.Folder)));

    internal TextSource Ukrainian { get; } = EbibleTextSource.Read(TestResources.EbibleFolder("BiblicaUkrainian2022"));
}

public class NewTextsThreeTests(ThirdBatchTexts texts) : IClassFixture<ThirdBatchTexts>
{
    /// <summary>
    /// The Almeida holds every verse the King James numbers, once its five places of other numbering
    /// are laid at the English addresses — the only address with nothing at it is 2 Corinthians 13:13,
    /// whose words the printing gives to 13:12.
    /// </summary>
    [Fact]
    public void TheAlmeidaIsAWholeBibleInTheEnglishNumbering()
    {
        var verses = WorldTexts.Verses(texts.Almeida).ToList();

        texts.Almeida.Books.Should().HaveCount(66);
        verses.Should().HaveCount(31_102 - 1);
        verses.Should().NotContain(verse => verse.Book == 47 && verse.Chapter == 13 && verse.Verse.Number == 13);
        verses.Sum(verse => verse.Verse.Words.Count).Should().Be(ALMEIDA_WORDS);
    }

    [Fact]
    public void TheAlmeidaReadsAsPrinted()
    {
        WorldTexts.Text(texts.Almeida, 1, 1, 1).Should().Be("No principio creou Deus os céus e a terra.");
        WorldTexts.Text(texts.Almeida, 40, 1, 1).Should().Be("Livro da geração de Jesus Christo, filho de David, filho d’Abrahão.");
        WorldTexts.Text(texts.Almeida, 19, 3, 1).Should().StartWith(
            "Psalmo de David, quando fugiu de diante da face de Absalão seu filho. Senhor, como se teem multiplicado");
    }

    /// <summary>A psalm's title is the head of its first verse and says so, as the King James's are.</summary>
    [Fact]
    public void AnAlmeidaPsalmTitleOpensItsFirstVerse()
    {
        var verse = WorldTexts.Verses(texts.Almeida).Single(v => (v.Book, v.Chapter, v.Verse.Number) == (19, 3, 1)).Verse;

        verse.MarksASuperscription.Should().BeTrue();
        WorldTexts.Verses(texts.Almeida).Count(v => v.Verse.MarksASuperscription).Should().Be(ALMEIDA_TITLES);
    }

    /// <summary>
    /// The printing's italics are words the translator supplied, and they are marked as supplied; its
    /// cross references and alternative renderings are notes, never words.
    /// </summary>
    [Fact]
    public void TheAlmeidasItalicsAreSuppliedAndItsMarksAreNotes()
    {
        var verse = WorldTexts.Verses(texts.Almeida).Single(v => (v.Book, v.Chapter, v.Verse.Number) == (1, 1, 2)).Verse;

        verse.Words.Single(word => word.Surface == "havia").SuppliedSpan.Should().NotBeNull();
        verse.Words.Should().NotContain(word => word.Surface.Contains('[') || word.Surface.Contains('_'));
        verse.Notes.Where(note => note.Kind == VerseNoteKind.CrossReference).Select(note => note.Content)
            .Should().Equal("Jer. 4.23.", "Job 26.13. Psa. 104.30.");

        var all = WorldTexts.Verses(texts.Almeida).SelectMany(v => v.Verse.Notes).ToList();
        all.Count(note => note.Kind == VerseNoteKind.CrossReference).Should().Be(ALMEIDA_REFERENCES);
        all.Count(note => note.Kind == VerseNoteKind.Footnote).Should().Be(ALMEIDA_RENDERINGS);
    }

    /// <summary>Where the printing numbers apart what the English prints as one verse, both addresses are kept.</summary>
    [Theory]
    [InlineData(7, 5, 31, "E socegou a terra quarenta annos.")]
    [InlineData(9, 20, 42, "Então se levantou David, e se foi; e Jonathan entrou na cidade.")]
    [InlineData(64, 1, 14, "Paz seja comtigo. Os amigos te saudam. Sauda os amigos por nome.")]
    public void AVerseThePrintingNumbersApartJoinsTheEnglishOne(int book, int chapter, int verse, string ending)
    {
        WorldTexts.Text(texts.Almeida, book, chapter, verse).Should().EndWith(ending);
        WorldTexts.Verses(texts.Almeida).Single(v => (v.Book, v.Chapter, v.Verse.Number) == (book, chapter, verse))
            .Verse.Stated.Should().Equal(new StatedNumberDraft(chapter, verse), new StatedNumberDraft(chapter, verse + 1));
    }

    [Fact]
    public void TheAlmeidasFirstKingsTwentyTwoFollowsTheEnglishAfterItsJoin()
    {
        WorldTexts.Text(texts.Almeida, 11, 22, 53).Should().StartWith("E serviu a Baal");
        WorldTexts.Verses(texts.Almeida).Single(v => (v.Book, v.Chapter, v.Verse.Number) == (11, 22, 53))
            .Verse.Stated.Should().Equal(new StatedNumberDraft(22, 54));
        WorldTexts.Verses(texts.Almeida).Count(v => v.Book == 11 && v.Chapter == 22).Should().Be(53);
    }

    /// <summary>Mark 4:34 is printed 31, and Hosea 11:5 runs on in the paragraph of 11:4.</summary>
    [Fact]
    public void TheAlmeidasMisprintsAreReadAsWhatTheyMean()
    {
        WorldTexts.Text(texts.Almeida, 41, 4, 34).Should().StartWith("E sem parabolas nunca lhes fallava");
        WorldTexts.Text(texts.Almeida, 28, 11, 4).Should().EndWith("e lhe dei mantimento.");
        WorldTexts.Text(texts.Almeida, 28, 11, 5).Should().StartWith("Não voltará para a terra do Egypto");
    }

    /// <summary>
    /// The ULT's words come out of the alignment whole, one verse at a time, and its psalm titles
    /// open their first verse as the English prints them.
    /// </summary>
    [Fact]
    public void TheLiteralTextReadsAsPrinted()
    {
        texts.Literal.Books.Should().HaveCount(56);
        WorldTexts.Text(texts.Literal, 1, 1, 1).Should().Be("In the beginning God created the heavens and the earth.");
        WorldTexts.Text(texts.Literal, 43, 1, 1).Should().StartWith("In the beginning was the Word");
        WorldTexts.Text(texts.Literal, 19, 3, 1).Should().StartWith("A psalm of David, when he fled from the face of Absalom his son Yahweh, how many");
        WorldTexts.Verses(texts.Literal).Single(v => (v.Book, v.Chapter, v.Verse.Number) == (19, 3, 1))
            .Verse.MarksASuperscription.Should().BeTrue();
        WorldTexts.Verses(texts.Literal).SelectMany(v => v.Verse.Words)
            .Should().NotContain(word => word.Surface.Contains('\\') || word.Surface.Contains('|'));
    }

    [Fact]
    public void TheLiteralTextHoldsTheVersesOfItsRelease() =>
        WorldTexts.Verses(texts.Literal).Should().HaveCount(LITERAL_VERSES);

    [Fact]
    public void TheShareAlikeTextsSayWhatTheyAre()
    {
        foreach (var definition in new[] { texts.Literal.Definition, texts.Ukrainian.Definition })
        {
            definition.Licence.Should().Be("CC-BY-SA-4.0");
            definition.Redistribution.Should().Be(Redistribution.ShareAlike);
        }

        texts.Almeida.Definition.Redistribution.Should().Be(Redistribution.PublicDomain);
    }

    /// <summary>
    /// Biblica's Ukrainian is the Psalms and the New Testament and nothing else, and its Psalms are
    /// numbered as the Hebrew numbers them.
    /// </summary>
    [Fact]
    public void TheUkrainianIsThePsalmsAndTheNewTestament()
    {
        texts.Ukrainian.Books.Select(book => book.CanonicalOrdinal).Should().Equal([19, .. Enumerable.Range(40, 27)]);
        texts.Ukrainian.Books.Select(book => book.Position).Should().Equal(Enumerable.Range(1, 28));
        WorldTexts.Verses(texts.Ukrainian).Should().HaveCount(2526 + 7957);
        WorldTexts.Text(texts.Ukrainian, 43, 1, 1).Should().Be("На початку було Слово, і Слово було з Богом, і Слово було Бог.");
        WorldTexts.Text(texts.Ukrainian, 19, 3, 1).Should().Be("Псалом Давидів, коли він втікав від свого сина Авесалома.");
    }

    [Theory]
    [InlineData(19, 3, 1, 19, 3, 0)]
    [InlineData(19, 3, 2, 19, 3, 1)]
    [InlineData(43, 1, 1, 43, 1, 1)]
    public void TheUkrainianPsalmsArePlacedAsTheHebrewNumbersThem(
        int book, int chapter, int verse, int canonicalBook, int canonicalChapter, int canonicalVerse)
    {
        var frame = TvtmsReader.Read(TestResources.Tvtms).Frame(
            texts.Ukrainian.Definition.Versification,
            EditionShape.Of(WorldTexts.Verses(texts.Ukrainian).Select(v =>
                (v.Book, v.Chapter, v.Verse.Number, v.Verse.Label, v.Verse.Words.Sum(word => word.Surface.Length)))));

        frame.Resolve(book, chapter, verse)[0]
            .Should().Be(new CanonicalReference(canonicalBook, canonicalChapter, canonicalVerse));
    }

    [Fact]
    public void TheUnalignedLineIsTheWordAlone() =>
        UnfoldingWordTextSource.Unaligned(
                "\\v 1 \\zaln-s |x-strong=\"H0430\" x-content=\"אֱלֹהִ֑ים\"\\*\\w God|x-occurrence=\"1\" x-occurrences=\"1\"\\w*\\zaln-e\\*,\n\\ts\\*")
            .Should().Be("\\v 1 God,\n");

    private const int ALMEIDA_WORDS = 706_112;
    private const int ALMEIDA_TITLES = 116;
    private const int ALMEIDA_REFERENCES = 20_287;
    private const int ALMEIDA_RENDERINGS = 1_070;
    private const int LITERAL_VERSES = 23_186;
}

/// <summary>
/// unfoldingWord aligns its translation to its own Hebrew and Greek, which are not BHSA and Nestle:
/// where a word is spelled or accented differently, the join finds it by its Strong number — only
/// where the number stands once in the source verse and once in the witness verse.
/// </summary>
public class InterlinearNumberTests
{
    [Theory]
    [InlineData("c:d:H0776", "H776")]
    [InlineData("H1254a", "H1254")]
    [InlineData("G17220", "G1722")]
    [InlineData("G35880", "G3588")]
    public void AStrongCodeNamesTheLemmaTheCorpusWrites(string code, string lemma) =>
        InterlinearJoin.Lemma(code).Should().Be(lemma);

    [Fact]
    public void AWordSpelledOtherwiseJoinsByItsNumberWhenItStandsOnceOnEachSide()
    {
        var (pairs, account) = Join(
            [Word(1, "eng", "Absalom")],
            [Word(101, "hbo", "אַבְשָׁלֹ֗ם", "H53"), Word(102, "hbo", "בְּנֹֽו", "H1121")],
            [("H0053", "אַבְשָׁל֬וֹם", 1)],
            new AlignmentSpan("H0053", "אַבְשָׁל֬וֹם", ["Absalom"], 1, 1));

        pairs.Single().To.Should().Equal(101);
        account.SpansJoinedByNumber.Should().Be(1);
    }

    [Fact]
    public void ANumberTheWitnessHoldsTwiceIsRefused()
    {
        var (pairs, _) = Join(
            [Word(1, "eng", "day")],
            [Word(101, "hbo", "יֹ֥ום", "H3117"), Word(102, "hbo", "יֹֽום", "H3117")],
            [("H3117", "יוֹם", 1)],
            new AlignmentSpan("H3117", "יוֹם", ["day"], 1, 1));

        pairs.Should().BeEmpty();
    }

    /// <summary>The prefix the source cuts off has to be spelled by the witness words before the one the number found.</summary>
    [Fact]
    public void APrefixIsJoinedOnlyWhereTheWitnessSpellsIt()
    {
        var witness = new[] { Word(100, "hbo", "וְ", "H9000"), Word(101, "hbo", "רֹמֵ֥שׂ", "H7430") };

        Join([Word(1, "eng", "moving")], witness, [("c:H7430", "וְ⁠רֹמֵשׂ", 1)],
                new AlignmentSpan("c:H7430", "וְ⁠רֹמֵשׂ", ["moving"], 1, 1)).Pairs
            .Single().To.Should().Equal(100, 101);
        Join([Word(1, "eng", "moving")], witness, [("b:H7430", "בְּ⁠רֹמֵשׂ", 1)],
                new AlignmentSpan("b:H7430", "בְּ⁠רֹמֵשׂ", ["moving"], 1, 1)).Pairs
            .Should().BeEmpty();
    }

    /// <summary>A psalm's title before verse 1 is kept for verse 1, where the corpus prints it.</summary>
    [Fact]
    public void ATitlesSpansGoToTheFirstVerse()
    {
        var verses = Usfm3AlignmentReader.Read(
            """
            \c 3
            \d \zaln-s |x-strong="H4210" x-occurrence="1" x-occurrences="1" x-content="מִזְמ֥וֹר"\*\w A|x-occurrence="1" x-occurrences="1"\w* \w psalm|x-occurrence="1" x-occurrences="1"\w*\zaln-e\*
            \q1 \v 1 \zaln-s |x-strong="H3068" x-occurrence="1" x-occurrences="1" x-content="יְהוָ֗ה"\*\w Yahweh|x-occurrence="1" x-occurrences="1"\w*\zaln-e\*
            """);

        verses.Should().ContainSingle().Which.Number.Should().Be(1);
        verses[0].Spans.Select(span => span.Strong).Should().Equal("H4210", "H3068");
        verses[0].Originals.Should().HaveCount(2);
    }

    [Fact]
    public void TheLiteralTextsBracesAreSuppliedWords() =>
        UnfoldingWordTextSource.Unaligned(@"\v 2 darkness {was} over").Should().Be(@"\v 2 darkness \addwas\add* over");

    private static (List<InterlinearPair> Pairs, InterlinearJoinAccount Account) Join(
        IReadOnlyList<InterlinearWord> translated,
        IReadOnlyList<InterlinearWord> original,
        IReadOnlyList<(string, string, int)> originals,
        params AlignmentSpan[] spans)
    {
        var pairs = new List<InterlinearPair>();
        var account = new InterlinearJoinAccount();
        InterlinearJoin.Verse("test 1:1", new AlignedVerse(1, 1, spans, Originals: originals), translated, original, pairs, account);
        return (pairs, account);
    }

    private static InterlinearWord Word(long id, string language, string written, string? strong = null) =>
        new(id, Essenthos.Core.Corpus.WordFolding.Fold(written, language), language, written, strong);
}
