using Essenthos.Core.Loading;
using Essenthos.Core.Usfm;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// The two inline markers that carry a claim rather than only words, which the reader learned about
/// for the German and Spanish texts.
///
/// Brenton and the Kulish Bible say nothing about the originals, so until now every marker standing
/// inside a line was either a note to drop or a span to unwrap. These two are neither: <c>\w</c>
/// names the Hebrew or Greek word a translated word renders, and <c>\add</c> marks a word the base
/// text does not have. Dropping the first would leave German and Spanish reaching the originals
/// only through this project's own aligner; dropping the second would print the translators'
/// additions as though the source had them.
/// </summary>
public class UsfmAnnotationTests
{
    [Fact]
    public void ATaggedWordCarriesItsStrongNumber()
    {
        const string tagged =
            """
            \id GEN
            \c 1
            \p
            \v 1 Am \w Anfang|strong="H7225"\w* \w schuf|strong="H1254"\w* Gott.
            """;

        var words = UsfmReader.Read(tagged).Chapters[0]!.Verses[0]!.Words;

        words.Select(word => word.Surface).Should().Equal("Am", "Anfang", "schuf", "Gott");
        words.Select(word => word.StrongNumber).Should().Equal(null, "H7225", "H1254", null);
    }

    /// <summary>
    /// The number is written a dozen ways in the wild and stored one way, so the padding a file
    /// happens to use never reaches a link.
    /// </summary>
    [Fact]
    public void APaddedNumberIsStoredTheWayEveryOtherOneIs() =>
        UsfmReader.Read(
                """
                \id GEN
                \c 1
                \v 1 \w Gott|strong="H0430"\w*
                """)
            .Chapters[0]!.Verses[0]!.Words[0]!.StrongNumber.Should().Be("H430");

    /// <summary>
    /// A tag over a phrase gives every word of the phrase the number. That is a reading of the
    /// edition rather than its own words — the file says the phrase renders it — and it is the only
    /// thing a per-word column can hold. The Spanish tags 198,086 of its spans this way.
    /// </summary>
    [Fact]
    public void EveryWordOfATaggedPhraseCarriesTheNumber()
    {
        var words = UsfmReader.Read(
                """
                \id GEN
                \c 1
                \v 1 \w EN el principio|strong="H7225"\w*
                """)
            .Chapters[0]!.Verses[0]!.Words;

        words.Select(word => word.Surface).Should().Equal("EN", "el", "principio");
        words.Should().OnlyContain(word => word.StrongNumber == "H7225");
    }

    /// <summary>
    /// A tag naming more than one number leaves the word with none. The Spanish writes <c>doce</c>
    /// as two and ten, and picking one of the pair would print this reader's choice as the
    /// edition's; the word is still the word, and the number is simply not claimed.
    /// </summary>
    [Fact]
    public void ATagNamingSeveralNumbersClaimsNone() =>
        UsfmReader.Read(
                """
                \id GEN
                \c 1
                \v 1 \w doce|strong="H8147,H6240"\w*
                """)
            .Chapters[0]!.Verses[0]!.Words[0]!.StrongNumber.Should().BeNull();

    /// <summary>
    /// A supplied span numbers its words, and the numbering restarts with each verse — the same
    /// shape the Synodal's square brackets already fill, so a reader can show what an edition added
    /// without knowing which edition it is looking at.
    /// </summary>
    [Fact]
    public void SuppliedSpansAreNumberedWithinTheirVerse()
    {
        var verses = UsfmReader.Read(
                """
                \id GEN
                \c 1
                \p
                \v 1 alfa \add beta\add* gamma \add delta\add* epsilon
                \v 2 zeta \add eta\add*
                """)
            .Chapters[0]!.Verses;

        verses[0]!.Words.Select(word => (word.Surface, word.SuppliedSpan))
            .Should().Equal(("alfa", null), ("beta", 1), ("gamma", null), ("delta", 2), ("epsilon", null));
        verses[1]!.Words.Select(word => (word.Surface, word.SuppliedSpan))
            .Should().Equal(("zeta", null), ("eta", 1));
    }

