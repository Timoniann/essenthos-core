using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading;
using Essenthos.Core.XmlBible;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// Correcting what bible4u's King James and Synodal print wrong: read from the files and the
/// witnesses on this disk, made by the reader on a cold load and in place on a warm one.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class TextRepairTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly TextRepairLoader _loader;

    public TextRepairTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        Clear();
        _loader = new TextRepairLoader(_db, NullLogger<TextRepairLoader>.Instance);
    }

    public void Dispose()
    {
        Clear();
        _db.Dispose();
    }

    private void Clear() => _db.Database.ExecuteSqlRaw("DELETE FROM text");

    private static bool KingJamesOnDisk =>
        Directory.Exists(TestResources.KingJames2006Folder) && File.Exists(TestResources.Bible4u("KJV"));

    [Fact]
    public void TheKingJamesTakesThe1769sCapitalsReadingsAndSubscriptions()
    {
        if (!KingJamesOnDisk)
        {
            return;
        }

        var repairs = KingJamesRepairs();
        var printed = repairs.Verses.ToDictionary(r => (r.Book, r.Chapter, r.Verse), r => r.Printed);

        printed[(46, 9, 1)].Should().StartWith("Am I not an apostle? am I not free?");
        printed[(2, 38, 22)].Should().Contain("Bezaleel the son of Uri");
        printed[(24, 30, 11)].Should().Contain("saith the LORD, to save thee").And.Contain("yet will I not make");
        printed[(1, 2, 4)].Should().Contain("the LORD God made the earth");
        printed[(19, 110, 1)].Should().StartWith("The LORD said unto my Lord,");
        printed[(45, 16, 27)].Should().EndWith(
            "Amen. Written to the Romans from Corinthus, and sent by Phebe servant of the church at Cenchrea.");
        printed[(41, 12, 7)].Should().EndWith("shall be ours.");

        // Only the capitals change in a verse whose reading is right: the words stay the file's.
        var genesis = repairs.Verses.Single(r => r is { Book: 1, Chapter: 2, Verse: 4 });
        genesis.After.Select(t => t.Word.ToLowerInvariant()).Should().Equal(genesis.Before.Select(t => t.Word.ToLowerInvariant()));

        repairs.Verses.Count(r => r.After.Count > r.Before.Count && r.Book >= 45 && r.Book <= 58 &&
                                  r.Printed.Contains(" written", StringComparison.OrdinalIgnoreCase)).Should().Be(14);
        repairs.Verses.Count(r => !r.After.Select(t => t.Word.ToLowerInvariant())
                .SequenceEqual(r.Before.Select(t => t.Word.ToLowerInvariant())))
            .Should().Be(27 - 1 + 14 + 95,
                "every reading but the stray quotation mark changes the words, and so does every subscription and "
                + "each of the 95 verses printing the possessive of the divine name apart");
        repairs.Verses.SelectMany(r => r.After).Count(t => t.Word is "LORD" or "LORD's").Should().BeGreaterThan(6_400);
        repairs.Verses.SelectMany(r => r.Before).Should().NotContain(t => t.Word == "LORD");
    }

    [Fact]
    public void EveryCorrectionOfTheSynodalNamesATokenItsFilePrints()
    {
        if (!File.Exists(TestResources.Bible4u("RUSV")))
        {
            return;
        }

        var repairs = SynodalRepairs();
        var printed = repairs.Verses.ToDictionary(r => (r.Book, r.Chapter, r.Verse), r => r.Printed);

        SynodalCorrections.All.Should().HaveCount(209);
        repairs.Verses.Should().HaveCount(130);
        printed[(11, 7, 14)].Should().Contain("Отец его Тирянин был медник").And.Contain("и производил у него");
        printed[(1, 10, 13)].Should().Contain("От Мицраима произошли Лудим");
        printed[(6, 4, 10)].Should().Contain("сказать народу – так, как завещал");
        printed[(11, 7, 19)].Should().Contain("[наподобие лилии]");
        printed[(38, 3, 8)].Should().EndWith("ОТРАСЛЬ.");

        // Words run together come apart and dashes written as a letter become punctuation again, so
        // the verses gain 162 words between them; the 14 whose count stands are letters and names.
        repairs.Verses.Sum(r => r.After.Count - r.Before.Count).Should().Be(162);
        repairs.Verses.Where(r => r.After.Count == r.Before.Count).Should().HaveCount(14);
    }

    /// <summary>
    /// The real thing in five books: the King James loaded from its file, a link on a word that stays
    /// and on one that goes, then corrected in place.
    /// </summary>
    [Fact]
    public async Task AWarmKingJamesIsCorrectedWithEveryWordThatStaysKeepingItsRow()
    {
        if (!KingJamesOnDisk)
        {
            return;
        }

        int[] books = [2, 8, 17, 45, 46];
        await LoadFile("KJV", books, repairs: null);
        var passover = Words(2, 12, 11);
        var lord = passover.Single(w => w.Surface == "Lord");
        var witness = Corpus.Add(_db, "WIT", TextKind.CriticalEdition, "grc", (1, 1, ["λόγος", "θεός"]));
        _db.SaveChanges();

        var apostle = Words(46, 9, 1);
        apostle.Take(4).Select(w => w.Surface).Should().Equal("Am", "I", "am", "not");
        var doubled = Link(apostle[2], _db.WordAt(witness, 1, 1, 1));
        var kept = Link(apostle[3], _db.WordAt(witness, 1, 1, 2));
        var boaz = Words(8, 2, 4);

        var outcome = await _loader.Load(Only(KingJamesRepairs(), books));

        Verse(46, 9, 1).Should().StartWith("Am I not an apostle?");
        Words(46, 9, 1).Select(w => w.Id).Should().Equal(apostle.Where((_, at) => at != 2).Select(w => w.Id));
        _db.Links.Any(l => l.Id == doubled.Id).Should().BeFalse("a link naming only the doubled word says nothing any more");
        _db.Links.Any(l => l.Id == kept.Id).Should().BeTrue();

        Verse(8, 2, 4).Should().Contain("The LORD be with you. And they answered him, The LORD bless thee.");
        Words(8, 2, 4).Select(w => w.Id).Should().Equal(boaz.Select(w => w.Id));
        Words(8, 2, 4).Where(w => w.Surface == "LORD").Should().OnlyContain(w => w.NormalisedText == "lord");

        // The possessive the file printed apart is one word again, the name's own row.
        Verse(2, 12, 11).Should().EndWith("it is the LORD's passover.");
        Words(2, 12, 11).Should().HaveCount(passover.Count - 1).And.Contain(w => w.Id == lord.Id && w.Surface == "LORD's");

        Verse(17, 8, 5).Should().Contain("if I have found favour in his sight");
        Verse(45, 11, 6).Should().Contain("then is it no more grace");
        Verse(45, 16, 27).Should().EndWith("Cenchrea.");
        outcome.Moved.Should().Be(1);
        outcome.Reshaped.Should().Contain([(46, 9, 1), (17, 8, 5), (45, 16, 27)]);
        outcome.Reshaped.Should().NotContain((45, 11, 6), "a word moved is not a word gained or lost");

        var text = _db.Texts.AsNoTracking().Single(t => t.Slug == Bible4uTextSource.KingJames);
        text.RightsNote.Should().EndWith(Loading.KingJamesRepairs.Note);
        TextPartSources.Of(text).Should().Contain(Loading.KingJamesRepairs.Source);

        (await _loader.Load(Only(KingJamesRepairs(), books))).Verses.Should().Be(0);
    }

    /// <summary>A cold load and a warm one corrected in place end with the same words.</summary>
    [Fact]
    public async Task ACorrectedWarmKingJamesReadsAsACorrectedColdOne()
    {
        if (!KingJamesOnDisk)
        {
            return;
        }

        int[] books = [45];
        await LoadFile("KJV", books, repairs: null);
        await _loader.Load(Only(KingJamesRepairs(), books));
        var warm = Chapters(Bible4uTextSource.KingJames);
        Clear();

        await LoadFile("KJV", books, repairs: KingJamesRepairs());
        (await _loader.Load(Only(KingJamesRepairs(), books))).Verses.Should().Be(0);
        Chapters(Bible4uTextSource.KingJames).Should().Equal(warm);
    }

    /// <summary>
    /// A psalm's first verse that has gained its superscription at its head is still corrected: the
    /// file's words are found where they stand.
    /// </summary>
    [Fact]
    public async Task AVerseWithWordsWrittenBeforeItIsCorrectedWhereItsOwnWordsStand()
    {
        Corpus.Add(_db, "KJV", TextKind.Translation, "eng", (3, 1, ["A", "Psalm", "of", "David", "Lord", "how"]));
        _db.SaveChanges();

        var outcome = await _loader.Load(new TextRepairs("KJV", [new VerseRepair(1, 3, 1, "Lord how", "LORD how")], "noted"));

        outcome.Rewritten.Should().Be(1);
        Verse(1, 3, 1).Should().Be("A Psalm of David LORD how");
    }

    [Fact]
    public async Task AVerseReadingNeitherAsItsFileNorAsItsCorrectionStopsThePass()
    {
        Corpus.Add(_db, "KJV", TextKind.Translation, "eng", (9, 1, ["Am", "I", "free"]));
        _db.SaveChanges();

        var correcting = () => _loader.Load(new TextRepairs("KJV", [new VerseRepair(1, 9, 1, "Am I am not", "Am I not")], "noted"));

        await correcting.Should().ThrowAsync<InvalidOperationException>().WithMessage("*9:1*");
        Verse(1, 9, 1).Should().Be("Am I free");
    }

    /// <summary>
    /// The Synodal's words run together are divided into the words the edition prints, and a word
    /// misspelt is the same word put right: it keeps its row.
    /// </summary>
    [Fact]
    public async Task TheSynodalsGluedWordsAreDividedAndItsMisspeltOnesKeepTheirRows()
    {
        if (!File.Exists(TestResources.Bible4u("RUSV")))
        {
            return;
        }

        int[] books = [1, 11];
        await LoadFile("RUSV", books, repairs: null);
        var misspelt = Words(1, 10, 13).Single(w => w.Surface == "произщшли");
        var glued = Words(11, 7, 14).Count;
        var bracketed = SuppliedWords(11, 7, 19);
        bracketed.Should().Equal("наподобиелилии");

        var outcome = await _loader.Load(Only(SynodalRepairs(), books));

        Words(1, 10, 13).Single(w => w.Id == misspelt.Id).Surface.Should().Be("произошли");
        Verse(11, 7, 14).Should().Contain("Отец его Тирянин был медник");
        Words(11, 7, 14).Should().HaveCount(glued + 3);
        SuppliedWords(11, 7, 19).Should().Equal("наподобие", "лилии");
        outcome.AddedWords.Should().HaveCount(outcome.Added);
        outcome.Reshaped.Should().Contain((11, 7, 14)).And.NotContain((1, 10, 13));

        (await _loader.Load(Only(SynodalRepairs(), books))).Verses.Should().Be(0);
    }

    private static TextRepairs KingJamesRepairs() =>
        Bible4uTextSource.Repairs(TestResources.Bible4u("KJV"), "KJV", TestResources.Folder("."));

    private static TextRepairs SynodalRepairs() =>
        Bible4uTextSource.Repairs(TestResources.Bible4u("RUSV"), "RUSV", TestResources.Folder("."));

    private static TextRepairs Only(TextRepairs repairs, int[] books) =>
        repairs with { Verses = [.. repairs.Verses.Where(r => books.Contains(r.Book))] };

    private async Task LoadFile(string translation, int[] books, TextRepairs? repairs)
    {
        var bible = new XmlBibleParser().Parse(File.ReadAllText(TestResources.Bible4u(translation)));
        var source = Bible4uTextSource.Build(bible, Bible4uTextSource.Definitions[translation], repairs);
        await new CorpusLoader(_db, NullLogger<CorpusLoader>.Instance).Load(new TextSource(
            source.Definition,
            [.. source.Books.Where(book => books.Contains(book.CanonicalOrdinal))]));
    }

    private Link Link(Word rendering, Word witness)
    {
        var link = new Link
        {
            FromTextId = rendering.TextId,
            ToTextId = witness.TextId,
            Relation = LinkRelation.Renders,
            Method = LinkMethod.Aligner,
            Confidence = 0.5,
            Source = "a test",
        };
        _db.Links.Add(link);
        _db.LinkWords.Add(new LinkWord { Link = link, WordId = rendering.Id, Side = LinkSide.From });
        _db.LinkWords.Add(new LinkWord { Link = link, WordId = witness.Id, Side = LinkSide.To });
        _db.SaveChanges();
        return link;
    }

    private List<Word> Words(int book, int chapter, int verse) =>
    [
        .. _db.Words.AsNoTracking()
            .Where(w => w.Verse!.Book!.CanonicalOrdinal == book && w.Verse.ChapterNumber == chapter
                        && w.Verse.Number == verse && w.Text!.Kind == TextKind.Translation)
            .OrderBy(w => w.Position),
    ];

    private string Verse(int book, int chapter, int verse) =>
        string.Concat(Words(book, chapter, verse).Select(w => w.Surface + w.Trailer)).TrimEnd();

    private List<string> SuppliedWords(int book, int chapter, int verse)
    {
        var ids = Words(book, chapter, verse).Select(w => w.Id).ToList();
        return
        [
            .. _db.Words.AsNoTracking()
                .Where(w => ids.Contains(w.Id)
                            && _db.WordGroupWords.Any(gw => gw.WordId == w.Id && gw.WordGroup!.Kind == WordGroupKind.Supplied))
                .OrderBy(w => w.Position)
                .Select(w => w.Surface),
        ];
    }

    private List<string> Chapters(string slug) =>
    [
        .. _db.Verses.AsNoTracking()
            .Where(v => v.Text!.Slug == slug)
            .OrderBy(v => v.Book!.CanonicalOrdinal).ThenBy(v => v.ChapterNumber).ThenBy(v => v.Number)
            .Select(v => string.Concat(v.Words.OrderBy(w => w.Position).Select(w => w.Surface + w.Trailer))),
    ];
}
