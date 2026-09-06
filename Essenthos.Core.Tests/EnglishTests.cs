using Essenthos.Core.Loading;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>Read once; six English Bibles in USFM is a few seconds.</summary>
public sealed class English
{
    internal TextSource Tyndale { get; } = EnglishTextSource.Read(TestResources.EbibleFolder("Tyndale1534"));

    internal TextSource Geneva { get; } = EnglishTextSource.Read(TestResources.EbibleFolder("Geneva1599"));

    internal TextSource AmericanStandard { get; } =
        EnglishTextSource.Read(TestResources.EbibleFolder("AmericanStandard1901"));

    internal TextSource Young { get; } = EnglishTextSource.Read(TestResources.EbibleFolder("Young1898"));

    internal TextSource WorldEnglish { get; } =
        EnglishTextSource.Read(TestResources.EbibleFolder("WorldEnglish"));

    internal TextSource JewishPublicationSociety { get; } =
        EnglishTextSource.Read(TestResources.EbibleFolder("Jps1917"));

    internal IEnumerable<WordDraft> Words(TextSource source) => source.Books
        .SelectMany(book => book.Chapters)
        .SelectMany(chapter => chapter.Verses)
        .SelectMany(verse => verse.Words);

    internal VerseDraft Verse(TextSource source, int ordinal, int chapter, int verse) => source.Books
        .Single(book => book.CanonicalOrdinal == ordinal)
        .Chapters.Single(one => one.Number == chapter)
        .Verses.Single(one => one.Number == verse);

    internal bool Holds(TextSource source, int ordinal, int chapter, int verse) => source.Books
        .Single(book => book.CanonicalOrdinal == ordinal)
        .Chapters.Single(one => one.Number == chapter)
        .Verses.Any(one => one.Number == verse);
}

/// <summary>
/// The English Bibles that are not the King James in other spelling.
///
/// Every one of them is here for what it is a witness to rather than for its name, so what is
/// checked is exactly that: which underlying text each follows, which edition of it this file is,
/// and — for the two that arrive tagged — that the tagging stays out.
/// </summary>
public class EnglishCorpusTests(English english) : IClassFixture<English>
{
    /// <summary>
    /// The shape of each, checked rather than trusted: a partial download is the failure this load
    /// can actually have, and eBible resets long connections.
    /// </summary>
    /// <param name="verses">
    /// Verses holding words. A verse slot the edition prints empty is not stored, and each of the
    /// three shortfalls here is the edition speaking. The American Standard leaves sixteen slots
    /// empty where the Textus Receptus prints a verse and its critical Greek does not; the World
    /// English Bible leaves five, four of them the same kind and the fifth Romans 16:25, whose
    /// doxology it prints at 14:24 instead; Tyndale leaves three, where a verse division made
    /// seventeen years after his printing has nothing of his to number.
    /// </param>
    [Theory]
    [InlineData(EnglishTextSource.Tyndale, 27, 260, 7957 - 3)]
    [InlineData(EnglishTextSource.Geneva, 66, 1189, 23137 + 7953)]
    [InlineData(EnglishTextSource.AmericanStandard, 66, 1189, 23145 + 7957 - 16)]
    [InlineData(EnglishTextSource.Young, 66, 1189, 23145 + 7957)]
    [InlineData(EnglishTextSource.WorldEnglish, 66, 1189, 23145 + 7958 - 5)]
    [InlineData(EnglishTextSource.JewishPublicationSociety, 39, 929, 23145)]
    public void EachHoldsTheBooksItsEditionHolds(string slug, int books, int chapters, int verses)
    {
        var source = Source(slug);

        source.Books.Should().HaveCount(books);
        source.Books.Sum(book => book.Chapters.Count).Should().Be(chapters);
        source.Books.Sum(book => book.Chapters.Sum(chapter => chapter.Verses.Count)).Should().Be(verses);
    }

