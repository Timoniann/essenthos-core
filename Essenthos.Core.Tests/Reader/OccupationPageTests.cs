using System.Net;
using System.Xml.Linq;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Endpoints;
using Essenthos.Core.Pages;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests.Reader;

[Collection(WitnessDatabaseCollection.Name)]
public sealed class OccupationPageTests : IDisposable
{
    private readonly WitnessDatabase _database;
    private readonly AppDbContext _db;
    private readonly PageCache _cache = new();
    private readonly string _folder = Directory.CreateTempSubdirectory("occupation-pages-").FullName;
    private static readonly string[] Names = ["Physician", "Лікар", "Arzt", "Médico"];

    public OccupationPageTests(WitnessDatabase database)
    {
        _database = database;
        _db = database.NewContext();
        _db.Database.ExecuteSqlRaw("DELETE FROM occupation; DELETE FROM entity");
        var person = new Entity { Slug = "luke", Name = "Luke", Kind = EntityKind.Person, Sex = "male", Source = "test", SourceId = "test:luke" };
        _db.Entities.Add(person);
        _db.SaveChanges();
        _db.EntityNameForms.Add(new EntityNameForm { EntityId = person.Id, Language = "ukr", Form = "Лука",
            GrammaticalCase = GrammaticalCases.Nominative, Source = "test" });
        var occupation = new Occupation { Slug = "physician", Category = "trade" };
        for (var i = 0; i < SiteLanguage.All.Count; i++)
            occupation.Labels.Add(new OccupationLabel { Language = SiteLanguage.All[i].Corpus, Name = Names[i], Masculine = Names[i], Feminine = Names[i] });
        occupation.People.Add(new PersonOccupation { Person = person, Source = "test",
            Witnesses = [new OccupationWitness { Kind = "profession", Text = "KJV", Reference = "COL 4:14", CanonicalBook = 51,
                CanonicalChapter = 4, CanonicalVerse = 14, Quote = "beloved physician" }] });
        _db.Occupations.Add(occupation);
        _db.Occupations.Add(new Occupation { Slug = "unpublished", Category = "office" });
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _cache.Dispose();
        _db.Dispose();
        Directory.Delete(_folder, recursive: true);
    }

    private PageLoader Loader() => new(_db, new EmptyCanon(), new SiteSettingsFile(Path.Combine(_folder, "settings.json"),
        NullLogger<SiteSettingsFile>.Instance), _cache, new DownloadsCatalogue(null, null, NullLogger<DownloadsCatalogue>.Instance));

    [Fact]
    public async Task EveryLocaleHasRealCatalogueDetailTitlesCanonicalAlternatesAndReaderLinks()
    {
        const string shell = "<!doctype html><html lang=\"en\"><head><title>Essenthos</title></head><body><div id=\"root\"></div></body></html>";
        for (var i = 0; i < SiteLanguage.All.Count; i++)
        {
            var language = SiteLanguage.All[i];
            var wording = PageWording.Compiled(language.Code);
            var catalogue = await Loader().Load(PageAddress.Parse(language.Path("/occupations")), wording, default);
            catalogue.Status.Should().Be(200);
            catalogue.Title.Should().Be(wording.Titled(wording.Text("page.occupations.title")));
            catalogue.Body.Should().Contain($"href=\"{language.Path("/occupations/physician")}\"").And.Contain(Names[i]);
            catalogue.Canonical.Should().Be("/occupations");
            catalogue.Description.Should().Be(wording.Text("page.occupations.description"));
            var detail = await Loader().Load(PageAddress.Parse(language.Path("/occupations/physician")), wording, default);
            detail.Status.Should().Be(200);
            detail.Title.Should().Be(wording.Titled(Names[i]));
            detail.Description.Should().Be(wording.Text("occupation.description", ("name", Names[i])));
            detail.Body.Should().Contain($"href=\"{language.Path("/people/luke")}\"")
                .And.Contain($"href=\"{language.Path("/read/colossians/4/14")}\"")
                .And.Contain(wording.Reference(wording.CitedBook(51)!, 4, 14));
            if (language == SiteLanguage.Ukrainian) detail.Body.Should().Contain("Лука");
            var html = PageHtml.Render(shell, detail, language, "https://essenthos.org");
            html.Should().Contain($"rel=\"canonical\" href=\"https://essenthos.org{language.Path("/occupations/physician")}\"")
                .And.Contain("hreflang=\"uk\"").And.Contain("hreflang=\"en\"").And.Contain("hreflang=\"de\"").And.Contain("hreflang=\"es\"")
                .And.NotContain("occupation.description").And.NotContain("page.occupations");
        }
    }

    [Fact]
    public async Task APersonsPageLinksTheTradeItsLineNamesToTheOccupationsPageInEveryLanguage()
    {
        for (var i = 0; i < SiteLanguage.All.Count; i++)
        {
            var language = SiteLanguage.All[i];
            var page = await Loader().Load(PageAddress.Parse(language.Path("/people/luke")), PageWording.Compiled(language.Code), default);
            page.Status.Should().Be(200, $"the {language.Code} page of a person whose line names a trade renders");
            page.Body.ToLowerInvariant().Should().Contain($"<a href=\"{language.Path("/occupations/physician")}\">{Names[i].ToLowerInvariant()}</a>");
        }
    }

