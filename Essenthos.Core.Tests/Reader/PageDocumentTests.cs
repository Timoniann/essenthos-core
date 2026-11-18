using System.Net;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Endpoints;
using Essenthos.Core.Pages;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;
using Xunit;

namespace Essenthos.Core.Tests.Reader;

/// <summary>
/// A record's, a Strong number's or an occupation's page written from the document the build stores (TSK-1670, TSK-1819) is
/// the page composed as it is asked, byte for byte, in every site language; the stored document is what is read once it is
/// there; and a document composed under another choice of the page's text, by another version or by other code, is not
/// read. The record's and the occupation's API answers are read from the same documents under the same conditions, and the
/// record's carries a validator answered with 304 before anything is read.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class PageDocumentTests : IDisposable
{
    private const string Shell = "<!doctype html><html lang=\"en\"><head><title>Essenthos</title></head><body><div id=\"root\"></div></body></html>";

    private const string OtherCommit = "0000000000000000000000000000000000000000";

    private readonly WitnessDatabase _database;
    private readonly AppDbContext _db;
    private readonly ServiceProvider _services;
    private readonly string _folder = Directory.CreateTempSubdirectory("page-documents-").FullName;

    private static readonly string[] Records = ["gibbar", "bether"];

    private static readonly string[] Numbers = ["H1", "G2"];

    private static readonly string[] Occupations = ["shepherd"];

    private string Settings => Path.Combine(_folder, "settings.json");

    private string Images => Path.Combine(_folder, "images");

    public PageDocumentTests(WitnessDatabase database)
    {
        _database = database;
        _db = database.NewContext();
        _db.Database.ExecuteSqlRaw(
            "DELETE FROM page_document; DELETE FROM occupation; DELETE FROM reign_statement; DELETE FROM text; DELETE FROM entity; DELETE FROM strong_entry");

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
        var shepherd = new Occupation { Slug = "shepherd", Category = "trade" };
        _db.Occupations.Add(shepherd);
        _db.SaveChanges();
        foreach (var language in SiteLanguage.All)
        {
            _db.OccupationLabels.Add(new OccupationLabel
            {
                OccupationId = shepherd.Id, Language = language.Corpus, Name = $"shepherd ({language.Code})", Masculine = "shepherd",
                Feminine = "shepherdess", Description = "one who keeps sheep",
            });
        }

        _db.PersonOccupations.Add(new PersonOccupation
        {
            PersonId = gibbar.Id, OccupationId = shepherd.Id, Source = "test",
            Witnesses =
            [
                new OccupationWitness
                {
                    Kind = "profession", Text = "test-english", Reference = "GEN 1:2", CanonicalBook = 1, CanonicalChapter = 1,
                    CanonicalVerse = 2, Quote = "Gibbar went",
                },
            ],
        });
        _db.SaveChanges();
        Directory.CreateDirectory(Images);

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
        if (settings is not null)
        {
            File.WriteAllText(Settings, settings);
        }

        return new PageLoader(_db, new CanonIndex(_services.GetRequiredService<IServiceScopeFactory>()),
            new SiteSettingsFile(Settings, NullLogger<SiteSettingsFile>.Instance), cache,
            new DownloadsCatalogue(null, null, NullLogger<DownloadsCatalogue>.Instance), new PlacePictures(Images));
    }

    private static IEnumerable<(SiteLanguage Language, string Path)> Pages() =>
        from language in SiteLanguage.All
        from path in Records.Select(slug => $"/{(slug == "bether" ? "places" : "people")}/{slug}")
            .Concat(Numbers.Select(number => $"/strong/{number}"))
            .Concat(Occupations.Select(slug => $"/occupations/{slug}"))
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

            foreach (var slug in Occupations)
            {
                var document = (await loader.OccupationDocument(slug, language, default))!;
                Add(PageDocuments.OccupationKind, language, slug, PageDocuments.Pack(document, document.Data, PageDocuments.Occupation));
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
        _db.PageDocuments.Count().Should().Be(SiteLanguage.All.Count * (Records.Length + Numbers.Length + Occupations.Length));

        // What the documents hold is what is read: a name changed in the tables after they were made does not show.
        _db.Database.ExecuteSqlRaw("UPDATE entity SET name = 'Changed' WHERE slug = 'gibbar'; UPDATE strong_entry SET definition = 'changed'");
        foreach (var (_, path) in Pages())
        {
            (await Html(path)).Should().Be(composed[path], path);
        }

        composed.Values.Should().Contain(html => html.Contains("page-data"));
        composed["/occupations/shepherd"].Should().Contain("Gibbar");
    }

    /// <summary>
    /// The build composes a record's or a Strong number's pages in every language one after another, reading what they read
    /// alike once, and the verses and texts every page reads alike once per run; the documents are those each page composes
    /// alone.
    /// </summary>
    [Fact]
    public async Task DocumentsComposedWithTheBuildsSharedReadsAreThoseComposedAlone()
    {
        using var cache = new PageCache();
        var loader = Loader(cache);
        var alone = new List<string>();
        foreach (var address in Records.Concat(Numbers))
        {
            alone.AddRange(await Composed(address));
        }

        var together = new List<string>();
        SharedReads.Corpus.Value = new SharedReads.CorpusReads();
        try
        {
            foreach (var address in Records.Concat(Numbers))
            {
                SharedReads.Current.Value = new SharedReads();
                together.AddRange(await Composed(address));
                together.AddRange(await Composed(address));
            }
        }
        finally
        {
            SharedReads.Current.Value = null;
            SharedReads.Corpus.Value = null;
        }

        together.Should().Equal(alone.Chunk(SiteLanguage.All.Count).SelectMany(one => one.Concat(one)));

        async Task<List<string>> Composed(string address)
        {
            var hashes = new List<string>();
            foreach (var language in SiteLanguage.All)
            {
                hashes.Add(Numbers.Contains(address)
                    ? await loader.StrongDocument(address, language, default) is { } strong
                        ? PageDocuments.Pack(strong, strong.Data, PageDocuments.Strong).Hash
                        : "none"
                    : await loader.RecordDocument(address, language, default) is { } record
                        ? PageDocuments.Pack(record, record.Data, PageDocuments.Record).Hash
                        : "none");
            }

            return hashes;
        }
    }

    /// <summary>
    /// Where two verses of the text stand at one address, the page quotes the verse whose own address it is, whichever
    /// the database would return first.
    /// </summary>
    [Fact]
    public async Task APageQuotesTheVerseWhoseOwnAddressItIsWhereTwoVersesShareIt()
    {
        _db.Database.ExecuteSqlRaw(
            "UPDATE verse SET label = 'a' WHERE number = 2 AND text_id = (SELECT id FROM text WHERE slug = 'test-english')");
        var english = await _db.Texts.SingleAsync(t => t.Slug == "test-english");
        var chapter = await _db.Chapters.SingleAsync(c => c.TextId == english.Id && c.Number == 1);
        var own = new Verse { TextId = english.Id, BookId = chapter.BookId, ChapterId = chapter.Id, ChapterNumber = 1, Number = 2, Sequence = 3 };
        _db.Verses.Add(own);
        _db.VerseReferences.Add(new VerseReference { Verse = own, CanonicalBook = 1, CanonicalChapter = 1, CanonicalVerse = 2, IsPrimary = true });
        _db.Words.AddRange(
            new Word { TextId = english.Id, Verse = own, Position = 1, Surface = "Gibbar", Trailer = " " },
            new Word { TextId = english.Id, Verse = own, Position = 2, Surface = "stayed", Trailer = string.Empty });
        await _db.SaveChangesAsync();

        using var cache = new PageCache();
        var document = (await Loader(cache).RecordDocument("gibbar", SiteLanguage.All.Single(l => l.Code == "en"), default))!;

        document.Verses.Should().ContainSingle().Which.Text.Should().Be("Gibbar stayed");
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
        File.Delete(Settings);
        using var fresh = new PageCache();
        (await Loader(fresh).Load(PageAddress.Parse("/people/gibbar"), PageWording.Compiled("en"), default))
            .Heading.Should().Contain("Changed", "a document of another version is composed again");
    }

    [Fact]
    public async Task ADocumentComposedByOtherCodeIsNotRead()
    {
        PageDocuments.Build.Should().NotBeNull("a test build of a checkout knows its commit");
        await Store();
        _db.Database.ExecuteSqlRaw("UPDATE entity SET name = 'Changed' WHERE slug = 'gibbar'");
        (await Heading("/people/gibbar")).Should().NotContain("Changed", "the document this build composed is read");

        await Restamp(PageDocuments.RecordKind, "gibbar", OtherCommit);
        (await Heading("/people/gibbar")).Should().Contain("Changed", "a document another commit composed is composed again");

        await Restamp(PageDocuments.RecordKind, "gibbar", null);
        (await Heading("/people/gibbar")).Should().Contain("Changed", "a document that names no commit is composed again");

        await using var app = await Host();
        using var client = Client(app);
        (await Name(client, "/v1/entities/gibbar?language=eng&prose=eng")).Should().Be("Changed", "the answer is composed as asked too");
    }

    [Fact]
    public async Task TheRecordAnswerIsTheStoredOneExactlyWhereThePageWouldReadIt()
    {
        await Store();
        await using var app = await Host();
        using var client = Client(app);
        foreach (var language in SiteLanguage.All)
        {
            foreach (var slug in Records)
            {
                var asked = $"/v1/entities/{slug}?language={language.Corpus}&prose={language.Corpus}";
                JsonNode.DeepEquals(await Answer(client, asked), await Answer(client, asked + "&live=1")).Should().BeTrue(asked);
            }
        }

        // What the documents hold is what is read: a name changed in the tables after they were made does not show, except
        // to an address the reader's script never writes, which is composed as asked.
        _db.Database.ExecuteSqlRaw("UPDATE entity SET name = 'Changed' WHERE slug IN ('gibbar', 'bether')");
        (await Name(client, "/v1/entities/gibbar?language=eng&prose=eng")).Should().Be("Gibbar");
        (await Name(client, "/v1/entities/gibbar?prose=eng&language=eng")).Should().Be("Gibbar", "the order of the two is no other address");
        (await Name(client, "/v1/entities/gibbar?language=eng&prose=eng&live=1")).Should().Be("Changed");
        (await Name(client, "/v1/entities/gibbar?language=eng")).Should().Be("Changed");
        (await Name(client, "/v1/entities/gibbar?language=en&prose=en")).Should().Be("Changed", "a language is not normalised");
        (await Name(client, "/v1/entities/gibbar?language=eng&prose=ukr")).Should().Be("Changed");
        (await Name(client, "/v1/entities/gibbar?language=eng&prose=eng&prose=eng")).Should().Be("Changed");

        // Our own pictures switched off: the answer the documents carry would show them.
        File.WriteAllText(Settings, "{\"settings\":{\"generatedImages\":false}}");
        (await Name(client, "/v1/entities/gibbar?language=eng&prose=eng")).Should().Be("Changed");
        File.Delete(Settings);
        (await Name(client, "/v1/entities/gibbar?language=eng&prose=eng")).Should().Be("Gibbar");

        // A place's picture drawn after the documents were made leads its pictures, which the stored answer does not hold.
        (await Name(client, "/v1/entities/bether?language=eng&prose=eng")).Should().Be("Bether");
        Picture("eng", "bether");
        var drawn = await Answer(client, "/v1/entities/bether?language=eng&prose=eng");
        drawn["name"]!.GetValue<string>().Should().Be("Changed");
        drawn["images"]![0]!["kind"]!.GetValue<string>().Should().Be(PlacePictures.Kind);
        (await Name(client, "/v1/entities/bether?language=ukr&prose=ukr")).Should().Be("Changed", "the English picture stands in for a language with none");
    }

    [Fact]
    public async Task TheRecordAnswerCarriesAValidatorAnsweredBeforeAnythingIsRead()
    {
        await Store();
        await using var app = await Host();
        using var client = Client(app);
        const string asked = "/v1/entities/gibbar?language=eng&prose=eng";
        var first = await client.GetAsync(asked);
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        first.Headers.CacheControl!.NoCache.Should().BeTrue();
        var tag = first.Headers.ETag!.Tag;
        (await client.GetAsync("/v1/entities/bether?language=eng&prose=eng")).Headers.ETag!.Tag.Should().NotBe(tag, "the tag is the record's own");

        (await Held(client, asked, tag)).StatusCode.Should().Be(HttpStatusCode.NotModified);
        (await Held(client, asked, "W/" + tag)).StatusCode.Should().Be(HttpStatusCode.NotModified, "a compressing proxy weakens the tag");
        (await Held(client, asked, "\"another\"")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Held(client, "/v1/entities/missing?language=eng&prose=eng", "*")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Nothing is read for a client that holds the answer: with the record and its document gone it is still told it has it.
        _db.Database.ExecuteSqlRaw("DELETE FROM page_document; DELETE FROM person_occupation; DELETE FROM entity WHERE slug = 'gibbar'");
        var held = await Held(client, asked, tag);
        held.StatusCode.Should().Be(HttpStatusCode.NotModified);
        held.Headers.ETag!.Tag.Should().Be(tag);

        // The owner's switches move the tag.
        File.WriteAllText(Settings, "{\"settings\":{\"generatedImages\":false}}");
        (await Held(client, asked, tag)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task APersonAmongTheKingsSaysSo()
    {
        var uzziah = new Entity { Slug = "uzziah", Name = "Uzziah", Kind = EntityKind.Person, Sex = "male" };
        var nehemiah = new Entity { Slug = "nehemiah", Name = "Nehemiah", Kind = EntityKind.Person, Sex = "male" };
        _db.Entities.AddRange(uzziah, nehemiah);
        await _db.SaveChangesAsync();
        var gibbar = await _db.Entities.SingleAsync(e => e.Slug == "gibbar");
        _db.ReignStatements.AddRange(
            new ReignStatement
            {
                EntityId = gibbar.Id, RulerEntityId = uzziah.Id, Role = ReignRoles.Prophet, Kind = ReignStatementKinds.Superscription,
                CanonicalBook = 1, CanonicalChapter = 1, CanonicalVerse = 1, Source = "test",
            },
            new ReignStatement
            {
                EntityId = nehemiah.Id, RulerEntityId = uzziah.Id, Role = ReignRoles.Subject, Kind = ReignStatementKinds.Dated,
                CanonicalBook = 1, CanonicalChapter = 1, CanonicalVerse = 2, Source = "test",
            });
        await _db.SaveChangesAsync();

        await using var app = await Host();
        using var client = Client(app);
        foreach (var (slug, among) in new[] { ("gibbar", true), ("uzziah", true), ("nehemiah", false), ("bether", false) })
        {
            (await Answer(client, $"/v1/entities/{slug}?language=eng&prose=eng"))["amongKings"]!.GetValue<bool>().Should().Be(among, slug);
        }
    }

    [Fact]
    public async Task TheOccupationsFirstPageIsTheStoredOneExactlyWhereThePageWouldReadIt()
    {
        await Store();
        await using var app = await Host();
        using var client = Client(app);
        foreach (var language in SiteLanguage.All)
        {
            var asked = $"/v1/occupations/shepherd?language={language.Corpus}&prose={language.Corpus}&skip=0&take={PageData.OccupationPeople}";
            JsonNode.DeepEquals(await Answer(client, asked), await Answer(client, asked + "&live=1")).Should().BeTrue(asked);
        }

        _db.Database.ExecuteSqlRaw("UPDATE entity SET name = 'Changed' WHERE slug = 'gibbar'");
        var stored = $"/v1/occupations/shepherd?language=eng&prose=eng&skip=0&take={PageData.OccupationPeople}";
        (await Practitioner(client, stored)).Should().Be("Gibbar");
        (await Practitioner(client, stored + "&live=1")).Should().Be("Changed");
        (await Practitioner(client, "/v1/occupations/shepherd?language=eng&prose=eng")).Should().Be("Changed");
        (await Practitioner(client, "/v1/occupations/shepherd?language=eng&prose=eng&skip=0&take=20")).Should().Be("Changed");
        (await Heading("/occupations/shepherd")).Should().NotBeNull();

        await Restamp(PageDocuments.OccupationKind, "shepherd", OtherCommit);
        (await Practitioner(client, stored)).Should().Be("Changed", "a document another commit composed is not read");
    }

    [Fact]
    public async Task ARecordPagesValidatorMovesWithItsPlacesPicture()
    {
        await using var app = await Host();
        using var client = Client(app);
        const string page = "/v1/pages/places/bether";
        var first = await client.GetAsync(page);
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        var tag = first.Headers.ETag!.Tag;
        (await Held(client, page, tag)).StatusCode.Should().Be(HttpStatusCode.NotModified);

        // A picture drawn after the page was given out is not the page the crawler holds, and the page written now shows it.
        Picture("eng", "bether");
        var drawn = await Held(client, page, tag);
        drawn.StatusCode.Should().Be(HttpStatusCode.OK, "a place's picture drawn since moves its page's validator");
        drawn.Headers.ETag!.Tag.Should().NotBe(tag);
        var picture = new PlacePictures(Images).For("bether", "eng")!;
        (await drawn.Content.ReadAsStringAsync()).Should().Contain(picture.Digest);
        drawn.Content.Headers.LastModified!.Value.Should().BeOnOrAfter(
            new DateTimeOffset(picture.WrittenAt.AddTicks(-(picture.WrittenAt.Ticks % TimeSpan.TicksPerSecond)), TimeSpan.Zero));
        (await Held(client, page, drawn.Headers.ETag!.Tag)).StatusCode.Should().Be(HttpStatusCode.NotModified);
        (await Held(client, "/v1/pages/people/gibbar", tag)).StatusCode.Should().Be(HttpStatusCode.NotModified, "a page with no picture is untouched");
        (await Held(client, "/v1/pages/people/gibbar", drawn.Headers.ETag!.Tag)).StatusCode.Should().Be(HttpStatusCode.OK, "a picture's tag is its page's own");
    }

    private async Task<string> Heading(string path)
    {
        using var cache = new PageCache();
        var address = PageAddress.Parse(path);
        return (await Loader(cache).Load(address, PageWording.Compiled(address.Language.Code), default)).Heading;
    }

    /// <summary>Every document of one address rewritten as though composed by <paramref name="build"/>.</summary>
    private async Task Restamp(string kind, string address, string? build)
    {
        foreach (var row in await _db.PageDocuments.Where(d => d.Kind == kind && d.Address == address).ToListAsync())
        {
            row.Body = kind == PageDocuments.RecordKind
                ? Restamped(row.Body, PageDocuments.Record, document => document with { Build = build })
                : Restamped(row.Body, PageDocuments.Occupation, document => document with { Build = build });
        }

        await _db.SaveChangesAsync();

        static byte[] Restamped<T>(byte[] body, JsonTypeInfo<T> info, Func<T, T> change)
        {
            var (document, data) = PageDocuments.Unpack(body, info);
            return PageDocuments.Pack(change(document), data, info).Body;
        }
    }

    private void Picture(string language, string slug)
    {
        var folder = Path.Combine(Images, PlacePictures.Folder, language);
        Directory.CreateDirectory(folder);
        using var bitmap = new SKBitmap(4, 3);
        bitmap.Erase(SKColors.SteelBlue);
        using var data = bitmap.Encode(SKEncodedImageFormat.Webp, 80);
        File.WriteAllBytes(Path.Combine(folder, $"{slug}.webp"), data.ToArray());
    }

    private async Task<WebApplication> Host()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddScoped(_ => _database.NewContext());
        builder.Services.AddSingleton(new SiteSettingsFile(Settings, NullLogger<SiteSettingsFile>.Instance));
        builder.Services.AddSingleton(new PlacePictures(Images));
        var shell = Path.Combine(_folder, "index.html");
        await File.WriteAllTextAsync(shell, Shell);
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            { [PageShell.ConfigurationKey] = shell, [PageEndpoints.OriginKey] = "https://essenthos.test" });
        builder.Services.AddSingleton<PageShell>();
        builder.Services.AddScoped<Sitemaps>();
        builder.Services.AddSingleton<SitemapDates>();
        builder.Services.AddPageServices();
        builder.Services.Configure<JsonOptions>(options =>
            options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default));
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var app = builder.Build();
        app.MapGroup("/v1").MapEncyclopedia();
        app.MapGroup("/v1").MapPages();
        await app.StartAsync();
        return app;
    }

    private static HttpClient Client(WebApplication app) => new() { BaseAddress = new Uri(app.Urls.Single()) };

    private static async Task<JsonNode> Answer(HttpClient client, string path)
    {
        var response = await client.GetAsync(path);
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, $"{path}: {body}");
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
        return JsonNode.Parse(body)!;
    }

    private static async Task<string> Name(HttpClient client, string path) => (await Answer(client, path))["name"]!.GetValue<string>();

    private static async Task<string> Practitioner(HttpClient client, string path) =>
        (await Answer(client, path))["people"]![0]!["person"]!["name"]!.GetValue<string>();

    private static Task<HttpResponseMessage> Held(HttpClient client, string path, string tag)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.TryAddWithoutValidation("If-None-Match", tag);
        return client.SendAsync(request);
    }
}