    /// <summary>
    /// Two of the six are not whole Bibles and are not meant to be. Tyndale printed a New Testament
    /// and was burned before he finished the Old; the JPS TaNaKH has no New Testament to print. Both
    /// keep the canonical ordinal, so Matthew is book 40 in Tyndale as it is everywhere else, and
    /// its position within the text is 1.
    /// </summary>
    [Fact]
    public void TyndaleIsANewTestamentAndKeepsTheCanonicalOrdinals()
    {
        english.Tyndale.Books.Select(book => book.CanonicalOrdinal).Should().Equal(Enumerable.Range(40, 27));
        english.Tyndale.Books.Select(book => book.Position).Should().Equal(Enumerable.Range(1, 27));
    }

    [Fact]
    public void TheJpsIsAnOldTestamentAndKeepsTheCanonicalOrdinals()
    {
        english.JewishPublicationSociety.Books.Select(book => book.CanonicalOrdinal)
            .Should().Equal(Enumerable.Range(1, 39));
        english.JewishPublicationSociety.Books.Should()
            .OnlyContain(book => book.Position == book.CanonicalOrdinal);
    }

    [Theory]
    [InlineData(EnglishTextSource.Geneva)]
    [InlineData(EnglishTextSource.AmericanStandard)]
    [InlineData(EnglishTextSource.Young)]
    [InlineData(EnglishTextSource.WorldEnglish)]
    public void EveryBookStandsWhereTheCanonPutsIt(string slug)
    {
        var source = Source(slug);

        source.Books.Select(book => book.CanonicalOrdinal).Should().Equal(Enumerable.Range(1, 66));
        source.Books.Should().OnlyContain(book => book.Position == book.CanonicalOrdinal);
    }

    /// <summary>
    /// That Tyndale's spelling is his and not a modernisation. Several digital Tyndales are in
    /// modern spelling, and a modern-spelling Tyndale is close enough to the King James to be worth
    /// nothing here: the whole reason to hold this text is that it is what 1611 revised.
    /// </summary>
    [Fact]
    public void TyndaleIsInHisOwnSpelling()
    {
        Text(english.Verse(english.Tyndale, 40, 1, 1))
            .Should().Be("This is the boke of the generacion of Iesus Christ the sonne of Dauid the sonne also of Abraham.");
        Text(english.Verse(english.Tyndale, 43, 3, 16))
            .Should().StartWith("For God so loveth the worlde that he hath geven his only sonne");
    }

    /// <summary>
    /// And that the Geneva's is too, by the reading the edition is named for: it is the Breeches
    /// Bible because Genesis 3:7 ends this way and no other English version does.
    /// </summary>
    [Fact]
    public void TheGenevaIsTheBreechesBibleInItsOwnSpelling()
    {
        Text(english.Verse(english.Geneva, 1, 3, 7)).Should().EndWith("and made them selues breeches.");
        Text(english.Verse(english.Geneva, 43, 3, 16))
            .Should().StartWith("For God so loued the worlde, that hee hath giuen his onely begotten Sonne");
    }

    /// <summary>
    /// Young translated by rule rather than by idiom, which is the whole reason this text is the
    /// most valuable of the six: an English word here stands for one original word far more often
    /// than in any other version, so where an aligner disagrees with it the disagreement is worth
    /// reading. Neither of these two readings is idiomatic English and both are what a word-for-word
    /// rendering produces.
    /// </summary>
    [Fact]
    public void YoungIsLiteralToTheOriginalRatherThanToEnglish()
    {
        Text(english.Verse(english.Young, 1, 1, 1))
            .Should().StartWith("In the beginning of God's preparing the heavens and the earth");
        Text(english.Verse(english.Young, 43, 3, 16)).Should().Contain("may have life age-during");
    }

