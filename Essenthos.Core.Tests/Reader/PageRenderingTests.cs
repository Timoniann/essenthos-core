using System.Text.RegularExpressions;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Endpoints;
using Essenthos.Core.Pages;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Reader;

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
        new(41, "Від Марка", 3, 3, text, "Біблія Огієнка")
        {
            Verses = [(2, "І вони стежили за Ним."), (3, text), (4, "А до них Він сказав.")],
            ChapterCount = 16,
            TextLanguage = "uk",
        };

    private static string VersePage(string text) =>
        PageHtml.Render(Shell, PageViews.Verse(SiteLanguage.Ukrainian, Ukrainian, Mark33(text)), SiteLanguage.Ukrainian, Origin);

    /// <summary>
    /// A section whose page opens on a longer lead than its description says the lead, as the reader's
    /// page does, and only its description where the wording has no lead.
    /// </summary>
    [Fact]
    public void ASectionSaysItsLeadWhereTheWordingHasOne()
    {
        const string Wording = """
            { "page": { "encyclopedia": { "title": "People", "description": "Short.", "lead": "Everyone the text names, at length." } } }
            """;
        var address = PageAddress.Parse("/encyclopedia");

        var led = PageViews.Section(SiteLanguage.English, PageWording.Parse("en", Wording), address);
        var plain = PageViews.Section(SiteLanguage.English, PageWording.Parse("en", Wording.Replace(", \"lead\": \"Everyone the text names, at length.\"", "")), address);

        led.Body.Should().Contain("<p>Everyone the text names, at length.</p>");
        led.Description.Should().Be("Short.");
        plain.Body.Should().Contain("<p>Short.</p>");
    }

    /// <summary>
    /// A Strong page sets out the lexicon's numbered senses a line each, as the reader's page does, so
    /// the text a reader sees first is the same height before the script runs as after it.
    /// </summary>
    [Fact]
    public void AStrongPageSetsOutItsSensesALineEach()
    {
        var page = PageViews.Strong(SiteLanguage.English, PageWording.Compiled("en"), new StrongView(
            "H430", "אֱלֹהִים", "ʼĕlôhîym", "gods in the ordinary sense", "plural of H433",
            "1) (plural)\n1a) rulers, judges\n2) (plural intensive-singular meaning)"));

        var html = PageHtml.Render(Shell, page, SiteLanguage.English, Origin);

        html.Should().Contain("<p>gods in the ordinary sense</p><p class=\"outline\">1) (plural)\n1a) rulers, judges\n2)")
            .And.Contain(".prerendered:not(.reading) .outline{white-space:pre-line}")
            .And.Contain("<p>plural of H433</p>");
    }

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
        html.Should().Contain("<p class=\"text-ltr\" lang=\"uk\">&lt;script&gt;alert(&quot;x&quot;)&lt;/script&gt; &amp; &#39;so&#39;</p>");
        Regex.Match(html, "<meta name=\"description\" content=\"([^\"]*)\">").Groups[1].Value
            .Should().Contain("alert(&quot;x&quot;)");
    }

    [Fact]
    public void TheVersesEssentialsGoInsideTheRootWhereTheApplicationMounts()
    {
        var html = VersePage("Стань посередині!");

        html.Should().MatchRegex("<div id=\"root\" class=\"h-full\"><article class=\"prerendered reading\" lang=\"uk\" data-chapter=\"/read/mark/3\" data-verse=\"3\">.*<h1>Від Марка 3</h1>.*</article></div>");
        html.Should().Contain("<a href=\"/uk/read/mark/3\">Від Марка 3</a>");
        html.Should().Contain("<a href=\"/uk/read/mark/2\" rel=\"prev\">← Від Марка 2</a><a href=\"/uk/read/mark/4\" rel=\"next\">Від Марка 4 →</a>");
    }

    /// <summary>
    /// A verse page shows the verse where the reader shows it: inside its chapter, in the reader's own
    /// grid, marked, and every verse linked to its own page. The plain rules for pages the script
    /// replaces leave that copy alone, since the reader's stylesheet draws it.
    /// </summary>
    [Fact]
    public void AVersePageShowsItsChapterAsTheReaderDoesWithTheVerseMarked()
    {
        var html = VersePage("Стань посередині!");

        html.Should().Contain(
            "<div class=\"verse-row\" id=\"v2\"><a href=\"/uk/read/mark/3/2\" class=\"verse-number\">2</a>" +
            "<div class=\"verse-cell\"><p class=\"text-ltr\" lang=\"uk\">І вони стежили за Ним.</p></div></div>" +
            "<div class=\"verse-row\" id=\"v3\" data-landed=\"\"><a href=\"/uk/read/mark/3/3\" class=\"verse-number\">3</a>");
        Regex.Matches(html, "data-landed").Should().HaveCount(1);
        html.Should().Contain("<div class=\"reading-pane\">Біблія Огієнка</div>");
        html.Should().NotMatchRegex(@"[}>,]\.prerendered[ {]", "every plain rule passes over the chapter's copy");
    }

    [Fact]
    public void AChapterPageShowsItsVersesWithNoneMarked()
    {
        var page = PageViews.Chapter(SiteLanguage.English, PageWording.Compiled("en"),
            new ChapterView(41, "Mark", 1, 16, [(1, "The beginning."), (2, "As it is written.")], "King James Version") { TextLanguage = "en" });

        page.Body.Should().StartWith("<article class=\"prerendered reading\" lang=\"en\" data-chapter=\"/read/mark/1\"><div class=\"reading-bar\"></div>")
            .And.Contain("<h1>Mark 1</h1>")
            .And.Contain("<p class=\"text-ltr\" lang=\"en\">As it is written.</p>")
            .And.NotContain("data-landed")
            .And.NotContain("rel=\"prev\"")
            .And.Contain("<a href=\"/read/mark/2\" rel=\"next\">Mark 2 →</a>");
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
    [InlineData("/strong/h430/verses", "Verses", "/strong/H430/verses")]
    [InlineData("/uk/people/david/verses", "Verses", "/people/david/verses")]
    [InlineData("/people/david/verses/more", "NotFound", "/people/david/verses/more")]
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

    [Theory]
    [InlineData("person", "jethro", "/people/jethro")]
    [InlineData("people", "levites", "/peoples/levites")]
    [InlineData("observance", "passover", "/observances/passover")]
    [InlineData("occupation", "shepherd", "/occupations/shepherd")]
    [InlineData("deity", "baal", null)]
    [InlineData("", "nothing", null)]
    public void ANameInARecordsLineLinksToItsPageOrToNothing(string kind, string slug, string? path)
    {
        PageAddress.TargetPath(kind, slug).Should().Be(path);
    }

    [Fact]
    public void ANameInARecordsLineWithNoPageIsSaidAsPlainWords()
    {
        var line = new LinePart[]
        {
            new("shepherd", "/occupations/shepherd"), new(", "), new("servant of "), new("someone", PageAddress.TargetPath("deity", "x")),
        };
        var page = PageViews.Record(SiteLanguage.Ukrainian, Ukrainian,
            new RecordView(EntityKind.Person, "moses", "Мойсей",
                "shepherd, servant of someone", line, 0, []));

        page.Body.Should().Contain("<p><a href=\"/uk/occupations/shepherd\">shepherd</a>, servant of someone</p>");
    }

    [Fact]
    public void OnlyTheFirstPageOfAListOfVersesAtTheUsualSizeIsIndexed()
    {
        PageAddress.Parse("/places/jerusalem/verses").Should().Match<PageAddress>(a =>
            a.Indexed && a.Skip == 0 && a.Take == PageAddress.VersesPerPage && a.RecordKind == EntityKind.Place);
        PageAddress.Parse("/places/jerusalem/verses", new Dictionary<string, string> { ["skip"] = "40" })
            .Should().Match<PageAddress>(a => !a.Indexed && a.Skip == 40);
        PageAddress.Parse("/places/jerusalem/verses", new Dictionary<string, string> { ["take"] = "100" })
            .Should().Match<PageAddress>(a => !a.Indexed && a.Take == 100);
        PageAddress.Parse("/places/jerusalem/verses", new Dictionary<string, string> { ["take"] = "7" })
            .Take.Should().Be(PageAddress.VersesPerPage, "a size the page does not offer is the usual one");
    }

    /// <summary>
    /// A page of every verse of a record lists its verses under their books, each book with how many
    /// of the record's verses it holds, every verse linked to its page and quoted, and the next page
    /// linked for a crawler to follow.
    /// </summary>
    [Fact]
    public void AListOfVersesGroupsThemByBookAndLinksTheNextPage()
    {
        var page = PageViews.Verses(SiteLanguage.Ukrainian, Ukrainian, new VersesView(
            "/people/david/verses", "/people/david", "Давид", PageSections.Encyclopedia, 1023, 0, 20,
            [new BookCount(8, "Рут", 2), new BookCount(9, "1 Самуїлова", 287)],
            [
                new ListedVerse(8, 4, 17, "Рут 4:17", "і породив Єссея, батька Давидового."),
                new ListedVerse(9, 16, 13, "1 Самуїлова 16:13", "і зійшов Дух Господній на Давида"),
            ],
            "Біблія Огієнка"));

        var html = PageHtml.Render(Shell, page, SiteLanguage.Ukrainian, Origin);

        Title(html).Should().Be("Давид: усі вірші · Essenthos");
        page.Body.Should().Contain("<h2>Рут (2)</h2><ul><li><a href=\"/uk/read/ruth/4/17\">Рут 4:17</a> і породив Єссея")
            .And.Contain("<h2>1 Самуїлова (287)</h2>")
            .And.Contain("<a href=\"/uk/people/david\">Давид</a>")
            .And.Contain("1023 вірші.")
            .And.Contain("<a href=\"/uk/people/david/verses?skip=20\" rel=\"next\">Наступні вірші</a>")
            .And.NotContain("rel=\"prev\"");
        html.Should().Contain("<link rel=\"canonical\" href=\"https://essenthos.org/uk/people/david/verses\"");
    }

    [Fact]
    public void ARecordsPageLinksToEveryVerseOfIt()
    {
        var page = PageViews.Record(SiteLanguage.English, PageWording.Compiled("en"),
            new RecordView(EntityKind.Person, "david", "David", "king of Israel", [], 1023, [new VerseMention(8, 4, 17, "Ruth 4:17")]));

        page.Body.Should().Contain("<a href=\"/people/david/verses\">All verses (1023)</a>");
    }

    [Fact]
    public void ARecordsPageSaysItsNameInTheOriginalWhatItIsKnownForAndQuotesItsFirstVerses()
    {
        var page = PageViews.Record(SiteLanguage.English, PageWording.Compiled("en"),
            new RecordView(EntityKind.Person, "moses", "Moses", "prophet", [], 795,
                [new VerseMention(2, 2, 10, "Exodus 2:10") { Text = "She named him Moses." }])
            {
                Names = [new OriginalName(null, "מֹשֶׁה", "mosheh", ["H4872"], "Μωϋσῆς", "Mōüsē͂s", ["G3475"], "draw out")],
                KnownFor = [new KnownClause("was called by God from a burning bush", "en")],
            });

        page.Body.Should().Contain(
            "<p><span lang=\"he\">מֹשֶׁה</span> mosheh (<a href=\"/strong/H4872\">H4872</a>); " +
            "<span lang=\"el\">Μωϋσῆς</span> Mōüsē͂s (<a href=\"/strong/G3475\">G3475</a>) — draw out</p>");
        page.Body.Should().Contain("<li lang=\"en\">was called by God from a burning bush</li>");
        page.Body.Should().Contain("<li><a href=\"/read/exodus/2/10\">Exodus 2:10</a> She named him Moses.</li>");
    }

    [Fact]
    public void TheAnswersAPageWasWrittenFromFollowTheRootAndCannotCloseTheirElement()
    {
        var answer = new KnownForResponse("</script><script>alert(1)</script> & ‘so’", "eng", []);
        var page = PageViews.Record(SiteLanguage.English, PageWording.Compiled("en"),
            new RecordView(EntityKind.Person, "moses", "Moses", "prophet", [], 0, [])) with
        {
            Data = PageData.Of([(PageData.EntityAddress("moses", SiteLanguage.Ukrainian), PageData.Json(answer))]),
        };

        var html = PageHtml.Render(Shell, page, SiteLanguage.English, Origin);

        html.Should().MatchRegex(
            """</article></div><script type="application/json" id="page-data">\{"/v1/entities/moses\?language=ukr\\u0026prose=ukr":.*</script>""");
        html.Should().NotContain("</script><script>alert");
        html.Should().Contain("‘so’");
    }

    [Fact]
    public void AStrongPageSaysHowOftenItStandsHowThePagesTextRendersItAndWhere()
    {
        var page = PageViews.Strong(SiteLanguage.Ukrainian, Ukrainian,
            new StrongView("G2316", "θεός", "theós", "бог", null)
            {
                Uses = 1311,
                CountedIn = "Nestle 1904",
                RenderedIn = "Біблія Огієнка",
                Renderings = [new StrongRenderingCount("Бог", 900), new StrongRenderingCount("Божий", 300)],
                Books = [new StrongBookCount("Від Матвія", 50), new StrongBookCount("Від Марка", 49)],
            });

        page.Body.Should().Contain("<p>Вживається в Біблії 1311 разів. Пораховано за Nestle 1904.</p>");
        page.Body.Should().Contain("<h2>Як це слово передає Біблія Огієнка</h2><ul><li>Бог — 900</li><li>Божий — 300</li></ul>");
        page.Body.Should().Contain("<h2>Де воно стоїть, книга за книгою</h2><p>Від Матвія 50; Від Марка 49</p>");
    }

    [Fact]
    public void AVersePageGivesTheVerseInTheOriginalWordByWordAfterTheChaptersCopy()
    {
        var verse = Mark33("Стань посередині!") with
        {
            Original = new OriginalVerseView("Hebrew Bible", "hbo", "בְּרֵאשִׁית בָּרָא",
                [new OriginalWordView("בְּרֵאשִׁית", ["H7225"], null), new OriginalWordView("בָּרָא", ["H1254"], null)]),
        };

        var html = PageHtml.Render(Shell, PageViews.Verse(SiteLanguage.Ukrainian, Ukrainian, verse), SiteLanguage.Ukrainian, Origin);

        html.Should().Contain(
            "</article><section class=\"prerendered\" lang=\"uk\"><h2>Від Марка 3:3 — в оригіналі, Hebrew Bible</h2>" +
            "<p lang=\"hbo\" dir=\"rtl\">בְּרֵאשִׁית בָּרָא</p><ol>" +
            "<li><span lang=\"hbo\" dir=\"rtl\">בְּרֵאשִׁית</span> <a href=\"/uk/strong/H7225\">H7225</a></li>");
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
