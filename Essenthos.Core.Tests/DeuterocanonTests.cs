using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading;
using Essenthos.Core.Loading.Frame;
using Essenthos.Core.Loading.Links;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>The deuterocanonical texts and books, read once: six folders of USFM is a few seconds.</summary>
public sealed class Deuterocanon
{
    private static string Resources => TestResources.Folder(string.Empty);

    internal TextSource Synodal { get; } = DeuterocanonTextSource.Extend(
        Bible4uTextSource.Read(TestResources.Bible4u("RUSV"), "RUSV"), Resources);

    internal TextSource KingJames { get; } = DeuterocanonTextSource.Extend(
        Bible4uTextSource.Read(TestResources.Bible4u("KJV"), "KJV"), Resources);

    internal TextSource WorldEnglish { get; } = DeuterocanonTextSource.Extend(
        EnglishTextSource.Read(TestResources.EbibleFolder("WorldEnglish")), Resources);

    internal TextSource Brenton { get; } =
        DeuterocanonTextSource.Read(TestResources.EbibleFolder(DeuterocanonTextSource.BrentonFolder));

    internal TextSource DouayRheims { get; } =
        DeuterocanonTextSource.Read(TestResources.EbibleFolder(DeuterocanonTextSource.DouayRheimsFolder));

    internal TextSource Vulgate { get; } =
        DeuterocanonTextSource.Read(TestResources.EbibleFolder(DeuterocanonTextSource.VulgateFolder));

    internal static BookDraft Book(TextSource source, int ordinal) =>
        source.Books.Single(book => book.CanonicalOrdinal == ordinal);

    internal static int Verses(BookDraft book) => book.Chapters.Sum(chapter => chapter.Verses.Count);

    internal static string Text(TextSource source, int ordinal, int chapter, int verse) => string.Concat(
        Book(source, ordinal).Chapters.Single(one => one.Number == chapter).Verses.Single(one => one.Number == verse)
            .Words.Select(word => word.Surface + word.Trailer));

    internal static VersificationFrame Frame(TextSource source, VersificationRules rules) =>
        rules.Frame(source.Definition.Versification, EditionShape.Of(source.Books.SelectMany(book =>
            book.Chapters.SelectMany(chapter => chapter.Verses.Select(verse =>
                (book.CanonicalOrdinal, chapter.Number, verse.Number, verse.Label,
                    verse.Words.Sum(word => word.Surface.Length)))))));
}

/// <summary>
/// The books the Greek and Latin Bibles hold beyond the sixty-six: which text gains which, under which
/// ordinal, and that nothing the editions print as apparatus or as somebody else's work is read as
/// scripture.
/// </summary>
[Trait(TestCategory.Name, TestCategory.Corpus)]
public class DeuterocanonReaderTests(Deuterocanon read) : IClassFixture<Deuterocanon>
{
    private const int FirstEsdras = 68;

    private const int SecondEsdras = 69;

    private const int FirstMaccabees = 73;

    private const int SecondMaccabees = 74;

    private const int ThirdMaccabees = 80;

    private const int FourthMaccabees = 81;

    /// <summary>
    /// The eleven, with the verses each page held at the revision taken. The Synodal's second book of
    /// Ezra is the Greek 1 Esdras and its third the Latin 2 Esdras, and the text of each says so: the
    /// one opens with Josiah's passover, the other with the prophet's genealogy.
    /// </summary>
    [Theory]
    [InlineData(68, 442, "2-я Ездры")]
    [InlineData(70, 244, "Товит")]
    [InlineData(71, 340, "Иудифь")]
    [InlineData(75, 440, "Премудрость Соломона")]
    [InlineData(72, 1523, "Премудрость Иисуса, сына Сирахова")]
    [InlineData(76, 72, "Послание Иеремии")]
    [InlineData(67, 141, "Варух")]
    [InlineData(73, 924, "1-я Маккавейская")]
    [InlineData(74, 556, "2-я Маккавейская")]
    [InlineData(80, 180, "3-я Маккавейская")]
    [InlineData(69, 875, "3-я Ездры")]
    public void TheSynodalGainsItsElevenNonCanonicalBooks(int ordinal, int verses, string name)
    {
        var book = Deuterocanon.Book(read.Synodal, ordinal);

        Deuterocanon.Verses(book).Should().Be(verses);
        book.NameNative.Should().Be(name);
        read.Synodal.Books.Should().HaveCount(66 + 11);
        Deuterocanon.Text(read.Synodal, FirstEsdras, 1, 1).Should().StartWith("И совершил Иосия");
        Deuterocanon.Text(read.Synodal, SecondEsdras, 1, 1).Should().StartWith("Вторая книга Ездры пророка");
        Deuterocanon.Text(read.Synodal, FirstMaccabees, 1, 1).Should().StartWith("После того как Александр");
    }