    /// <summary>
    /// The JPS is a Jewish translation of the Masoretic text and not a Christian one: it
    /// transliterates the name of the place at Genesis 22:14 where a Christian version renders it,
    /// and its speech is in single quotation marks throughout.
    /// </summary>
    [Fact]
    public void TheJpsRendersTheHebrewAsAJewishTranslationDoes()
    {
        Text(english.Verse(english.JewishPublicationSociety, 1, 22, 14))
            .Should().Contain("Adonai-jireh").And.Contain("'In the mount where the LORD is seen.'");
        Text(english.Verse(english.JewishPublicationSociety, 19, 23, 1))
            .Should().Be("A Psalm of David. The LORD is my shepherd; I shall not want.");
    }

    /// <summary>
    /// Which Greek each New Testament follows, read out of the file rather than repeated from the
    /// literature. Three readings separate the three traditions, and between them they tell all
    /// three apart: the Textus Receptus prints Acts 8:37 and the doxology of the Lord's Prayer, the
    /// critical text prints neither, and the Byzantine Majority Text prints the doxology and not
    /// Acts 8:37.
    /// </summary>
    [Theory]
    [InlineData(EnglishTextSource.Tyndale, true, true)]
    [InlineData(EnglishTextSource.Geneva, true, true)]
    [InlineData(EnglishTextSource.Young, true, true)]
    [InlineData(EnglishTextSource.AmericanStandard, false, false)]
    [InlineData(EnglishTextSource.WorldEnglish, false, true)]
    public void EachNewTestamentShowsWhichGreekItFollows(string slug, bool actsEight37, bool doxology)
    {
        var source = Source(slug);

        english.Holds(source, 44, 8, 37).Should().Be(actsEight37);

        // "the power, and the glory", in five spellings and with the kingdom rendered as the reign
        // in Young's, so the word looked for is the one all five of them spell the same way.
        Text(english.Verse(source, 40, 6, 13)).Contains("power", StringComparison.OrdinalIgnoreCase)
            .Should().Be(doxology);
    }

    /// <summary>
    /// And the World English Bible's Byzantine base again, from the one place its verse addresses
    /// differ from the American Standard's at all: the Byzantine manuscripts close Romans with the
    /// doxology at the end of chapter 14, and this edition numbers it 14:24-26 with 16:25 left
    /// empty.
    /// </summary>
    [Fact]
    public void TheWorldEnglishBiblePutsTheRomansDoxologyWhereTheByzantineTextDoes()
    {
        Text(english.Verse(english.WorldEnglish, 45, 14, 24)).Should().StartWith("Now to him who is able");
        english.Holds(english.WorldEnglish, 45, 16, 25).Should().BeFalse();
        Text(english.Verse(english.AmericanStandard, 45, 16, 25)).Should().StartWith("Now to him that is able");
    }

    /// <summary>
    /// That the World English Bible loaded is the updated edition and not the Classic, which is a
    /// different text under the same name: the Classic renders the divine name Yahweh 6,902 times
    /// and this one renders it LORD.
    /// </summary>
    [Fact]
    public void TheWorldEnglishBibleIsTheUpdatedEditionAndNotTheClassic()
    {
        Text(english.Verse(english.WorldEnglish, 19, 23, 1)).Should().Contain("The LORD is my shepherd");
        english.Words(english.WorldEnglish).Count(word => word.Surface == "Yahweh").Should().BeLessThan(200);
    }

    /// <summary>
    /// The American Standard renders the divine name as Jehovah, which is the difference a reader
    /// sees in the first psalm they open and the reason it reads unlike every other English text
    /// here.
    /// </summary>
    [Fact]
    public void TheAmericanStandardWritesTheDivineNameAsJehovah()
    {
        // The psalm's superscription stands inside its first verse in every text here, so the
        // reading looked for is inside the verse rather than at the head of it.
        Text(english.Verse(english.AmericanStandard, 19, 23, 1)).Should().Contain("Jehovah is my shepherd");
        Text(english.Verse(english.AmericanStandard, 1, 1, 1))
            .Should().Be("In the beginning God created the heavens and the earth.");
    }

