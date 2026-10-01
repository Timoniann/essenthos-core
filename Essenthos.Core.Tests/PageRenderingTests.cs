using System.Text.RegularExpressions;
using Essenthos.Core.Pages;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// What the server writes into the reader's index.html for a search engine or a link preview: the
/// page's own title and description in its language, its addresses in every language, text that
/// cannot break out of the markup, and a 404 for an address that names nothing.
/// </summary>
public sealed class PageRenderingTests
{
    private const string Origin = "https://essenthos.org";

    private const string Shell = """
        <!doctype html>
        <html lang="en" class="h-full">
          <head>
            <meta charset="utf-8" />
            <meta
              name="description"
              content="Essenthos — read the Bible against its Hebrew and Greek originals, word by word."
            />
            <title>Essenthos</title>
            <meta property="og:type" content="website" />
            <meta property="og:title" content="Essenthos" />
            <meta property="og:image" content="http://web:8080/og-image.png" />
            <meta name="twitter:card" content="summary_large_image" />
            <script type="module" crossorigin src="/assets/index-abc.js"></script>
          </head>
          <body class="h-full">
            <div id="root" class="h-full"></div>
          </body>
        </html>
        """;

    private static readonly PageWording Ukrainian = PageWording.Compiled("uk");

    private static VerseView Mark33(string text) =>
        new(41, "Від Марка", 3, 3, text, "Біблія Огієнка") { Previous = 2, Next = 4 };

    private static string VersePage(string text) =>
        PageHtml.Render(Shell, PageViews.Verse(SiteLanguage.Ukrainian, Ukrainian, Mark33(text)), SiteLanguage.Ukrainian, Origin);

    [Fact]
    public void AVersePageInUkrainianIsTitledWithItsAddressTheVerseAndTheText()
    {
        var html = VersePage("І говорить Він до чоловіка з сухою рукою: Стань посередині!");

        Regex.Matches(html, "<title>").Should().HaveCount(1, "the shell's own title is replaced, not joined");
        Title(html).Should().Contain("Марка 3:3")
            .And.Be("Від Марка 3:3 — «І говорить Він до чоловіка з сухою рукою: Стань посередині!» — Біблія Огієнка · Essenthos");
        html.Should().Contain("<html lang=\"uk\"");
        Meta(html, "description").Should().StartWith("Від Марка 3:3, Біблія Огієнка: «І говорить");
        html.Should().NotContain("http://web:8080/og-image.png", "the shell's preview tags give way to the page's");
        html.Should().Contain($"<meta property=\"og:image\" content=\"{Origin}/og-image.png\">");
    }

    [Fact]
    public void AGermanVerseIsCitedAsAGermanReaderCitesIt()
    {
        var german = PageWording.Compiled("de");
        var html = PageHtml.Render(
            Shell,
            PageViews.Verse(SiteLanguage.German, german,
                new VerseView(41, german.CitedBook(41)!, 3, 3, "Und er sprach zu dem Menschen mit der verdorrten Hand: Tritt hervor!", "Lutherbibel 1912")),
            SiteLanguage.German,
            Origin);

        Title(html).Should().StartWith("Markus 3,3 — „Und er sprach");
        german.CitedBook(1).Should().Be("1. Mose");
        german.CitedBook(13).Should().Be("1. Chronik");
        PageWording.Compiled("es").Reference(PageWording.Compiled("es").CitedBook(40)!, 5, 3).Should().Be("Mateo 5:3");
        PageWording.Compiled("uk").CitedBook(70).Should().Be("Товит", "a book the Ohienko does not hold still has a Ukrainian name");
    }

    [Fact]
    public void EveryLanguageOfTheVerseIsNamedAndEnglishIsTheDefault()
    {
        var html = VersePage("Стань посередині!");

        html.Should().Contain($"<link rel=\"canonical\" href=\"{Origin}/uk/read/mark/3/3\">");
        Regex.Matches(html, "<link rel=\"alternate\" hreflang=\"([^\"]+)\" href=\"([^\"]+)\">")
            .Select(match => (match.Groups[1].Value, match.Groups[2].Value))
            .Should().Equal(
                ("en", $"{Origin}/read/mark/3/3"),
                ("uk", $"{Origin}/uk/read/mark/3/3"),
                ("de", $"{Origin}/de/read/mark/3/3"),
                ("es", $"{Origin}/es/read/mark/3/3"),
                ("x-default", $"{Origin}/read/mark/3/3"));
    }

    [Fact]
    public void TextFromTheCorpusIsEscapedInTheHeadAndInThePage()
    {
        var html = VersePage("<script>alert(\"x\")</script> & 'so'");

        html.Should().NotContain("<script>alert");
        html.Should().Contain("<p>&lt;script&gt;alert(&quot;x&quot;)&lt;/script&gt; &amp; &#39;so&#39;</p>");
        Regex.Match(html, "<meta name=\"description\" content=\"([^\"]*)\">").Groups[1].Value
            .Should().Contain("alert(&quot;x&quot;)");
    }

    [Fact]
    public void TheVersesEssentialsGoInsideTheRootWhereTheApplicationMounts()
    {
        var html = VersePage("Стань посередині!");

        html.Should().MatchRegex("<div id=\"root\" class=\"h-full\"><article class=\"prerendered\" lang=\"uk\">.*<h1>Від Марка 3:3</h1>.*</article></div>");
        html.Should().Contain("<a href=\"/uk/read/mark/3/2\" rel=\"prev\">");
        html.Should().Contain("<a href=\"/uk/read/mark/3\">Від Марка 3</a>");
    }