    /// <summary>
    /// USFM's mark for a character marker standing inside another one. The Spanish uses it three
    /// times in 390,758 tags, for a tagged word inside a supplied span, and three occurrences in a
    /// text this size are what a reader meets long after it has been trusted.
    /// </summary>
    [Fact]
    public void ATaggedWordInsideASuppliedSpanIsBoth()
    {
        var words = UsfmReader.Read(
                """
                \id 2CH
                \c 3
                \p
                \v 3 \w Estas|strong="H0428"\w* \add \+w son las medidas|strong="H4055"\+w*\add* fin
                """)
            .Chapters[0]!.Verses[0]!.Words;

        words.Select(word => word.Surface).Should().Equal("Estas", "son", "las", "medidas", "fin");
        words.Select(word => word.StrongNumber)
            .Should().Equal("H428", "H4055", "H4055", "H4055", null);
        words.Select(word => word.SuppliedSpan).Should().Equal(null, 1, 1, 1, null);
    }
}

/// <summary>Read once; three complete Bibles in USFM is a few seconds.</summary>
public sealed class Ebible
{
    internal TextSource Luther { get; } = EbibleTextSource.Read(TestResources.EbibleFolder("Luther1912"));

    internal TextSource Elberfelder { get; } =
        EbibleTextSource.Read(TestResources.EbibleFolder("Elberfelder1905"));

    internal TextSource ReinaValera { get; } =
        EbibleTextSource.Read(TestResources.EbibleFolder("ReinaValera1909"));

    internal IEnumerable<WordDraft> Words(TextSource source) => source.Books
        .SelectMany(book => book.Chapters)
        .SelectMany(chapter => chapter.Verses)
        .SelectMany(verse => verse.Words);

    internal VerseDraft Verse(TextSource source, int ordinal, int chapter, int verse) => source.Books
        .Single(book => book.CanonicalOrdinal == ordinal)
        .Chapters.Single(one => one.Number == chapter)
        .Verses.Single(one => one.Number == verse);
}

/// <summary>
/// The German and Spanish Bibles as they came off eBible. Each is a complete Bible in the English
/// numbering, and two of the three arrive tagged — which is the whole reason these three were the
/// ones taken, so it is checked rather than assumed.
/// </summary>
public class EbibleCorpusTests(Ebible ebible) : IClassFixture<Ebible>
{
    /// <summary>
    /// Sixty-six books and 1,189 chapters each, checked rather than trusted: a partial download is
    /// the failure this load can actually have, and eBible resets long connections.
    /// </summary>
    /// <param name="verses">
    /// The catalogue states 23,145 Old Testament verses and 7,957 New for all three. Luther has all
    /// of them; the other two have fewer, and both shortfalls are the edition speaking rather than a
    /// short download. The Elberfelder prints Acts 15:34 as a dash, which is its way of saying the
    /// verse is not in its Greek, and a verse of no words is not stored. The Reina-Valera leaves 18
    /// verse slots empty where its own numbering runs ahead of the English — the seams checked
    /// below.
    /// </param>
    [Theory]
    [InlineData(EbibleTextSource.Luther, 23145 + 7957)]
    [InlineData(EbibleTextSource.Elberfelder, 23145 + 7957 - 1)]
    [InlineData(EbibleTextSource.ReinaValera, 23145 + 7957 - 18)]
    public void EachIsAWholeBible(string slug, int verses)
    {
        var source = Source(slug);

        source.Books.Should().HaveCount(66);
        source.Books.Sum(book => book.Chapters.Count).Should().Be(929 + 260);
        source.Books.Sum(book => book.Chapters.Sum(chapter => chapter.Verses.Count)).Should().Be(verses);
    }

