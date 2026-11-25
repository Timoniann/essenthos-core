using System.Net;
using System.Text.RegularExpressions;
using Essenthos.Core.Corpus;
using Essenthos.Core.Endpoints;
using Essenthos.Core.Pages;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests.Reader;

/// <summary>
/// The home page as a crawler or a language model reads it: what Essenthos is, what is on it and how
/// its links are made, in each language, inside the page's main landmark; and the site in Markdown for
/// a client that asks for Markdown, named in a Link header for one that does not.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class HomePageTests(WitnessDatabase database) : IDisposable
{
    private const string Origin = "https://essenthos.org";

    private const string Shell =
        "<!doctype html><html lang=\"en\"><head><title>Essenthos</title></head><body><div id=\"root\"></div></body></html>";

    private const string Llms = "# Essenthos\n\n> A free, non-commercial place to study the Bible.\n";

    private readonly string _folder = Directory.CreateTempSubdirectory("home-page-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Theory]
    [InlineData("en", "What Essenthos is", "Reading the text", "Reference works", "How the links are made and checked", "Open data")]
    [InlineData("uk", "Що таке Essenthos", "Читання тексту", "Довідкові праці", "Як створюють і перевіряють зв’язки між словами", "Відкриті дані")]
    [InlineData("de", "Was Essenthos ist", "Den Text lesen", "Nachschlagewerke", "Wie die Verknüpfungen entstehen und geprüft werden", "Offene Daten")]
    [InlineData("es", "Qué es Essenthos", "Leer el texto", "Obras de consulta", "Cómo se hacen y se comprueban los enlaces", "Datos abiertos")]
    public void TheHomePageSaysWhatTheSiteIsUnderItsHeadingsInItsLanguage(
        string code, string site, string reading, string reference, string method, string open)
    {
        var language = SiteLanguage.All.Single(l => l.Code == code);
        var page = PageViews.Home(language, PageWording.Compiled(code), [new BookLink(1, "Genesis")]);
        var html = PageHtml.Render(Shell, page, language, Origin);

        Regex.Matches(page.Body, "<section><h2>([^<]*)</h2>").Select(m => WebUtility.HtmlDecode(m.Groups[1].Value))
            .Should().Equal(site, reading, reference, method, open);
        var sections = Regex.Matches(page.Body, "<section>.*?</section>").Select(m => m.Value).ToList();
        sections.Should().AllSatisfy(section => section.Should().MatchRegex("^<section><h2>[^<]+</h2>(<h3>[^<]+</h3><p>[^<]+</p>)+</section>$"));
        var words = sections.Sum(section =>
            Regex.Replace(section, "<[^>]+>", " ").Split(' ', StringSplitOptions.RemoveEmptyEntries).Length);
        words.Should().BeInRange(650, 1000, "the sections describe the site in some detail, as plain text");
        page.Body.Should().NotContain("home.about", "every key has its text");
        html.Should().MatchRegex("<div id=\"root\"><main><article class=\"prerendered\" lang=\"" + code + "\"><h1>Essenthos</h1>.*</article></main></div>");
    }

    /// <summary>The same points in every language, so a panel built from them shows each language alike.</summary>
    [Fact]
    public void EveryLanguageHasTheSamePoints()
    {
        string[] Points(PageWording t) =>
            [.. t.Keys("home.about").SelectMany(section => t.Keys($"home.about.{section}.points").Select(point => $"{section}.{point}"))];

        var english = Points(PageWording.Compiled("en"));
        english.Should().HaveCountGreaterThan(15);
        foreach (var code in new[] { "uk", "de", "es" })
        {
            Points(PageWording.Compiled(code)).Should().Equal(english, code);
        }
    }

    [Fact]
    public void TheHomePageSaysWhatItCanStandBehind()
    {
        var english = PageViews.Home(SiteLanguage.English, PageWording.Compiled("en"), []).Body;
        english.Should().Contain("The interface is available in four languages: English, Ukrainian, German and Spanish.")
            .And.Contain("most words are linked")
            .And.Contain("never shown as one a source states")
            .And.Contain("points to the verses it comes from")
            .And.NotContain("every word of", "not every word is linked");
        PageViews.Home(SiteLanguage.Ukrainian, PageWording.Compiled("uk"), []).Body
            .Should().Contain("гебрейськ").And.Contain("проект").And.NotContain("єврейськ").And.NotContain("проєкт");
    }

    [Fact]
    public void APageWithNothingWrittenLeavesTheRootEmpty() =>
        PageHtml.Render(Shell, PageViews.Search(PageWording.Compiled("en"), "love"), SiteLanguage.English, Origin)
            .Should().Contain("<div id=\"root\"></div>").And.NotContain("<main>");

    [Theory]
    [InlineData("text/markdown", true)]
    [InlineData("text/markdown, text/html;q=0.9", true)]
    [InlineData("text/plain, text/markdown;q=0.8, */*;q=0.1", true)]
    [InlineData("text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8", false)]
    [InlineData("text/html, text/markdown", false)]
    [InlineData("text/markdown;q=0.5, text/html", false)]
    [InlineData("*/*", false)]
    [InlineData("", false)]
    public void MarkdownIsAnsweredOnlyToAClientThatRanksItAboveHtml(string accept, bool markdown)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Accept = accept;
        PageEndpoints.PrefersMarkdown(context.Request).Should().Be(markdown);
    }

    [Fact]
    public async Task TheHomePageAnswersMarkdownWhenAskedAndNamesItOtherwise()
    {
        await File.WriteAllTextAsync(Path.Combine(_folder, "llms.txt"), Llms);
        await using var app = await Started();
        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };

        foreach (var home in new[] { "/v1/pages/", "/v1/pages/uk/" })
        {
            using var asked = new HttpRequestMessage(HttpMethod.Get, home);
            asked.Headers.TryAddWithoutValidation("Accept", "text/markdown, text/html;q=0.9");
            var markdown = await client.SendAsync(asked);
            markdown.StatusCode.Should().Be(HttpStatusCode.OK, home);
            markdown.Content.Headers.ContentType!.ToString().Should().Be("text/markdown; charset=utf-8");
            (await markdown.Content.ReadAsStringAsync()).Should().Be(Llms);
            markdown.Headers.Vary.Should().Equal("Accept");

            var html = await client.GetAsync(home);
            html.Content.Headers.ContentType!.MediaType.Should().Be("text/html");
            html.Headers.Vary.Should().Equal("Accept");
            html.Headers.GetValues("Link").Should().Equal("</llms.txt>; rel=\"alternate\"; type=\"text/markdown\"");
            (await html.Content.ReadAsStringAsync()).Should().Contain("<main><article class=\"prerendered\"");
        }

        using var notHome = new HttpRequestMessage(HttpMethod.Get, "/v1/pages/lexicon");
        notHome.Headers.TryAddWithoutValidation("Accept", "text/markdown");
        var section = await client.SendAsync(notHome);
        section.Content.Headers.ContentType!.MediaType.Should().Be("text/html", "only the home page has a Markdown form");
        section.Headers.Vary.Should().BeEmpty();
        section.Headers.Contains("Link").Should().BeFalse();
        await app.StopAsync();
    }

    [Fact]
    public async Task WithoutLlmsTxtTheHomePageNamesNoMarkdownAndAnswersHtml()
    {
        await using var app = await Started();
        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        using var asked = new HttpRequestMessage(HttpMethod.Get, "/v1/pages/");
        asked.Headers.TryAddWithoutValidation("Accept", "text/markdown");
        var answer = await client.SendAsync(asked);
        answer.StatusCode.Should().Be(HttpStatusCode.OK);
        answer.Content.Headers.ContentType!.MediaType.Should().Be("text/html");
        answer.Headers.Contains("Link").Should().BeFalse();
        await app.StopAsync();
    }

    private async Task<WebApplication> Started()
    {
        var shell = Path.Combine(_folder, "index.html");
        await File.WriteAllTextAsync(shell, Shell);
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            { [PageShell.ConfigurationKey] = shell, [PageEndpoints.OriginKey] = Origin });
        builder.Services.AddScoped(_ => database.NewContext());
        builder.Services.AddSingleton<ICanonIndex>(sp => new CanonIndex(sp.GetRequiredService<IServiceScopeFactory>()));
        builder.Services.AddSingleton<PageCache>();
        builder.Services.AddSingleton(new SiteSettingsFile(Path.Combine(_folder, "settings.json"), NullLogger<SiteSettingsFile>.Instance));
        builder.Services.AddSingleton<PageShell>();
        builder.Services.AddSingleton<PageValidator>();
        builder.Services.AddSingleton(new DownloadsCatalogue(null, null, NullLogger<DownloadsCatalogue>.Instance));
        builder.Services.AddScoped<PageLoader>();
        builder.Services.AddScoped<Sitemaps>();
        builder.Services.AddSingleton<SitemapDates>();
        var app = builder.Build();
        app.MapGroup("/v1").MapPages();
        await app.StartAsync();
        return app;
    }
}
