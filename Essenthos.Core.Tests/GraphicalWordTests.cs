using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// The printed word, where the corpus stores several rows for one.
///
/// A row is a morpheme. Hebrew writes the preposition and the article onto the noun, so BHSA holds
/// בְּרֵאשִׁית as בְּ and רֵאשִׁית with nothing between them, and a reader typing what the page
/// shows is typing something no single row contains. These are asked of Postgres because the run is
/// found by a window over the trailer, and a window function is the thing under test.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class GraphicalWordTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly IDbContextTransaction _transaction;

    public GraphicalWordTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _transaction = _db.Database.BeginTransaction();
    }

    public void Dispose()
    {
        _transaction.Rollback();
        _transaction.Dispose();
        _db.Dispose();
    }

    /// <summary>
    /// Genesis 1:1 as BHSA holds it: four printed words out of six rows, with the preposition and
    /// both articles written onto the word after them.
    /// </summary>
    private Text Hebrew()
    {
        var text = new Text { Slug = "bhsa-test", Name = "bhsa-test", Kind = TextKind.ManuscriptTradition, Language = "hbo" };
        var book = new Book { Text = text, CanonicalOrdinal = 1, Position = 1, Name = "Genesis", Slug = "gen" };
        var chapter = new Chapter { Text = text, Book = book, Number = 1 };
        var verse = new Verse { Text = text, Book = book, Chapter = chapter, ChapterNumber = 1, Number = 1 };
        _db.AddRange(text, book, chapter, verse);
        _db.VerseReferences.Add(new VerseReference
        {
            Verse = verse, CanonicalBook = 1, CanonicalChapter = 1, CanonicalVerse = 1, IsPrimary = true,
        });

        Add(text, verse, 1, "בְּ", string.Empty, "ב");
        Add(text, verse, 2, "רֵאשִׁית", " ", "ראשית");
        Add(text, verse, 3, "בָּרָא", " ", "ברא");
        Add(text, verse, 4, "אֱלֹהִים", " ", "אלהים");
        Add(text, verse, 5, "הָ", string.Empty, "ה");
        Add(text, verse, 6, "אָרֶץ", string.Empty, "ארץ");
        _db.SaveChanges();
        return text;
    }

    private void Add(Text text, Verse verse, int position, string surface, string trailer, string folded) =>
        _db.Words.Add(new Word
        {
            Text = text, Verse = verse, Position = position,
            Surface = surface, Trailer = trailer, NormalisedText = folded,
        });

    private async Task Run() =>
        await new GraphicalWordLoader(_db, NullLogger<GraphicalWordLoader>.Instance).Load();

    [Fact]
    public async Task EveryRowOfARunCarriesTheWholePrintedWord()
    {
        var text = Hebrew();
        await Run();

        var words = await _db.Words.Where(w => w.TextId == text.Id)
            .OrderBy(w => w.Position)
            .Select(w => new { w.Surface, w.GraphicalText })
            .ToListAsync();

        words.Select(w => w.GraphicalText).Should().Equal(
            "בראשית", "בראשית", null, null, "הארץ", "הארץ");
    }

    /// <summary>
    /// A word printed on its own gets nothing, which is what keeps the column sparse and what lets
    /// a term matching it mean "this crossed a row boundary" rather than "this is a word".
    /// </summary>
    [Fact]
    public async Task AWordPrintedOnItsOwnIsNotGivenARun()
    {
        var text = Hebrew();
        await Run();

        var alone = await _db.Words
            .Where(w => w.TextId == text.Id && (w.Surface == "בָּרָא" || w.Surface == "אֱלֹהִים"))
            .ToListAsync();

        alone.Should().HaveCount(2).And.OnlyContain(w => w.GraphicalText == null);
    }

    /// <summary>
    /// The last word of a verse ends its run whatever its trailer says. Every Greek verse of this
    /// corpus ends with an empty trailer, so reading that as "joined to the next" would join the
    /// last word of a verse to the first word of the one after it.
    /// </summary>
    [Fact]
    public async Task AVerseDoesNotRunIntoTheNext()
    {
        var text = new Text
        {
            Slug = "greek-test", Name = "greek-test", Kind = TextKind.PrintedEdition, Language = "grc",
        };
        var book = new Book { Text = text, CanonicalOrdinal = 40, Position = 1, Name = "Matthew", Slug = "mat" };
        var chapter = new Chapter { Text = text, Book = book, Number = 1 };
        _db.AddRange(text, book, chapter);

        foreach (var number in new[] { 1, 2 })
        {
            var verse = new Verse
            {
                Text = text, Book = book, Chapter = chapter, ChapterNumber = 1, Number = number,
            };
            _db.Add(verse);
            _db.VerseReferences.Add(new VerseReference
            {
                Verse = verse, CanonicalBook = 40, CanonicalChapter = 1, CanonicalVerse = number, IsPrimary = true,
            });
            Add(text, verse, 1, "βιβλος", " ", "βιβλος");
            Add(text, verse, 2, "γενεσεως", string.Empty, "γενεσεως");
        }

        _db.SaveChanges();
        await Run();

        (await _db.Words.Where(w => w.TextId == text.Id).ToListAsync())
            .Should().OnlyContain(w => w.GraphicalText == null);
    }

    /// <summary>
    /// The pass is in the start-up pipeline beside the folding, so it runs on every boot and must
    /// cost nothing on a corpus it has already done.
    /// </summary>
    [Fact]
    public async Task RunningItTwiceChangesNothing()
    {
        var text = Hebrew();
        await Run();
        var second = await new GraphicalWordLoader(_db, NullLogger<GraphicalWordLoader>.Instance).Load();

        second.Words.Should().Be(0);
        (await _db.Words.CountAsync(w => w.TextId == text.Id && w.GraphicalText != null)).Should().Be(4);
    }
}