    /// <summary>
    /// Both German texts were renumbered to the English scheme by their publisher and say so in the
    /// running text — Luther writes <c>[30:1]</c> and the Elberfelder <c>(030:2)</c> — which is the
    /// one place either file records the numbering its own readers hold. Reading them is what keeps
    /// "30" and "1" out of the corpus as scripture.
    /// </summary>
    [Theory]
    [InlineData(EbibleTextSource.Luther, 357)]
    [InlineData(EbibleTextSource.Elberfelder, 261)]
    [InlineData(EbibleTextSource.ReinaValera, 0)]
    public void TheGermanTextsSayHowTheyAreNumberedAtHome(string slug, int addresses) =>
        Source(slug).Books
            .SelectMany(book => book.Chapters)
            .SelectMany(chapter => chapter.Verses)
            .Sum(verse => verse.Stated.Count)
            .Should().Be(addresses);

    /// <summary>
    /// Where Luther's numbering and the English one part, Luther holds the English verse and says
    /// what it is called at home. This is the check that the German is placed in the shared frame
    /// and not merely stored under English-looking addresses: the King James numbers "And Moses
    /// told the children of Israel" as Numbers 29:40, and the German Bible prints it as 30:1.
    /// </summary>
    [Fact]
    public void LutherHoldsTheEnglishVerseAndNamesItsOwn()
    {
        var verse = ebible.Verse(ebible.Luther, 4, 29, 40);

        Text(verse).Should().StartWith("Und Mose sagte den Kindern Israel");
        verse.Stated.Should().ContainSingle().Which.Should().Be(new StatedNumberDraft(30, 1));
    }

    /// <summary>
    /// And the Spanish is not placed. eBible left the English slot empty at 18 seams and let the
    /// following chapter run one to three verses late, merging the tail — so Numbers 30:1 of this
    /// file holds what the King James numbers 29:40, and its 30:16 holds two English verses at once.
    /// Nothing is missing and 216 verses are at an address other than the one the German uses for
    /// the same words, which is a wrong answer in a pane rather than a hole in the text.
    ///
    /// It is a test rather than a note because it is the defect a later reader would otherwise
    /// rediscover from a reader's complaint, and because a fetch that quietly corrected it would
    /// break the join to the one hand-made Spanish alignment that exists, which is keyed to these
    /// addresses.
    /// </summary>
    [Fact]
    public void TheSpanishKeepsItsOwnVerseDivisionAtEighteenSeams()
    {
        var chapter = ebible.ReinaValera.Books.Single(book => book.CanonicalOrdinal == 4)
            .Chapters.Single(one => one.Number == 29);

        chapter.Verses.Should().NotContain(verse => verse.Number == 40);
        Text(ebible.Verse(ebible.ReinaValera, 4, 30, 1))
            .Should().StartWith("Y MOISÉS dijo á los hijos de Israel");
        Text(ebible.Verse(ebible.ReinaValera, 4, 30, 16))
            .Should().Contain("Estas son las ordenanzas");
    }

    /// <summary>
    /// The numbering is the English one throughout, which is what lets all three stand beside the
    /// King James in the shared frame with no rule of their own. It is not a property of the
    /// language: eBible's other German text, the Textbibel, keeps the Hebrew division and prints
    /// Joel in four chapters and Malachi in three, and would need a rule of its own.
    /// </summary>
    [Theory]
    [InlineData(EbibleTextSource.Luther, 19, 150)]
    [InlineData(EbibleTextSource.Luther, 29, 3)]
    [InlineData(EbibleTextSource.Luther, 39, 4)]
    [InlineData(EbibleTextSource.Elberfelder, 19, 150)]
    [InlineData(EbibleTextSource.ReinaValera, 19, 150)]
    [InlineData(EbibleTextSource.ReinaValera, 39, 4)]
    public void EachIsNumberedTheWayTheKingJamesIs(string slug, int ordinal, int chapters) =>
        Source(slug).Books.Single(book => book.CanonicalOrdinal == ordinal)
            .Chapters.Should().HaveCount(chapters);