    [Theory]
    [InlineData("en", "Heavenly role")]
    [InlineData("uk", "Небесна роль")]
    [InlineData("de", "Himmlische Rolle")]
    [InlineData("es", "Función celestial")]
    public async Task HeavenlyCategoryIsClearlyMarkedBeforeClientScriptsRun(string locale, string marker)
    {
        var occupation = await _db.Occupations.SingleAsync(o => o.Slug == "physician");
        occupation.Category = "heavenly_role";
        await _db.SaveChangesAsync();
        var wording = PageWording.Compiled(locale);
        var language = SiteLanguage.All.Single(l => l.Code == locale);
        var catalogue = await Loader().Load(PageAddress.Parse(language.Path("/occupations")), wording, default);
        WebUtility.HtmlDecode(catalogue.Body).Should().Contain(marker);
        var page = await Loader().Load(PageAddress.Parse(language.Path("/occupations/physician")), wording, default);
        WebUtility.HtmlDecode(page.Body).Should().Contain(marker);
    }

    [Theory]
    [InlineData("q", "Arzt")]
    [InlineData("category", "trade")]
    [InlineData("skip", "100")]
    public async Task FilteredAndPagedCataloguesAreNotIndexed(string key, string value)
    {
        var address = PageAddress.Parse("/de/occupations", new Dictionary<string, string> { [key] = value });
        address.Indexed.Should().BeFalse();
        var page = await Loader().Load(address, PageWording.Compiled("de"), default);
        page.Status.Should().Be(200);
        page.Indexed.Should().BeFalse();
        page.Canonical.Should().BeNull();
    }

    [Fact]
    public async Task MissingAndUnpublishedDetailsReturn404WithoutCanonical()
    {
        foreach (var slug in new[] { "missing", "unpublished" })
        {
            var page = await Loader().Load(PageAddress.Parse("/occupations/" + slug), PageWording.Compiled("en"), default);
            page.Status.Should().Be(404);
            page.Indexed.Should().BeFalse();
            page.Canonical.Should().BeNull();
        }
    }

    [Fact]
    public async Task SitemapsOfferCatalogueAndOnlyAdmittedDetailsInAllLanguages()
    {
        var sitemaps = new Sitemaps(_db, new EmptyCanon(), _cache);
        (await sitemaps.Names(default)).Should().Contain("occupations");
        (await sitemaps.Addresses("pages.xml", default))!.Should().Contain("/occupations");
        var addresses = await sitemaps.Addresses("occupations.xml", default);
        addresses.Should().Equal("/occupations/physician");
        using var bytes = new MemoryStream();
        await Sitemaps.Write(bytes, "https://essenthos.org", addresses!);
        bytes.Position = 0;
        var xml = XDocument.Load(bytes);
        XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";
        xml.Root!.Elements(ns + "url").Should().HaveCount(4);
        xml.Descendants(ns + "loc").Select(e => e.Value).Should()
            .BeEquivalentTo(SiteLanguage.All.Select(language => "https://essenthos.org" + language.Path("/occupations/physician")));
    }

    [Fact]
    public async Task PageEndpointReturnsRealHtmlAndStatusBeforeClientScriptsRun()
    {
        var shellPath = Path.Combine(_folder, "index.html");
        await File.WriteAllTextAsync(shellPath, "<html><head><title>Essenthos</title></head><body><div id=\"root\"></div></body></html>");
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            { [PageShell.ConfigurationKey] = shellPath, [PageEndpoints.OriginKey] = "https://essenthos.org" });
        builder.Services.AddScoped(_ => _database.NewContext());
        builder.Services.AddSingleton<ICanonIndex>(new EmptyCanon());
        builder.Services.AddSingleton<PageCache>();
        builder.Services.AddSingleton(new SiteSettingsFile(Path.Combine(_folder, "settings.json"), NullLogger<SiteSettingsFile>.Instance));
        builder.Services.AddSingleton<PageShell>();
        builder.Services.AddSingleton(new DownloadsCatalogue(null, null, NullLogger<DownloadsCatalogue>.Instance));
        builder.Services.AddScoped<PageLoader>();
        builder.Services.AddScoped<Sitemaps>();
        await using var app = builder.Build();
        app.MapGroup("/v1").MapPages();
        await app.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        var detail = await client.GetAsync("/v1/pages/uk/occupations/physician");
        detail.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = WebUtility.HtmlDecode(await detail.Content.ReadAsStringAsync());
        html.Should().Contain("<title>Лікар · Essenthos</title>").And.Contain("/uk/read/colossians/4/14");
        (await client.GetAsync("/v1/pages/occupations/missing")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync("/v1/sitemaps/occupations.xml")).StatusCode.Should().Be(HttpStatusCode.OK);
        using var unfollowed = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = client.BaseAddress };
        var respelled = await unfollowed.GetAsync("/v1/pages/uk/occupations/physician/");
        respelled.StatusCode.Should().Be(HttpStatusCode.PermanentRedirect);
        respelled.Headers.Location!.OriginalString.Should().Be("/uk/occupations/physician");
        (await unfollowed.GetAsync("/v1/pages/uk/")).StatusCode.Should().Be(HttpStatusCode.OK, "the Ukrainian home page is /uk/");
        await app.StopAsync();
    }

    private sealed class EmptyCanon : ICanonIndex
    {
        public Task<TextEntry?> Text(string slug, CancellationToken cancellationToken) => Task.FromResult<TextEntry?>(null);
        public Task<IReadOnlyList<TextEntry>> Texts(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<TextEntry>>([]);
        public Task<int> ChapterCount(int canonicalBook, CancellationToken cancellationToken) => Task.FromResult(0);
        public Task<int> ChapterCountAcross(IEnumerable<int> textIds, int canonicalBook, CancellationToken cancellationToken) => Task.FromResult(0);
        public Task<int> ChapterCountIn(int textId, int canonicalBook, CancellationToken cancellationToken) => Task.FromResult(0);
        public void Forget() { }
    }
}
