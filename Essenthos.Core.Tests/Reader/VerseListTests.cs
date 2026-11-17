using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Endpoints;
using Essenthos.Core.Pages;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests.Reader;

/// <summary>
/// The page of every verse of a record or a Strong number: the verses as the books run, under each
/// book with how many it holds, quoted from the page language's text, a page at a time.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class VerseListTests : IDisposable
{
    private const int Psalms = 19;

    private readonly AppDbContext _db;
    private readonly PageCache _cache = new();
    private readonly ServiceProvider _services;
    private readonly string _folder = Directory.CreateTempSubdirectory("verse-list-").FullName;

    public VerseListTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _db.Database.ExecuteSqlRaw("DELETE FROM text; DELETE FROM entity; DELETE FROM strong_entry");

        var english = Corpus.Add(_db, "test-english", TextKind.Translation, "eng", (1, 1, ["In", "the", "beginning", "God"]));
        _db.AddBook(english, Psalms, "Psalms",
            (49, 1, ["Hear", "this,", "all", "ye", "people"]),
            (49, 2, ["Both", "low", "and", "high"]));
        var hebrew = Corpus.Add(_db, "test-hebrew", TextKind.CriticalEdition, "hbo", (1, 1, ["בראשית", "ברא", "אלהים", "אלהים"]));
        _db.AddBook(hebrew, Psalms, "Psalms", (49, 1, ["שמעו", "זאת", "אלהים"]));
        var gibbar = new Entity { Slug = "gibbar", Name = "Gibbar", Kind = EntityKind.Person };
        _db.Entities.Add(gibbar);
        _db.StrongEntries.Add(new StrongEntry { StrongNumber = "H430", Lemma = "אֱלֹהִים", Transliteration = "ʼĕlôhîym" });
        _db.SaveChanges();

        // A translation tagging its own words may carry the number more often than the original does.
        foreach (var word in _db.Words.Where(w => (w.Text!.Slug == "test-hebrew" && w.Surface == "אלהים")
                                                  || (w.Text!.Slug == "test-english" && w.Verse!.ChapterNumber == 1)).ToList())
        {
            word.StrongNumber = "H430";
        }

        foreach (var (book, chapter, verse) in new[] { (Psalms, 49, 2), (1, 1, 1), (Psalms, 49, 1) })
        {
            _db.EntityVerses.Add(new EntityVerse
            {
                EntityId = gibbar.Id,
                CanonicalBook = book,
                CanonicalChapter = chapter,
                CanonicalVerse = verse,
                Label = "Gibbar",
                Disputed = false,
                Source = "test",
            });
        }

        _db.SaveChanges();
        _db.WriteStoredStrong().GetAwaiter().GetResult();
        _services = new ServiceCollection().AddScoped(_ => database.NewContext()).BuildServiceProvider();
    }

    public void Dispose()
    {
        _services.Dispose();
        _cache.Dispose();
        _db.Dispose();
        Directory.Delete(_folder, recursive: true);
    }

    private ICanonIndex Canon() => new CanonIndex(_services.GetRequiredService<IServiceScopeFactory>());

    private async Task<PageContent> Load(string path, Dictionary<string, string>? query = null) =>
        await new PageLoader(_db, Canon(), new SiteSettingsFile(Path.Combine(_folder, "settings.json"),
                NullLogger<SiteSettingsFile>.Instance), _cache, new DownloadsCatalogue(null, null, NullLogger<DownloadsCatalogue>.Instance))
            .Load(PageAddress.Parse(path, query), PageWording.Compiled("en"), default);

    [Fact]
    public async Task ARecordsVersesRunAsTheBooksDoUnderTheirBooksWithTheirWords()
    {
        var page = await Load("/people/gibbar/verses");

        page.Status.Should().Be(StatusCodes.Status200OK);
        page.Body.Should().Contain("<h1>Gibbar: all verses</h1>")
            .And.Contain("3 verses.")
            .And.Contain("<h2>Genesis (1)</h2><ul><li><a href=\"/read/genesis/1/1\">Genesis 1:1</a> In the beginning God</li></ul>")
            .And.Contain("<h2>Psalms (2)</h2><ul><li><a href=\"/read/psalms/49/1\">Psalms 49:1</a> Hear this, all ye people</li>"
                         + "<li><a href=\"/read/psalms/49/2\">Psalms 49:2</a> Both low and high</li></ul>")
            .And.NotContain("rel=\"next\"");
    }

    [Fact]
    public async Task APageOfTheListHoldsAsManyAsAskedAndLinksTheRest()
    {
        var page = await Load("/people/gibbar/verses", new Dictionary<string, string> { ["skip"] = "1", ["take"] = "10" });

        page.Indexed.Should().BeFalse();
        page.Body.Should().NotContain("Genesis 1:1").And.Contain("Psalms 49:2")
            .And.Contain("<a href=\"/people/gibbar/verses?take=10\" rel=\"prev\">");
    }

    [Fact]
    public async Task AListAskedUnderAnotherKindOfRecordIsSentToItsOwnAddress()
    {
        var page = await Load("/places/gibbar/verses");

        page.Status.Should().Be(StatusCodes.Status301MovedPermanently);
        page.MovedTo.Should().Be("/people/gibbar/verses");
    }

    [Fact]
    public async Task AStrongNumbersVersesAreCountedOnceEachInTheOriginalHoldingItMost()
    {
        var list = await StrongVerseEndpoints.Page(_db, Canon(), "H430", null, 0, 20, default);

        list.Corpus.Should().Be("test-hebrew");
        list.Total.Should().Be(2);
        list.Items.Select(v => (v.Book.Ordinal, v.Chapter, v.Verse, v.Words.Count)).Should().Equal((1, 1, 1, 2), (Psalms, 49, 1, 1));
        list.Books!.Select(b => (b.Book.Ordinal, b.Count)).Should().Equal((1, 1), (Psalms, 1));
        (await StrongVerseEndpoints.Page(_db, Canon(), "H430", null, 1, 1, default)).Books.Should()
            .BeEquivalentTo(list.Books, options => options.WithStrictOrdering(), "every page says how many verses each book holds");

        var page = await Load("/strong/H430/verses");
        page.Body.Should().Contain("<h1>אֱלֹהִים ʼĕlôhîym (H430): all verses</h1>").And.Contain("In the beginning God");
    }
}
