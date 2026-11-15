using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Endpoints;
using Essenthos.Core.Pages;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests.Reader;

/// <summary>
/// A record's or a Strong number's page written from the document the build stores (TSK-1670) is the page composed as it
/// is asked, byte for byte, in every site language; the stored document is what is read once it is there; and a document
/// composed under another choice of the page's text, or by another version, is not read.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class PageDocumentTests : IDisposable
{
    private const string Shell = "<!doctype html><html lang=\"en\"><head><title>Essenthos</title></head><body><div id=\"root\"></div></body></html>";

    private readonly AppDbContext _db;
    private readonly ServiceProvider _services;
    private readonly string _folder = Directory.CreateTempSubdirectory("page-documents-").FullName;

    private static readonly string[] Records = ["gibbar", "bether"];

    private static readonly string[] Numbers = ["H1", "G2"];

    public PageDocumentTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _db.Database.ExecuteSqlRaw("DELETE FROM page_document; DELETE FROM text; DELETE FROM entity; DELETE FROM strong_entry");

        Corpus.Add(_db, "test-english", TextKind.Translation, "eng", (1, 1, ["In", "the", "beginning"]), (1, 2, ["And", "Gibbar", "went"]));
        Corpus.Add(_db, "test-other", TextKind.Translation, "eng", (1, 1, ["At", "first"]), (1, 2, ["Then", "Gibbar", "left"]));
        Corpus.Add(_db, "test-hebrew", TextKind.CriticalEdition, "hbo", (1, 1, ["בראשית"]));
        var gibbar = new Entity { Slug = "gibbar", Name = "Gibbar", Kind = EntityKind.Person, Sex = "male", Distinguisher = "a man of Judah" };
        var bether = new Entity { Slug = "bether", Name = "Bether", Kind = EntityKind.Place };
        _db.Entities.AddRange(gibbar, bether);
        _db.SaveChanges();
        _db.EntityVerses.AddRange(
            new EntityVerse { EntityId = gibbar.Id, CanonicalBook = 1, CanonicalChapter = 1, CanonicalVerse = 2, Source = "test" },
            new EntityVerse { EntityId = bether.Id, CanonicalBook = 1, CanonicalChapter = 1, CanonicalVerse = 1, Source = "test" });
        _db.PlaceLocations.Add(new PlaceLocation { EntityId = bether.Id, Latitude = 31.7123456789, Longitude = 35.1, Kind = "point", ModernId = "m1", CoordinatesSource = "test", Source = "test" });
        _db.EntityNameForms.Add(new EntityNameForm { EntityId = gibbar.Id, Language = "ukr", Form = "Ґіббар",
            GrammaticalCase = GrammaticalCases.Nominative, Source = "test" });
        _db.StrongEntries.AddRange(
            new StrongEntry { StrongNumber = "H1", Lemma = "אָב", Transliteration = "ʼâb", Definition = "father", Derivation = "a primitive word" },
            new StrongEntry { StrongNumber = "G2", Lemma = "Ἀαρών", Transliteration = "Aarṓn", Definition = "Aaron" });
        _db.SaveChanges();

        _services = new ServiceCollection().AddScoped(_ => database.NewContext()).BuildServiceProvider();
    }

    public void Dispose()
    {
        _services.Dispose();
        _db.Dispose();
        Directory.Delete(_folder, recursive: true);
    }

    private PageLoader Loader(PageCache cache, string? settings = null)
    {
        var path = Path.Combine(_folder, "settings.json");
        if (settings is not null)
        {
            File.WriteAllText(path, settings);
        }

        return new PageLoader(_db, new CanonIndex(_services.GetRequiredService<IServiceScopeFactory>()),
            new SiteSettingsFile(path, NullLogger<SiteSettingsFile>.Instance), cache,
            new DownloadsCatalogue(null, null, NullLogger<DownloadsCatalogue>.Instance));
    }

    private static IEnumerable<(SiteLanguage Language, string Path)> Pages() =>
        from language in SiteLanguage.All
        from path in Records.Select(slug => $"/{(slug == "bether" ? "places" : "people")}/{slug}")
            .Concat(Numbers.Select(number => $"/strong/{number}"))
        select (language, language.Path(path));

    private async Task<string> Html(string path)
    {
        using var cache = new PageCache();
        var address = PageAddress.Parse(path);
        var page = await Loader(cache).Load(address, PageWording.Compiled(address.Language.Code), default);
        page.Status.Should().Be(200, path);
        return PageHtml.Render(Shell, page, address.Language, "https://essenthos.test");
    }

    private async Task Store()
    {
        using var cache = new PageCache();
        var loader = Loader(cache);
        foreach (var language in SiteLanguage.All)
        {
            foreach (var slug in Records)
            {
                var document = (await loader.RecordDocument(slug, language, default))!;
                Add(PageDocuments.RecordKind, language, slug, PageDocuments.Pack(document, document.Data, PageDocuments.Record));
            }

            foreach (var number in Numbers)
            {
                var document = (await loader.StrongDocument(number, language, default))!;
                Add(PageDocuments.StrongKind, language, number, PageDocuments.Pack(document, document.Data, PageDocuments.Strong));
            }
        }

        await _db.SaveChangesAsync();

        void Add(string kind, SiteLanguage language, string address, (byte[] Body, string Hash, int Length) packed) =>
            _db.PageDocuments.Add(new PageDocument
            {
                Kind = kind, Language = language.Code, Address = address, Body = packed.Body, Hash = packed.Hash,
            });
    }

    [Fact]
    public async Task APageWrittenFromItsStoredDocumentIsThePageComposedAsAsked()
    {
        var composed = new Dictionary<string, string>();
        foreach (var (_, path) in Pages())
        {
            composed[path] = await Html(path);
        }

        await Store();
        _db.PageDocuments.Count().Should().Be(SiteLanguage.All.Count * (Records.Length + Numbers.Length));

        // What the documents hold is what is read: a name changed in the tables after they were made does not show.
        _db.Database.ExecuteSqlRaw("UPDATE entity SET name = 'Changed' WHERE slug = 'gibbar'; UPDATE strong_entry SET definition = 'changed'");
        foreach (var (_, path) in Pages())
        {
            (await Html(path)).Should().Be(composed[path], path);
        }

        composed.Values.Should().Contain(html => html.Contains("page-data"));
    }

    [Fact]
    public async Task ADocumentComposedUnderAnotherChoiceOfTextOrByAnotherVersionIsNotRead()
    {
        await Store();
        _db.Database.ExecuteSqlRaw("UPDATE entity SET name = 'Changed' WHERE slug = 'gibbar'");

        using var cache = new PageCache();
        var chosen = Loader(cache, "{\"settings\":{\"readerTranslation\":\"test-other\"}}");
        var page = await chosen.Load(PageAddress.Parse("/people/gibbar"), PageWording.Compiled("en"), default);
        page.Heading.Should().Contain("Changed", "the page's text is not the one the document was composed under");

        var stale = await _db.PageDocuments.SingleAsync(d => d.Kind == PageDocuments.RecordKind && d.Language == "en" && d.Address == "gibbar");
        var (document, data) = PageDocuments.Unpack(stale.Body, PageDocuments.Record);
        stale.Body = PageDocuments.Pack(document with { Version = PageDocuments.Version + 1 }, data, PageDocuments.Record).Body;
        await _db.SaveChangesAsync();
        File.Delete(Path.Combine(_folder, "settings.json"));
        using var fresh = new PageCache();
        (await Loader(fresh).Load(PageAddress.Parse("/people/gibbar"), PageWording.Compiled("en"), default))
            .Heading.Should().Contain("Changed", "a document of another version is composed again");
    }
}
