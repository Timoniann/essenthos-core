using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Endpoints;
using Essenthos.Core.Loading;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// Writing the end of a verse onto a text whose file cut it short: Ohienko's Genesis 22:19 in
/// miniature, and then the seven verses of the real file against the real transcription.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class VerseEndingTests : IDisposable
{
    private const string Head = "І вернувсь Авраам до слуг своїх.";

    private const string Whole =
        "І вернувсь Авраам до слуг своїх. І встали вони, та й пішли разом до Беер-Шеви. І осів Авраам у Беер-Шеві.";

    private readonly AppDbContext _db;
    private readonly VerseEndingLoader _loader;

    public VerseEndingTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        Clear();
        _loader = new VerseEndingLoader(_db, NullLogger<VerseEndingLoader>.Instance);
    }

    public void Dispose()
    {
        Clear();
        _db.Dispose();
    }

    private void Clear() => _db.Database.ExecuteSqlRaw("DELETE FROM text");

    [Fact]
    public async Task TheLostWordsAreWrittenAfterTheOnesTheVerseHad()
    {
        Ukrainian();
        var before = Words(22, 19).Select(w => w.Id).ToList();

        var outcome = await _loader.Load("UBIO", [Ending(22, 19, Whole)], "restored");

        outcome.Verses.Should().Be(1);
        outcome.Words.Should().Be(14);
        Verse(22, 19).Should().Be(Whole);

        // The words the verse had keep their rows, and so whatever stands on them.
        Words(22, 19).Take(before.Count).Select(w => w.Id).Should().Equal(before);
    }

    /// <summary>
    /// Nothing says which Hebrew word the restored words render, so they arrive with no Strong
    /// number and in no link rather than with a guess.
    /// </summary>
    [Fact]
    public async Task TheRestoredWordsCarryNoStrongNumberAndNoLink()
    {
        Ukrainian();

        await _loader.Load("UBIO", [Ending(22, 19, Whole)], "restored");

        var added = Words(22, 19).Skip(6).ToList();
        added.Should().HaveCount(14).And.OnlyContain(w => w.StrongNumber == null);
        var ids = added.Select(w => w.Id).ToList();
        _db.LinkWords.Count(lw => ids.Contains(lw.WordId)).Should().Be(0);
    }

    [Fact]
    public async Task ASecondRunWritesNothingAndNamesTheSourceOnce()
    {
        Ukrainian();

        await _loader.Load("UBIO", [Ending(22, 19, Whole)], "From Wikisource, CC BY-SA 4.0.", LostVerseEndings.OhienkoPart);
        var again = await _loader.Load("UBIO", [Ending(22, 19, Whole)], "From Wikisource, CC BY-SA 4.0.", LostVerseEndings.OhienkoPart);

        again.Verses.Should().Be(0);
        Verse(22, 19).Should().Be(Whole);
        var text = _db.Texts.AsNoTracking().Single(t => t.Slug == "UBIO");
        text.RightsNote.Should().Be("From Wikisource, CC BY-SA 4.0.");
        TextPartSources.Of(text).Should().Equal(LostVerseEndings.OhienkoPart);
    }

    /// <summary>
    /// A verse whose words are not the head of the edition's is a different verse or a different
    /// text, and completing it would be writing words where nobody checked they belong.
    /// </summary>
    [Fact]
    public async Task AVerseThatIsNotTheHeadOfTheEditionsStopsThePassBeforeAnythingIsWritten()
    {
        Corpus.Add(_db, "UBIO", TextKind.Translation, "ukr",
            (22, 19, Head.Split(' ')),
            (22, 20, ["І", "сталося"]));
        _db.SaveChanges();

        var writing = () => _loader.Load(
            "UBIO",
            [Ending(22, 19, Whole), Ending(22, 20, "Ось також Мілка вродила сини.")],
            "restored");

        await writing.Should().ThrowAsync<InvalidOperationException>().WithMessage("*22:20*");
        Verse(22, 19).Should().Be(Head);
    }

    [Fact]
    public void TheTranscriptionIsWrittenInThePunctuationTheLoadedTextUses()
    {
        LostVerseEndings.InTheLoadedPunctuation(
                "І сказав Господь до сатани́: „Звідки ти йдеш?“ А сатана́ відповів Господе́ві й сказав: " +
                "„Я мандрував по землі та й перейшов її“.")
            .Should().Be(
                "І сказав Господь до сатани: Звідки ти йдеш? А сатана відповів Господеві й сказав: " +
                "Я мандрував по землі та й перейшов її.");

        LostVerseEndings.InTheLoadedPunctuation("Отож, Господь Бог допоможе Мені, — хто ж отой ?")
            .Should().Be("Отож, Господь Бог допоможе Мені, хто ж отой?");
    }

    [Fact]
    public void ALatinLetterInTheTranscriptionIsRefused()
    {
        var reading = () => LostVerseEndings.InTheLoadedPunctuation("I сказав Господь");

        reading.Should().Throw<InvalidOperationException>().WithMessage("*Latin letter*");
    }

    /// <summary>
    /// The real thing: the five books of bible4u's Ohienko that hold a cut-short verse, loaded from
    /// the file, and completed from the Wikisource transcription on this disk. Every one of the seven
    /// must be the head of the printed verse, which is what makes taking them by address safe.
    /// </summary>
    [Fact]
    public async Task TheSevenVersesOfTheLoadedOhienkoComeToReadAsHisPrintingDoes()
    {
        if (!Directory.Exists(TestResources.OhienkoWikisourceFolder) ||
            !File.Exists(TestResources.Bible4u("UKR")))
        {
            return;
        }

        int[] books = [1, 10, 18, 23, 35];
        var loaded = Bible4uTextSource.Read(TestResources.Bible4u("UKR"), "UKR");
        await new CorpusLoader(_db, NullLogger<CorpusLoader>.Instance).Load(new TextSource(
            loaded.Definition,
            [.. loaded.Books.Where(book => books.Contains(book.CanonicalOrdinal))]));

        var endings = LostVerseEndings.OhienkoVerses(TestResources.OhienkoWikisourceFolder);
        var outcome = await _loader.Load(Bible4uTextSource.Ohienko, endings, LostVerseEndings.OhienkoNote);

        outcome.Verses.Should().Be(7);
        Verse(18, 2, 2).Should().Be(
            "І сказав Господь до сатани: Звідки ти йдеш? А сатана відповів Господеві й сказав: " +
            "Я мандрував по землі та й перейшов її.");
        Verse(1, 44, 26).Should().Be(
            "А ми відказали: Не можемо зійти. Коли найменший наш брат буде з нами, то зійдемо ми, бо не " +
            "можемо бачити лиця того мужа, якщо брат наш наймолодший не буде з нами.");
        Verse(23, 50, 9).Should().Be(
            "Отож, Господь Бог допоможе Мені, хто ж отой, що признає Мене винуватим? Вони всі розпадуться, " +
            "немов та одежа, їх міль пожере!");
        foreach (var ending in endings)
        {
            Verse(ending.Book, ending.Chapter, ending.Verse).Should().Be(ending.Complete);
        }

        // What the reader is sent for Job 2: the whole of verse 2, the answer unlinked and unnumbered.
        var ohienko = _db.Texts.AsNoTracking().Single(t => t.Slug == Bible4uTextSource.Ohienko);
        var served = (await Texts.ReadChapter(_db, ohienko.Id, 18, 2, default)).Single(v => v.Number == 2);
        string.Concat(served.Words.Select(w => w.Text + w.Trailer)).Should().EndWith("Я мандрував по землі та й перейшов її.");
        served.Words.TakeLast(7).Should().OnlyContain(w => w.StrongNo == null && w.OriginalWordIds.Length == 0);

        ohienko.RightsNote.Should().EndWith(LostVerseEndings.OhienkoNote);
        (await _loader.Load(Bible4uTextSource.Ohienko, endings, LostVerseEndings.OhienkoNote))
            .Verses.Should().Be(0);
    }

    private void Ukrainian()
    {
        Corpus.Add(_db, "UBIO", TextKind.Translation, "ukr", (22, 19, Head.Split(' ')));
        _db.SaveChanges();
    }

    private static VerseEnding Ending(int chapter, int verse, string complete) => new(1, chapter, verse, complete);

    private List<Word> Words(int chapter, int verse) =>
    [
        .. _db.Words.AsNoTracking()
            .Where(w => w.Verse!.ChapterNumber == chapter && w.Verse.Number == verse)
            .OrderBy(w => w.Position),
    ];

    private string Verse(int chapter, int verse) => Verse(1, chapter, verse);

    private string Verse(int book, int chapter, int verse) => string.Concat(_db.Words
        .Where(w => w.Verse!.Book!.CanonicalOrdinal == book && w.Verse.ChapterNumber == chapter
                    && w.Verse.Number == verse)
        .OrderBy(w => w.Position)
        .Select(w => w.Surface + w.Trailer)).TrimEnd();
}