    /// <summary>
    /// Its order is the canonical one, which is what lets the ordinal be the position. Checked
    /// rather than assumed: the file names carry eBible's own numbering, in which Genesis is 02 and
    /// Matthew is 70, and reading either as an ordinal would put every book in the wrong place.
    /// </summary>
    [Theory]
    [InlineData(EbibleTextSource.Luther)]
    [InlineData(EbibleTextSource.Elberfelder)]
    [InlineData(EbibleTextSource.ReinaValera)]
    public void EveryBookStandsWhereTheCanonPutsIt(string slug)
    {
        var source = Source(slug);

        source.Books.Select(book => book.CanonicalOrdinal).Should().Equal(Enumerable.Range(1, 66));
        source.Books.Should().OnlyContain(book => book.Position == book.CanonicalOrdinal);
    }

    [Theory]
    [InlineData(EbibleTextSource.Luther, 1, "Das 1. Buch Mose (Genesis)")]
    [InlineData(EbibleTextSource.Elberfelder, 1, "Das 1. Buch Mose (Genesis)")]
    [InlineData(EbibleTextSource.ReinaValera, 1, "Génesis")]
    [InlineData(EbibleTextSource.ReinaValera, 19, "Salmos")]
    public void EveryBookIsNamedInItsOwnLanguage(string slug, int ordinal, string name)
    {
        var source = Source(slug);

        source.Books.Should().OnlyContain(book => book.NameNative != null && book.NameNative != "");
        source.Books.Single(book => book.CanonicalOrdinal == ordinal).NameNative.Should().Be(name);
    }

    /// <summary>
    /// The shortest proof that this file is the 1912 revision and not one of the other three texts
    /// people call the Luther Bible: 1545 opens Genesis "AM ANFANG SCHUFF Gott Himel vnd Erden",
    /// and 1984 opens John 3:16 "Denn also" and continues "damit alle".
    /// </summary>
    [Fact]
    public void TheGermanIsLuthersInThe1912Revision()
    {
        Text(ebible.Verse(ebible.Luther, 1, 1, 1)).Should().Be("Am Anfang schuf Gott Himmel und Erde.");
        Text(ebible.Verse(ebible.Luther, 43, 3, 16)).Should().StartWith("Also hat Gott die Welt geliebt");
        Text(ebible.Verse(ebible.Luther, 43, 3, 16)).Should().Contain("auf daß alle");
    }

    /// <summary>
    /// And that the second German is not the first. The Elberfelder prints the divine name where
    /// Luther prints the title, which is the difference a reader sees in the first psalm they open.
    /// </summary>
    [Fact]
    public void TheOtherGermanIsTheElberfelder()
    {
        Text(ebible.Verse(ebible.Elberfelder, 19, 23, 1)).Should().Contain("Jehova ist mein Hirte");
        Text(ebible.Verse(ebible.Luther, 19, 23, 1)).Should().Contain("Der HERR ist mein Hirte");
    }

    /// <summary>
    /// The same question of the Spanish, where the answer matters more: the 1960 is the edition most
    /// readers know and is the one Sociedades Bíblicas Unidas licenses by the verse. 1909 writes
    /// "crió" where 1960 writes "creó", and accents the preposition that 1960 drops.
    /// </summary>
    [Fact]
    public void TheSpanishIsThe1909AndNotThe1960()
    {
        Text(ebible.Verse(ebible.ReinaValera, 1, 1, 1))
            .Should().Be("EN el principio crió Dios los cielos y la tierra.");
        Text(ebible.Verse(ebible.ReinaValera, 43, 3, 16)).Should().Contain("ha dado á su Hijo unigénito");
    }

    /// <summary>
    /// What the reader gets out of each, which no catalogue states and only counting establishes. A
    /// number here that moves without the verse counts moving is the tokeniser changing its mind,
    /// which is the change that would silently re-align every link ever built from these texts.
    /// </summary>
    [Theory]
    [InlineData(EbibleTextSource.Luther, 696963)]
    [InlineData(EbibleTextSource.Elberfelder, 721754)]
    [InlineData(EbibleTextSource.ReinaValera, 703737)]
    public void EveryWordIsCounted(string slug, int words) =>
        ebible.Words(Source(slug)).Should().HaveCount(words);