    /// <summary>
    /// Stands where the church prints it — 2 Ezra, Tobit and Judith after Nehemiah — and the italics
    /// the translators set for words of their own are supplied words, as the brackets are elsewhere.
    /// </summary>
    [Fact]
    public void TheSynodalsBooksStandInItsOwnOrderWithItsSuppliedWordsMarked()
    {
        read.Synodal.Books.OrderBy(book => book.Position).Select(book => book.CanonicalOrdinal)
            .Should().Equal(Canons.Find(Canons.Synodal)!.Ordinals);
        read.Synodal.Definition.PartSources.Should().Equal(DeuterocanonTextSource.SynodalSource);

        var supplied = Deuterocanon.Book(read.Synodal, FirstMaccabees).Chapters.Single(chapter => chapter.Number == 16)
            .Verses.Single(verse => verse.Number == 21).Words.Where(word => word.SuppliedSpan is not null);
        supplied.Select(word => word.Surface).Should().Equal("Птоломей");
    }

    /// <summary>
    /// The prologue of Sirach has no verse number and stands at the head of the first verse, as the
    /// King James's does; its title is the page's heading, not a word of it.
    /// </summary>
    [Fact]
    public void SirachsPrologueOpensItsFirstVerse()
    {
        Deuterocanon.Text(read.Synodal, 72, 1, 1).Should().StartWith("Многое и великое дано нам")
            .And.Contain("Всякая премудрость — от Господа");
    }

    [Fact]
    public void TheKingJamesGainsItsApocryphaBetweenTheTestaments()
    {
        read.KingJames.Books.Should().HaveCount(66 + 12);
        read.KingJames.Books.Select(book => book.CanonicalOrdinal).Should()
            .Contain([FirstEsdras, SecondEsdras, 70, 71, 72, 67, 75, 77, 78, 79, FirstMaccabees, SecondMaccabees]);
        Deuterocanon.Book(read.KingJames, FirstEsdras).Position.Should().Be(40, "the Apocrypha follows Malachi");
        Deuterocanon.Book(read.KingJames, 40).Position.Should().Be(52);
        Deuterocanon.Text(read.KingJames, SecondEsdras, 1, 1).Should().StartWith("The second book of the prophet Esdras");
        Deuterocanon.Verses(Deuterocanon.Book(read.KingJames, FirstMaccabees)).Should().Be(924);
        Deuterocanon.Book(read.KingJames, 67).Chapters.Should().HaveCount(6, "Baruch keeps the letter it prints as its sixth chapter");
    }

    [Fact]
    public void TheWorldEnglishBibleGainsThirteenBooksAndNoneNamesYahweh()
    {
        var gained = read.WorldEnglish.Books.Where(book => book.CanonicalOrdinal > 66).ToList();

        gained.Select(book => book.CanonicalOrdinal).Should()
            .BeEquivalentTo([70, 71, 75, 72, 67, FirstMaccabees, SecondMaccabees, FirstEsdras, 79, 82, ThirdMaccabees, SecondEsdras, FourthMaccabees]);
        gained.SelectMany(book => book.Chapters).SelectMany(chapter => chapter.Verses).SelectMany(verse => verse.Words)
            .Should().NotContain(word => word.Surface.Contains("Yahweh"));
        read.WorldEnglish.Definition.PartSources.Should().Equal(DeuterocanonTextSource.WorldEnglishSource);
        read.WorldEnglish.Definition.RightsNote.Should().Be(EnglishTextSource.Definitions["WorldEnglish"].RightsNote);
    }