    [Fact]
    public void AnAddressThatNamesNothingIsANotFoundPageNobodyIndexes()
    {
        var address = PageAddress.Parse("/uk/nowhere/at/all");
        var page = PageViews.NotFound(address.Language, Ukrainian);
        var html = PageHtml.Render(Shell, page, address.Language, Origin);

        address.Kind.Should().Be(PageKind.NotFound);
        page.Status.Should().Be(404);
        Title(html).Should().Be("Такої сторінки немає · Essenthos");
        html.Should().Contain("<meta name=\"robots\" content=\"noindex, follow\">");
        html.Should().NotContain("rel=\"canonical\"");
    }

    [Theory]
    [InlineData("/read/MRK/3", "Chapter", "/read/mark/3")]
    [InlineData("/uk/read/mark/3", "Chapter", "/read/mark/3")]
    [InlineData("/de/read/mark/3/3", "Verse", "/read/mark/3/3")]
    [InlineData("/uk/", "Home", "/")]
    [InlineData("/es/people/moses", "Record", "/people/moses")]
    [InlineData("/strong/h430", "Strong", "/strong/H430")]
    [InlineData("/timeline/kings", "Section", "/timeline/kings")]
    [InlineData("/uk/genealogies/jesus", "Section", "/genealogies/jesus")]
    [InlineData("/read/mark/0", "NotFound", "/read/mark/0")]
    [InlineData("/read/nobook/1", "NotFound", "/read/nobook/1")]
    [InlineData("/fr/read/mark/3", "NotFound", "/fr/read/mark/3")]
    public void AnAddressIsReadToOneCanonicalSpelling(string path, string kind, string english)
    {
        var address = PageAddress.Parse(path);

        address.Kind.ToString().Should().Be(kind);
        address.English.Should().Be(english);
    }

    [Fact]
    public void AChapterAddressWithAVerseIsTheVersesPage()
    {
        var address = PageAddress.Parse("/uk/read/mark/3", new Dictionary<string, string> { ["v"] = "3" });

        address.Kind.Should().Be(PageKind.Verse);
        address.English.Should().Be("/read/mark/3/3");
    }

    [Fact]
    public void AReadersOwnPagesAndTheListsAQueryPickedAreNotIndexed()
    {
        PageAddress.Parse("/uk/account").Indexed.Should().BeFalse();
        PageAddress.Parse("/admin/users").Indexed.Should().BeFalse();
        PageAddress.Parse("/suggestions/new").Indexed.Should().BeFalse();
        PageAddress.Parse("/search", new Dictionary<string, string> { ["q"] = "love" }).Indexed.Should().BeFalse();
        PageAddress.Parse("/encyclopedia", new Dictionary<string, string> { ["q"] = "a" }).Indexed.Should().BeFalse();
        PageAddress.Parse("/encyclopedia").Indexed.Should().BeTrue();
        PageAddress.Parse("/search").Indexed.Should().BeTrue();
    }

    [Fact]
    public void TheEnglishPrefixIsSentToTheAddressWithoutIt()
    {
        PageAddress.Parse("/en/read/mark/3").RedirectTo.Should().Be("/read/mark/3");
    }

    [Theory]
    [InlineData("one two three four five", 10, "one two…")]
    [InlineData("short", 10, "short")]
    [InlineData("  spaced\n  out  ", 20, "spaced out")]
    [InlineData("abcdefghijklmnop qr", 10, "abcdefghi…")]
    [InlineData("Stand up, in the middle; now", 25, "Stand up, in the middle…")]
    public void TextIsShortenedByTheReadersOwnRule(string text, int limit, string expected)
    {
        PageViews.Shortened(text, limit).Should().Be(expected);
    }

    [Theory]
    [InlineData("son of Jesse (RUT 4:17), King of Israel (2SA 5:3)", "son of Jesse, King of Israel")]
    [InlineData("whom the text calls the Holy Spirit (MAT 28:19; 1CO 3:16)", "whom the text calls the Holy Spirit")]
    [InlineData("son of Esau (GEN 36:4, 10, 15)", "son of Esau")]
    [InlineData("an angel (GEN 16:7, GEN 22:11, etc.)", "an angel")]
    [InlineData("the Paltite (Pelonite in 1CH 11:27)", "the Paltite (Pelonite in 1 Chronicles 11:27)")]
    [InlineData("a city (in Judah)", "a city (in Judah)")]
    public void ARecordsLineLosesTheCodesItsSourceWroteIntoIt(string line, string expected)
    {
        PageViews.WithoutCitations(line).Should().Be(expected);
    }

    [Theory]
    [InlineData(1, "Згадується в 1 вірші Біблії.")]
    [InlineData(3, "Згадується в 3 віршах Біблії.")]
    [InlineData(11, "Згадується в 11 віршах Біблії.")]
    [InlineData(801, "Згадується в 801 вірші Біблії.")]
    public void CountsAreWordedByTheLanguagesPluralRules(int count, string expected)
    {
        Ukrainian.Counted("entity.verses", count).Should().Be(expected);
    }

    [Fact]
    public void RobotsKeepOutOfEveryLanguagesPrivatePagesAndPointToTheSitemap()
    {
        var robots = PageEndpoints.Robots(Origin);

        robots.Should().Contain("Disallow: /v1/me\n")
            .And.Contain("Disallow: /uk/account\n")
            .And.Contain("Disallow: /es/search?\n")
            .And.EndWith($"Sitemap: {Origin}/sitemap.xml\n");
        robots.Should().NotContain("Disallow: /v1/\n", "a search engine that runs the page's script reads its data from the API");
    }

    private static string Title(string html) =>
        System.Net.WebUtility.HtmlDecode(Regex.Match(html, "<title>(.*?)</title>").Groups[1].Value);

    private static string Meta(string html, string name) =>
        System.Net.WebUtility.HtmlDecode(Regex.Match(html, $"<meta name=\"{name}\" content=\"([^\"]*)\">").Groups[1].Value);
}