    /// <summary>
    /// The reason Luther was taken over better-known German editions. A Strong number on the
    /// translated word is the edition saying which original word it renders, and it is the only
    /// route from German to the Hebrew and Greek that is not this project's own inference.
    /// </summary>
    [Fact]
    public void TheGermanCarriesItsNumbers() =>
        ebible.Words(ebible.Luther).Count(word => word.StrongNumber != null).Should().Be(365350);

    /// <summary>
    /// And the Spanish does not, although its file carries 671,228 of them. That tagging is Rubén
    /// Gómez's work, which eBible republishes under a public-domain line without naming him and
    /// which CrossWire carries encrypted as "Copyrighted; Permission to distribute granted to
    /// CrossWire" — so the words are loaded and the numbers are refused.
    ///
    /// It is a test because it is a decision, not a fact about the file: a later pass that "fixed"
    /// the Spanish's missing numbers by reading them off the same file would undo it silently.
    /// </summary>
    [Fact]
    public void TheSpanishTaggingIsNotOursAndIsNotLoaded() =>
        ebible.Words(ebible.ReinaValera).Should().OnlyContain(word => word.StrongNumber == null);

    /// <summary>
    /// And the third carries nothing at all, which is worth checking rather than assuming: a later
    /// pass writing derived annotation onto a text as though the edition had supplied it is a guess
    /// stored as testimony, and this is what would catch it.
    /// </summary>
    [Fact]
    public void TheElberfelderCarriesNoAnnotationAtAll() =>
        ebible.Words(ebible.Elberfelder).Should().OnlyContain(word =>
            word.Lemma == null && word.StrongNumber == null && word.Gloss == null
            && word.Morphology == null && word.SuppliedSpan == null);

    /// <summary>
    /// The italics of a printed Reina-Valera: 3,501 spans the translators marked as words they
    /// supplied, which reach the same column the Synodal's square brackets do.
    /// </summary>
    [Fact]
    public void TheSpanishMarksTheWordsItSupplies()
    {
        var verses = ebible.ReinaValera.Books
            .SelectMany(book => book.Chapters)
            .SelectMany(chapter => chapter.Verses)
            .ToList();

        verses.Sum(verse => verse.Words
                .Where(word => word.SuppliedSpan is not null)
                .Select(word => word.SuppliedSpan)
                .Distinct()
                .Count())
            .Should().Be(3501);
    }

    /// <summary>
    /// A book missing from the folder stops the load rather than shortening the Bible, and a folder
    /// nobody has written a definition for stops it before a file is opened — the licence and the
    /// provenance are part of the definition, not something filled in afterwards.
    /// </summary>
    [Fact]
    public void APartialFolderIsRefused()
    {
        var empty = Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), $"ebible-{Guid.NewGuid():n}", "Luther1912"));

        try
        {
            var act = () => EbibleTextSource.Read(empty.FullName);

            act.Should().Throw<InvalidOperationException>().WithMessage("*GEN*");
        }
        finally
        {
            empty.Parent!.Delete(recursive: true);
        }
    }

    [Fact]
    public void AFolderWithNoDefinitionIsRefused()
    {
        var unknown = Directory.CreateTempSubdirectory("Luther1984");

        try
        {
            var act = () => EbibleTextSource.Read(unknown.FullName);

            act.Should().Throw<ArgumentException>().WithMessage("*licence and provenance*");
        }
        finally
        {
            unknown.Delete(recursive: true);
        }
    }

    private TextSource Source(string slug) => slug switch
    {
        EbibleTextSource.Luther => ebible.Luther,
        EbibleTextSource.Elberfelder => ebible.Elberfelder,
        EbibleTextSource.ReinaValera => ebible.ReinaValera,
        _ => throw new ArgumentOutOfRangeException(nameof(slug), slug, "That is not one of the three."),
    };

    private static string Text(VerseDraft verse) =>
        string.Concat(verse.Words.Select(word => word.Surface + word.Trailer)).Trim();
}