    /// <summary>
    /// Brenton's English is his Greek's translation verse for verse: in every book both hold, the two
    /// print the same verse numbers in every chapter, the Greek's lettered pieces aside — except 1
    /// Samuel 17, where eBible sets in the twenty verses Brenton translated from Alexandrinus in an
    /// appendix, and four chapters of Nehemiah, which the English numbers as the English Bibles do and
    /// the Greek as the Hebrew does.
    /// </summary>
    [Fact]
    public void BrentonsEnglishIsDividedAsHisGreekIs()
    {
        var greek = SeptuagintTextSource.Read(TestResources.SeptuagintFolder);

        read.Brenton.Books.Should().HaveCount(53);
        read.Brenton.Books.Sum(Deuterocanon.Verses).Should().Be(29_005);
        read.Brenton.Books.Select(book => book.CanonicalOrdinal).Should()
            .Contain([FirstMaccabees, SecondMaccabees, ThirdMaccabees, FourthMaccabees]);

        var differing = new List<string>();
        foreach (var book in greek.Books)
        {
            var english = Deuterocanon.Book(read.Brenton, book.CanonicalOrdinal);
            foreach (var chapter in book.Chapters)
            {
                var printed = english.Chapters.SingleOrDefault(one => one.Number == chapter.Number)?.Verses
                    .Where(verse => verse.Words.Count > 0).Select(verse => verse.Number).Distinct() ?? [];
                if (!printed.SequenceEqual(chapter.Verses.Select(verse => verse.Number).Distinct()))
                {
                    differing.Add($"{book.CanonicalOrdinal}.{chapter.Number}");
                }
            }
        }

        differing.Should().BeEquivalentTo(["9.17", "16.3", "16.4", "16.9", "16.10"]);
    }

    /// <summary>
    /// Susanna and Bel come out of Daniel 13 and 14 as the versification data's Latin rules place them,
    /// each verse keeping the number the Vulgate prints it under, and meet the Greek's verse for verse.
    /// </summary>
    [Fact]
    public void TheVulgatesSusannaAndBelAreBooksOfTheirOwn()
    {
        foreach (var source in new[] { read.Vulgate, read.DouayRheims })
        {
            var susanna = Deuterocanon.Book(source, 77).Chapters.Single().Verses;
            var bel = Deuterocanon.Book(source, 78).Chapters.Single().Verses;

            susanna.Select(verse => verse.Number).Should().Equal(Enumerable.Range(1, 64));
            susanna[0].Stated.Should().Equal(new StatedNumberDraft(13, 1));
            bel.Select(verse => (verse.Number, verse.Label)).Should().Equal(
                [.. Enumerable.Range(1, 42).Select(number => (number, "")), (42, "a")]);
            bel[0].Stated.Should().Equal(new StatedNumberDraft(13, 65));
            bel[1].Stated.Should().Equal(new StatedNumberDraft(14, 1));
            bel[^1].Stated.Should().Equal(new StatedNumberDraft(14, 42));
            source.Books.OrderBy(book => book.Position).Select(book => book.CanonicalOrdinal)
                .SkipWhile(ordinal => ordinal != 27).Take(3).Should().Equal(27, 77, 78);
        }

        Deuterocanon.Text(read.Vulgate, 77, 1, 1).Should().StartWith("Et erat vir habitans in Babylone");
    }

    [Fact]
    public void TheLatinTextsHoldTheSeventyThreeAndNothingThatIsNotTheirs()
    {
        read.Vulgate.Books.Should().HaveCount(75);
        read.DouayRheims.Books.Should().HaveCount(75);
        read.Vulgate.Books.Sum(Deuterocanon.Verses).Should().Be(35_809);
        read.DouayRheims.Books.Sum(Deuterocanon.Verses).Should().Be(35_811);

        read.Vulgate.Books.SelectMany(book => book.Chapters).SelectMany(chapter => chapter.Verses)
            .Should().OnlyContain(verse => verse.Notes.Count == 0, "the Glossa Ordinaria is a commentary");
        read.DouayRheims.Books.SelectMany(book => book.Chapters).SelectMany(chapter => chapter.Verses)
            .SelectMany(verse => verse.Words).Should().OnlyContain(word => word.StrongNumber == null);

        Deuterocanon.Book(read.Vulgate, 17).Chapters.Should().HaveCount(16, "the Vulgate's Esther gathers its additions at the end");
        Deuterocanon.Book(read.Vulgate, 27).Chapters.Should().HaveCount(12);
        read.Vulgate.Books.OrderBy(book => book.Position).Select(book => book.CanonicalOrdinal).Take(19).Should()
            .Equal([.. Enumerable.Range(1, 16), 70, 71, 17], "Tobit and Judith stand before Esther in the Vulgate");
        Deuterocanon.Text(read.Vulgate, FirstMaccabees, 1, 1).Should().StartWith("Et factum est");
    }
}