    /// <summary>
    /// The JPS was renumbered to the English scheme by its publisher and says so verse by verse, in
    /// the numbering its own readers hold. That is worth more here than in any other text renumbered
    /// this way: this is a translation of the Masoretic text standing beside BHSA, which is numbered
    /// the Masoretic way, so the address it states is the address of the Hebrew it renders. Most of
    /// them are in the Psalms, where the Hebrew counts the superscription as verse one.
    /// </summary>
    [Fact]
    public void TheJpsSaysWhereEachVerseStandsInTheHebrewNumbering()
    {
        var verses = english.JewishPublicationSociety.Books
            .SelectMany(book => book.Chapters)
            .SelectMany(chapter => chapter.Verses)
            .ToList();

        verses.Sum(verse => verse.Stated.Count).Should().Be(2056);

        // The first verse of the third psalm holds the superscription and the line after it, which
        // the Hebrew numbers as two verses and the English as one, so this verse states both — and
        // it opens before the second of them, which is what makes the verse two verses of Hebrew
        // rather than one at a different address.
        var opening = english.Verse(english.JewishPublicationSociety, 19, 3, 1);
        opening.Stated.Should().Equal(new StatedNumberDraft(3, 1), new StatedNumberDraft(3, 2));
        opening.OpensBeforeItsStatedAddress.Should().BeTrue();
    }

    /// <summary>
    /// And that only the JPS does. A number in brackets is an address in one file and a footnote
    /// mark in another, so a text reading them where its publisher never wrote any would be putting
    /// this reader's guess into the corpus as the edition's numbering.
    /// </summary>
    [Theory]
    [InlineData(EnglishTextSource.Tyndale)]
    [InlineData(EnglishTextSource.Geneva)]
    [InlineData(EnglishTextSource.AmericanStandard)]
    [InlineData(EnglishTextSource.Young)]
    [InlineData(EnglishTextSource.WorldEnglish)]
    public void TheOthersStateNoNumberingOfTheirOwn(string slug) =>
        Source(slug).Books
            .SelectMany(book => book.Chapters)
            .SelectMany(chapter => chapter.Verses)
            .Sum(verse => verse.Stated.Count)
            .Should().Be(0);

    /// <summary>
    /// None of the six carries a Strong number, although two of the files carry hundreds of
    /// thousands: 705,378 in the American Standard and 683,868 in the World English Bible. That
    /// tagging is not a word-level claim — it puts 23 tags on an average Old Testament verse drawn
    /// from a pool of 8.4 distinct numbers, so each Hebrew word's number lands on about three
    /// English tokens, and 59% of the tags stand on English function words — and persisting it
    /// would be an inference stored where a reader takes a sourced claim to be. RUL-0024.
    ///
    /// It is a test because it is a decision and not a fact about the file: a later pass "fixing"
    /// the missing numbers by reading them off the same files would undo it silently.
    /// </summary>
    [Theory]
    [InlineData(EnglishTextSource.Tyndale)]
    [InlineData(EnglishTextSource.Geneva)]
    [InlineData(EnglishTextSource.AmericanStandard)]
    [InlineData(EnglishTextSource.Young)]
    [InlineData(EnglishTextSource.WorldEnglish)]
    [InlineData(EnglishTextSource.JewishPublicationSociety)]
    public void NoneOfThemCarriesAStrongNumber(string slug) =>
        english.Words(Source(slug)).Should().OnlyContain(word => word.StrongNumber == null);

    /// <summary>
    /// What the American Standard does carry is its own italics: 4,316 spans the revisers marked as
    /// words they supplied, which reach the same column the Synodal's square brackets and the
    /// Reina-Valera's do. It is the edition's own claim about its own words, which the Strong layer
    /// beside it is not.
    /// </summary>
    [Fact]
    public void TheAmericanStandardMarksTheWordsItSupplies()
    {
        var verses = english.AmericanStandard.Books
            .SelectMany(book => book.Chapters)
            .SelectMany(chapter => chapter.Verses)
            .ToList();

        verses.Sum(verse => verse.Words
                .Where(word => word.SuppliedSpan is not null)
                .Select(word => word.SuppliedSpan)
                .Distinct()
                .Count())
            .Should().Be(4316);
    }

