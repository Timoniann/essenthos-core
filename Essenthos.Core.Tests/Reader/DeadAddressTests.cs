using System.Net;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Endpoints;
using Essenthos.Core.Pages;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests.Reader;

/// <summary>
/// Addresses a search engine already knows that name no page of their own: a psalm's superscription
/// at verse 0, one edition's verse numbers written as the frame's, and the old address of a record
/// withdrawn from the encyclopedia. Each ends at a page — the chapter, or the record now holding the
/// word — or says it is gone; and nothing the sitemaps list fails to open.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class DeadAddressTests : IDisposable
{
    private const int Esther = 17;
    private const int Psalms = 19;

    private readonly WitnessDatabase _database;
    private readonly AppDbContext _db;
    private readonly PageCache _cache = new();
    private readonly ServiceProvider _services;
    private readonly string _folder = Directory.CreateTempSubdirectory("dead-addresses-").FullName;

    public DeadAddressTests(WitnessDatabase database)
    {
        _database = database;
        _db = database.NewContext();
        _db.Database.ExecuteSqlRaw("DELETE FROM text; DELETE FROM entity");

        var english = Corpus.Add(_db, "test-english", TextKind.Translation, "eng", (1, 1, ["In", "the", "beginning"]));
        _db.AddBook(english, Psalms, "Psalms",
            (49, 1, ["To", "the", "chief", "Musician,", "Hear", "this"]),
            (49, 2, ["Both", "low", "and", "high"]));
        _db.AddBook(english, Esther, "Esther",
            (5, 1, ["Now", "on", "the", "third", "day"]),
            (5, 14, ["Then", "said", "Zeresh"]));
        var hebrew = Corpus.Add(_db, "test-hebrew", TextKind.CriticalEdition, "hbo", (1, 1, ["בראשית"]));
        _db.AddBook(hebrew, Psalms, "Psalms",
            (49, 0, ["למנצח", "לבני", "קרח", "מזמור"]),
            (49, 1, ["שמעו", "זאת"]),
            (49, 2, ["גם", "בני", "אדם"]));
        _db.Entities.AddRange(
            new Entity { Slug = "gibbar", Name = "Gibbar", Kind = EntityKind.Person },
            new Entity { Slug = "bether", Name = "Bether", Kind = EntityKind.Place },
            new Entity
            {
                Slug = "abram", Name = "Abram", Kind = EntityKind.Person,
                Names =
                [
                    new EntityName { Label = "Abram", Kind = "proper name" },
                    new EntityName { Label = "Abraham", Kind = "proper name" },
                    new EntityName { Label = "Abraham our father", Kind = "title" },
                ],
            },
            new Entity { Slug = "towerofthehundred", Name = "Tower of the Hundred", Kind = EntityKind.Place },
            new Entity { Slug = "hosea", Name = "Hosea", Kind = EntityKind.Person, Names = [new EntityName { Label = "Hoshea", Kind = "proper name" }] },
            new Entity { Slug = "joshua", Name = "Joshua", Kind = EntityKind.Person, Names = [new EntityName { Label = "Hoshea", Kind = "proper name" }] });
        _db.SaveChanges();

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

    private PageLoader Loader() => new(_db, Canon(), new SiteSettingsFile(Path.Combine(_folder, "settings.json"),
        NullLogger<SiteSettingsFile>.Instance), _cache, new DownloadsCatalogue(null, null, NullLogger<DownloadsCatalogue>.Instance));

    private async Task<PageContent> Load(string path) =>
        await Loader().Load(PageAddress.Parse(path), PageWording.Compiled("en"), default);

    [Fact]
    public async Task APsalmsSuperscriptionIsAddressedByItsChapterAndNoSitemapListsVerseZero()
    {
        var title = PageAddress.Parse("/uk/read/psalms/49/0");

        title.Kind.Should().Be(PageKind.Chapter);
        title.English.Should().Be("/read/psalms/49");
        PageEndpoints.Respelled(title, "/uk/read/psalms/49/0", new Dictionary<string, string>())
            .Should().Be("/uk/read/psalms/49");
        PageEndpoints.Respelled(PageAddress.Parse("/read/psalms/49/0"), "/read/psalms/49/0", new Dictionary<string, string>())
            .Should().Be("/read/psalms/49");

        var verses = await new Sitemaps(_db, Canon(), _cache).Addresses("verses-psalms.xml", default);
        verses.Should().Equal("/read/psalms/49/1", "/read/psalms/49/2");
    }

    /// <summary>
    /// A verse number no text places in the chapter — the Greek Esther's or the Ge'ez Psalter's own
    /// numbers written as the frame's — opens the chapter it was asked in; a chapter nothing holds stays
    /// a page that names nothing.
    /// </summary>
    [Fact]
    public async Task AVerseNoTextPlacesIsSentToItsChapter()
    {
        var beyond = await Load("/de/read/esther/5/32");

        beyond.Status.Should().Be(StatusCodes.Status301MovedPermanently);
        beyond.MovedTo.Should().Be("/read/esther/5");
        (await Load("/read/psalms/49/2")).Status.Should().Be(StatusCodes.Status200OK);
        (await Load("/read/esther/3/18")).Status.Should().Be(StatusCodes.Status404NotFound, "no text holds Esther 3 here");
        (await Load("/read/esther/9/1")).Status.Should().Be(StatusCodes.Status404NotFound, "the frame has no ninth chapter here");
    }

    /// <summary>
    /// A withdrawn record whose word another record holds sends its old address there, as a folded
    /// record's does, whatever kind of record that is; one nothing took the place of is gone.
    /// </summary>
    [Fact]
    public async Task AWithdrawnRecordsAddressLeadsToTheRecordHoldingItsWordOrIsGone()
    {
        var gibbar = await Load("/places/gibbar-2");
        gibbar.Status.Should().Be(StatusCodes.Status301MovedPermanently);
        gibbar.MovedTo.Should().Be("/people/gibbar");
        (await Load("/uk/places/bether-2")).MovedTo.Should().Be("/places/bether");
        (await Load("/es/places/mahalab")).Status.Should().Be(StatusCodes.Status410Gone);
        (await Load("/places/nowhere-at-all")).Status.Should().Be(StatusCodes.Status404NotFound);

        (await MergedAddresses.Current(_db, "gibbar-2", default)).Should().Be("gibbar");
        _db.Entities.Add(new Entity { Slug = "river-2", Name = "River", Kind = EntityKind.Place });
        await _db.SaveChangesAsync();
        (await MergedAddresses.Current(_db, "river-2", default)).Should().Be("river-2", "a record holding the address again keeps it");
    }

    /// <summary>
    /// An address guessed from another of a record's names opens the record: a proper name it holds,
    /// its own name spelled with hyphens. A title is not a name, a name two records share leads
    /// nowhere, and a name is looked for only among records of the address's kind.
    /// </summary>
    [Fact]
    public async Task AnotherNameOfARecordLeadsToIt()
    {
        var abraham = await Load("/people/abraham");
        abraham.Status.Should().Be(StatusCodes.Status301MovedPermanently);
        abraham.MovedTo.Should().Be("/people/abram");
        (await Load("/uk/places/tower-of-the-hundred")).MovedTo.Should().Be("/places/towerofthehundred");
        (await Load("/people/abraham/verses")).MovedTo.Should().Be("/people/abram/verses");

        (await Load("/people/abraham-our-father")).Status.Should().Be(StatusCodes.Status404NotFound, "a title is not a name");
        (await Load("/people/hoshea")).Status.Should().Be(StatusCodes.Status404NotFound, "two records hold the name");
        (await Load("/places/abraham")).Status.Should().Be(StatusCodes.Status404NotFound, "no place holds the name");
        (await Load("/es/places/mahalab")).Status.Should().Be(StatusCodes.Status410Gone);
    }

    /// <summary>A letter outside ASCII is percent-encoded wherever an address leaves as one: a redirect, a sitemap, a canonical link.</summary>
    [Fact]
    public async Task AnAddressWithALetterOutsideAsciiLeavesEscaped()
    {
        PageAddress.Escaped("/de/events/león").Should().Be("/de/events/le%C3%B3n");
        PageAddress.Escaped("/events/geʽezscript").Should().Be("/events/ge%CA%BDezscript");
        PageAddress.Escaped("/read/mark/3?v=3").Should().Be("/read/mark/3?v=3");
        PageEndpoints.Respelled(PageAddress.Parse("/de/events/león/"), "/de/events/león/", new Dictionary<string, string>())
            .Should().Be("/de/events/le%C3%B3n");
        PageEndpoints.Respelled(PageAddress.Parse("/de/events/león"), "/de/events/león", new Dictionary<string, string>())
            .Should().BeNull();

        using var output = new MemoryStream();
        await Sitemaps.Write(output, "https://essenthos.org", ["/events/medellín"]);
        var xml = System.Text.Encoding.UTF8.GetString(output.ToArray());
        xml.Should().Contain("<loc>https://essenthos.org/uk/events/medell%C3%ADn</loc>")
            .And.Contain("href=\"https://essenthos.org/events/medell%C3%ADn\"")
            .And.NotContain("medellín");
        PageHtml.Alternates("https://essenthos.org", "/events/león").Should().OnlyContain(link => link.Url.EndsWith("/events/le%C3%B3n"));
    }

    /// <summary>Every address the sitemaps of verses and records list opens a page.</summary>
    [Fact]
    public async Task EveryAddressTheSitemapsListOpens()
    {
        var sitemaps = new Sitemaps(_db, Canon(), _cache);
        var listed = new List<string>();
        foreach (var name in (await sitemaps.Names(default)).Where(name => name.StartsWith("verses-") || name.StartsWith("records-")))
        {
            listed.AddRange(await sitemaps.Addresses(name + ".xml", default) ?? []);
        }

        listed.Should().Contain(["/read/psalms/49/1", "/read/esther/5/14", "/people/gibbar"]).And.NotContain("/read/psalms/49/0");
        var failing = new List<string>();
        foreach (var address in listed)
        {
            if ((await Load(address)).Status != StatusCodes.Status200OK)
            {
                failing.Add(address);
            }
        }

        failing.Should().BeEmpty();
    }

    /// <summary>The whole way a crawler takes: each old address answers with a redirect to a page, or with Gone.</summary>
    [Fact]
    public async Task TheServerAnswersEachKindWithARedirectOrGone()
    {
        var shell = Path.Combine(_folder, "index.html");
        await File.WriteAllTextAsync(shell, "<html><head><title>Essenthos</title></head><body><div id=\"root\"></div></body></html>");
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            { [PageShell.ConfigurationKey] = shell, [PageEndpoints.OriginKey] = "https://essenthos.org" });
        builder.Services.AddScoped(_ => _database.NewContext());
        builder.Services.AddSingleton<ICanonIndex>(sp => new CanonIndex(sp.GetRequiredService<IServiceScopeFactory>()));
        builder.Services.AddSingleton<PageCache>();
        builder.Services.AddSingleton(new SiteSettingsFile(Path.Combine(_folder, "settings.json"), NullLogger<SiteSettingsFile>.Instance));
        builder.Services.AddSingleton<PageShell>();
        builder.Services.AddSingleton(new DownloadsCatalogue(null, null, NullLogger<DownloadsCatalogue>.Instance));
        builder.Services.AddScoped<PageLoader>();
        builder.Services.AddScoped<Sitemaps>();
        await using var app = builder.Build();
        app.MapGroup("/v1").MapPages();
        await app.StartAsync();
        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = new Uri(app.Urls.Single()) };

        foreach (var (asked, arrives) in new[]
                 {
                     ("/uk/read/psalms/49/0", "/uk/read/psalms/49"),
                     ("/read/psalms/49/0", "/read/psalms/49"),
                     ("/de/read/esther/5/32", "/de/read/esther/5"),
                     ("/es/places/gibbar-2", "/es/people/gibbar"),
                     ("/places/bether-2", "/places/bether"),
                     ("/people/abraham", "/people/abram"),
                     ("/de/people/abram/", "/de/people/abram"),
                 })
        {
            var answer = await client.GetAsync("/v1/pages" + asked);
            answer.StatusCode.Should().Be(HttpStatusCode.PermanentRedirect, asked);
            answer.Headers.Location!.OriginalString.Should().Be(arrives, asked);
            (await client.GetAsync("/v1/pages" + arrives)).StatusCode.Should().Be(HttpStatusCode.OK, arrives);
        }

        (await client.GetAsync("/v1/pages/de/places/mahalab")).StatusCode.Should().Be(HttpStatusCode.Gone);
        var slashed = await client.GetAsync("/v1/pages/de/events/le%C3%B3n/");
        slashed.StatusCode.Should().Be(HttpStatusCode.PermanentRedirect);
        slashed.Headers.Location!.OriginalString.Should().Be("/de/events/le%C3%B3n");
        await app.StopAsync();
    }
}