/// <summary>
/// The frame places the two books of Maccabees from the Latin's rules alone: an edition numbered as
/// the Vulgate moves to the shared numbers, and the Greek and English editions stay at their own.
/// </summary>
[Trait(TestCategory.Name, TestCategory.Corpus)]
public class MaccabeesFrameTests(Deuterocanon read) : IClassFixture<Deuterocanon>
{
    private const int FirstMaccabees = 73;

    private const int SecondMaccabees = 74;

    private static readonly VersificationRules Rules = TvtmsReader.Read(TestResources.Tvtms);

    [Fact]
    public void TheGreekAndEnglishEditionsStayWhereTheyNumberThemselves()
    {
        TextSource[] sources =
        [
            SeptuagintTextSource.Read(TestResources.SeptuagintFolder), SweteTextSource.Read(TestResources.SweteFolder),
            read.Brenton, read.KingJames, read.WorldEnglish,
        ];

        foreach (var source in sources)
        {
            var frame = Deuterocanon.Frame(source, Rules);
            foreach (var book in source.Books.Where(book => book.CanonicalOrdinal is FirstMaccabees or SecondMaccabees))
            {
                foreach (var chapter in book.Chapters)
                {
                    chapter.Verses.Should().OnlyContain(
                        verse => frame.Resolve(book.CanonicalOrdinal, chapter.Number, verse.Number, false)
                            .SequenceEqual(new[] { new CanonicalReference(book.CanonicalOrdinal, chapter.Number, verse.Number) }),
                        $"{source.Definition.Slug} {book.Name} {chapter.Number}");
                }
            }
        }
    }

    /// <summary>The four chapters of Nehemiah the two number otherwise meet in the frame.</summary>
    [Fact]
    public void BrentonsNehemiahMeetsHisGreekInTheFrame()
    {
        const int nehemiah = 16;
        var greek = Deuterocanon.Frame(SeptuagintTextSource.Read(TestResources.SeptuagintFolder), Rules);
        var english = Deuterocanon.Frame(read.Brenton, Rules);

        english.Resolve(nehemiah, 4, 1)[0].Should().Be(greek.Resolve(nehemiah, 3, 33)[0]);
        english.Resolve(nehemiah, 9, 38)[0].Should().Be(greek.Resolve(nehemiah, 10, 1)[0]);
    }

    [Fact]
    public void TheVulgateIsPlacedByTheLatinRules()
    {
        var frame = Deuterocanon.Frame(read.Vulgate, Rules);

        frame.Resolve(FirstMaccabees, 1, 67)[0].Should().Be(new CanonicalReference(FirstMaccabees, 1, 64));
        frame.Resolve(FirstMaccabees, 1, 43)[0].Should().Be(new CanonicalReference(FirstMaccabees, 1, 43));
        frame.Resolve(SecondMaccabees, 2, 33)[0].Should().Be(new CanonicalReference(SecondMaccabees, 2, 32));
        frame.Resolve(SecondMaccabees, 15, 40)[0].Should().Be(new CanonicalReference(SecondMaccabees, 15, 39));
    }

    /// <summary>
    /// The Synodal divides 2 Maccabees 2 as the Latin does, ending it at verse 33, and the data's test
    /// for that numbering is what places it.
    /// </summary>
    [Fact]
    public void TheSynodalsSecondMaccabeesTwoIsPlacedAsTheLatinIs()
    {
        var frame = Deuterocanon.Frame(read.Synodal, Rules);

        frame.Resolve(SecondMaccabees, 2, 33)[0].Should().Be(new CanonicalReference(SecondMaccabees, 2, 32));
        frame.Resolve(FirstMaccabees, 1, 64)[0].Should().Be(new CanonicalReference(FirstMaccabees, 1, 64));
    }
}

/// <summary>
/// A book the frame has no rules for is joined only in the chapters two texts print alike, and the
/// books a text gains after it was joined are joined on the next load.
/// </summary>
[Trait(TestCategory.Name, TestCategory.Corpus)]
[Collection(WitnessDatabaseCollection.Name)]
public sealed class DeuterocanonVerseLinkTests : IDisposable
{
    private const int Tobit = 70;

    private const int Wisdom = 75;

    private const int FirstMaccabees = 73;

    private readonly AppDbContext _db;

    public DeuterocanonVerseLinkTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _db.Database.ExecuteSqlRaw("DELETE FROM text");
    }

    public void Dispose()
    {
        _db.Database.ExecuteSqlRaw("DELETE FROM text");
        _db.Dispose();
    }

    /// <summary>
    /// 1 Maccabees from every text that has it, Sirach from the Synodal and the Douay-Rheims's Susanna and
    /// Bel, written and read back: the loader's round trip checks that every verse reads as its words
    /// and trailers rebuild it, which is where a reader that mistook a marker for text would show.
    /// </summary>
    [Fact]
    public async Task EveryTextsMaccabeesLoadsAndReadsBackAsItWasRead()
    {
        var read = new Deuterocanon();
        TextSource[] sources = [read.Synodal, read.KingJames, read.WorldEnglish, read.Brenton, read.DouayRheims, read.Vulgate];

        foreach (var source in sources)
        {
            var books = source.Books
                .Where(book => book.CanonicalOrdinal == FirstMaccabees
                               || (source.Definition.Slug == Sources.SynodalSlug && book.CanonicalOrdinal == Sirach)
                               || (source.Definition.Slug == DeuterocanonTextSource.DouayRheims
                                   && book.CanonicalOrdinal is Susanna or Bel))
                .ToList();
            var outcome = await new CorpusLoader(_db, NullLogger<CorpusLoader>.Instance)
                .Load(new TextSource(source.Definition, books));

            outcome.AlreadyLoaded.Should().BeFalse(source.Definition.Slug);
        }

        var verses = await _db.Verses.Where(verse => verse.Book!.CanonicalOrdinal == FirstMaccabees)
            .GroupBy(verse => verse.Text!.Slug)
            .ToDictionaryAsync(text => text.Key, text => text.Count());
        verses.Should().BeEquivalentTo(new Dictionary<string, int>
        {
            [Sources.SynodalSlug] = 924, [Sources.KingJamesSlug] = 924, [EnglishTextSource.WorldEnglish] = 924,
            [DeuterocanonTextSource.BrentonEnglish] = 924, [DeuterocanonTextSource.DouayRheims] = 929,
            [DeuterocanonTextSource.ClementineVulgate] = 929,
        });
    }

    private const int Sirach = 72;

    private const int Susanna = 77;

    private const int Bel = 78;

    [Fact]
    public void AChapterTheTwoDivideOtherwiseIsLeftOut()
    {
        var here = new Dictionary<(int, int, int), List<int>>
        {
            [(Wisdom, 5, 23)] = [1], [(Wisdom, 5, 24)] = [2], [(Wisdom, 6, 1)] = [3], [(1, 1, 1)] = [4],
        };
        var there = new Dictionary<(int, int, int), List<int>>
        {
            [(Wisdom, 5, 23)] = [11], [(Wisdom, 6, 1)] = [13], [(1, 1, 1)] = [14], [(1, 1, 2)] = [15],
        };

        var (mine, theirs) = VerseLinkLoader.Agreeing(here, there);

        mine.Keys.Should().BeEquivalentTo([(Wisdom, 6, 1), (1, 1, 1)]);
        theirs.Keys.Should().BeEquivalentTo([(Wisdom, 6, 1), (1, 1, 1), (1, 1, 2)],
            "Genesis is placed by the frame, and a verse it divides otherwise is its business");
    }

    [Fact]
    public async Task TheBooksATextGainsAreJoinedOnTheNextLoad()
    {
        var synodal = Bible4uTextSource.Definitions["RUSV"];
        await Load(Tiny(synodal, (FirstMaccabees, 1, 64), (Wisdom, 5, 24)));
        await Load(Tiny(SeptuagintTextSource.Definition(), (FirstMaccabees, 1, 64), (Wisdom, 5, 23), (Tobit, 1, 22)));
        await Place();

        var loader = new VerseLinkLoader(_db, NullLogger<VerseLinkLoader>.Instance);
        await loader.Load();
        (await Joined()).Should().BeEquivalentTo([FirstMaccabees], "Wisdom 5 is divided otherwise in the two");

        await new CorpusLoader(_db, NullLogger<CorpusLoader>.Instance)
            .AddMissingBooks(Tiny(synodal, (FirstMaccabees, 1, 64), (Wisdom, 5, 24), (Tobit, 1, 22)));
        await Place();

        (await loader.Load()).AlreadyLoaded.Should().BeFalse();
        (await Joined()).Should().BeEquivalentTo([FirstMaccabees, Tobit]);
        (await loader.Load()).AlreadyLoaded.Should().BeTrue("Wisdom has nothing left to join");
    }

    /// <summary>
    /// The King James prints the Letter of Jeremiah as Baruch 6 and the Septuagint as a book of its
    /// own, and each of its verses is joined to the same verse under the other name.
    /// </summary>
    [Fact]
    public async Task TheLetterOfJeremiahIsJoinedUnderEitherName()
    {
        await Load(Tiny(Bible4uTextSource.Definitions["KJV"], (LetterOfJeremiah.Baruch, LetterOfJeremiah.InBaruch, 73)));
        await Load(Tiny(SeptuagintTextSource.Definition(), (LetterOfJeremiah.Book, LetterOfJeremiah.Chapter, 73)));
        await Place();

        await new VerseLinkLoader(_db, NullLogger<VerseLinkLoader>.Instance).Load();

        var pairs = await _db.VerseLinkVerses
            .Where(member => member.Side == LinkSide.From && member.Verse!.Text!.Slug == Sources.KingJamesSlug
                             && member.VerseLink!.ToText!.Slug == Sources.BrentonSeptuagintSlug)
            .Select(member => new
            {
                English = member.Verse!.Number,
                Greek = member.VerseLink!.Verses
                    .Where(other => other.Side == LinkSide.To)
                    .Select(other => other.Verse!.Number)
                    .Single(),
            })
            .ToListAsync();

        pairs.Should().HaveCount(73);
        pairs.Should().OnlyContain(pair => pair.English == pair.Greek);
    }

    /// <summary>
    /// The source the gained books come from is credited on the text they join, once, whether the
    /// text is loaded with them or gains them later.
    /// </summary>
    [Fact]
    public async Task TheSourceOfTheGainedBooksIsCreditedOnTheText()
    {
        var synodal = Bible4uTextSource.Definitions["RUSV"];
        await Load(Tiny(synodal, (1, 1, 3)));

        var loader = new CorpusLoader(_db, NullLogger<CorpusLoader>.Instance);
        var gained = Tiny(synodal with { PartSources = [DeuterocanonTextSource.SynodalSource] }, (1, 1, 3), (FirstMaccabees, 1, 64));
        await loader.AddMissingBooks(gained);
        await loader.AddMissingBooks(gained);

        var text = await _db.Texts.SingleAsync();
        Database.Entities.TextPartSources.Of(text).Should().Equal(DeuterocanonTextSource.SynodalSource);
        (await _db.Books.CountAsync()).Should().Be(2);

        await Load(Tiny(Bible4uTextSource.Definitions["KJV"] with { PartSources = [DeuterocanonTextSource.KingJamesSource] },
            (FirstMaccabees, 1, 64)));
        Database.Entities.TextPartSources.Of(await _db.Texts.SingleAsync(t => t.Slug == Sources.KingJamesSlug))
            .Should().Equal(DeuterocanonTextSource.KingJamesSource);
    }

    private async Task<List<int>> Joined() => await _db.VerseLinkVerses
        .Where(member => member.Side == LinkSide.From && member.Verse!.Text!.Slug == Sources.SynodalSlug
                         && member.VerseLink!.ToText!.Slug == Sources.BrentonSeptuagintSlug)
        .Select(member => member.Verse!.Book!.CanonicalOrdinal)
        .Distinct()
        .ToListAsync();

    private async Task Place()
    {
        var rules = TvtmsReader.Read(TestResources.Tvtms);
        var placer = new CanonicalFrameLoader(_db, NullLogger<CanonicalFrameLoader>.Instance);
        foreach (var text in await _db.Texts.ToListAsync())
        {
            await placer.Place(text, rules);
        }
    }

    private async Task Load(TextSource source) =>
        await new CorpusLoader(_db, NullLogger<CorpusLoader>.Instance).Load(source);

    /// <summary>A text of one-word verses in one chapter of each book named.</summary>
    private static TextSource Tiny(TextDefinition definition, params (int Ordinal, int Chapter, int Verses)[] books) =>
        new(definition,
        [
            .. books.Select((book, index) => new BookDraft(
                book.Ordinal,
                index + 1,
                BookReferences.Name(book.Ordinal),
                BookReferences.Slug(book.Ordinal),
                [
                    new ChapterDraft(book.Chapter,
                    [
                        .. Enumerable.Range(1, book.Verses)
                            .Select(number => new VerseDraft(number, [new WordDraft("word", "")])),
                    ]),
                ])),
        ]);
}