    /// <summary>
    /// And that the other five mark nothing at all, which is worth checking rather than assuming:
    /// annotation appearing on a text whose edition supplied none is the failure RUL-0024 exists
    /// for, and this is what would catch it.
    /// </summary>
    [Theory]
    [InlineData(EnglishTextSource.Tyndale)]
    [InlineData(EnglishTextSource.Geneva)]
    [InlineData(EnglishTextSource.Young)]
    [InlineData(EnglishTextSource.WorldEnglish)]
    [InlineData(EnglishTextSource.JewishPublicationSociety)]
    public void TheOthersCarryNoAnnotationAtAll(string slug) =>
        english.Words(Source(slug)).Should().OnlyContain(word =>
            word.Lemma == null && word.StrongNumber == null && word.Gloss == null
            && word.Morphology == null && word.SuppliedSpan == null);

    /// <summary>
    /// What the reader gets out of each, which no catalogue states and only counting establishes. A
    /// number here that moves without the verse counts moving is the tokeniser changing its mind,
    /// which is the change that would silently re-align every link ever built from these texts.
    /// </summary>
    [Theory]
    [InlineData(EnglishTextSource.Tyndale, 181998)]
    [InlineData(EnglishTextSource.Geneva, 783932)]
    [InlineData(EnglishTextSource.AmericanStandard, 784713)]
    [InlineData(EnglishTextSource.Young, 786938)]
    [InlineData(EnglishTextSource.WorldEnglish, 756240)]
    [InlineData(EnglishTextSource.JewishPublicationSociety, 611118)]
    public void EveryWordIsCounted(string slug, int words) =>
        english.Words(Source(slug)).Should().HaveCount(words);

    /// <summary>
    /// Every book is named as its own edition names it, from the file's table-of-contents line —
    /// which for these is the seventeenth-century title rather than the one-word modern name, and
    /// is the only thing any of these files says in its own words about a book as a whole.
    /// </summary>
    [Theory]
    [InlineData(EnglishTextSource.Geneva, 1, "The First Book of Moses, called Genesis")]
    [InlineData(EnglishTextSource.Tyndale, 40, "THE GOSPEL ACCORDING TO ST. MATTHEW")]
    [InlineData(EnglishTextSource.AmericanStandard, 1, "The First Book of Moses, Commonly Called Genesis")]
    public void EveryBookIsNamedAsItsEditionNamesIt(string slug, int ordinal, string name)
    {
        var source = Source(slug);

        source.Books.Should().OnlyContain(book => book.NameNative != null && book.NameNative != "");
        source.Books.Single(book => book.CanonicalOrdinal == ordinal).NameNative.Should().Be(name);
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
            Path.Combine(Path.GetTempPath(), $"english-{Guid.NewGuid():n}", "Young1898"));

        try
        {
            var act = () => EnglishTextSource.Read(empty.FullName);

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
        var unknown = Directory.CreateTempSubdirectory("DouayRheims1899");

        try
        {
            var act = () => EnglishTextSource.Read(unknown.FullName);

            act.Should().Throw<ArgumentException>().WithMessage("*licence and provenance*");
        }
        finally
        {
            unknown.Delete(recursive: true);
        }
    }

    private TextSource Source(string slug) => slug switch
    {
        EnglishTextSource.Tyndale => english.Tyndale,
        EnglishTextSource.Geneva => english.Geneva,
        EnglishTextSource.AmericanStandard => english.AmericanStandard,
        EnglishTextSource.Young => english.Young,
        EnglishTextSource.WorldEnglish => english.WorldEnglish,
        EnglishTextSource.JewishPublicationSociety => english.JewishPublicationSociety,
        _ => throw new ArgumentOutOfRangeException(nameof(slug), slug, "That is not one of the six."),
    };

    private static string Text(VerseDraft verse) =>
        string.Concat(verse.Words.Select(word => word.Surface + word.Trailer)).Trim();
}
