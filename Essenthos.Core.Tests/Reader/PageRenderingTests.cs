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

    /// <summary>
    /// The reader's address of a verse shows the whole chapter, like thirty other addresses, so it names the
    /// verse's own page as the one to keep, in every language (PRB-1653).
    /// </summary>
    [Fact]
    public void TheReadersAddressOfAVerseNamesTheVersesOwnPageInEveryLanguage()
    {
        var html = VersePage("Стань посередині!");

        html.Should().Contain($"<link rel=\"canonical\" href=\"{Origin}/uk/verse/mark/3/3\">");
        Regex.Matches(html, "<link rel=\"alternate\" hreflang=\"([^\"]+)\" href=\"([^\"]+)\">")
            .Select(match => (match.Groups[1].Value, match.Groups[2].Value))
            .Should().Equal(
                ("en", $"{Origin}/verse/mark/3/3"),
                ("uk", $"{Origin}/uk/verse/mark/3/3"),
                ("de", $"{Origin}/de/verse/mark/3/3"),
                ("es", $"{Origin}/es/verse/mark/3/3"),
                ("x-default", $"{Origin}/verse/mark/3/3"));
    }

    /// <summary>
    /// A verse's own page leads with the verse alone, its names linked to their records, then the way to the
    /// chapter at the verse, the verses either side and the same verse in other texts; it is its own canonical.
    /// </summary>
    [Fact]
    public void AVersesOwnPageGivesTheVerseItsNamesTheChapterAndOtherTexts()
    {
        var verse = new SingleVerseView(43, "Від Івана", 3, 16, "Бо так полюбив Бог світ, що дав Сина", "Біблія Огієнка")
        {
            Words =
            [
                new PaneWord("Бо", " ", null, null, null),
                new PaneWord("так", " ", null, null, null),
                new PaneWord("полюбив", "  ", null, null, null),
                new PaneWord("Бог", " ", null, null, ("god", "term")),
                new PaneWord("світ", ", ", null, null, null),
                new PaneWord("що", " ", null, null, null),
                new PaneWord("дав", " ", null, null, null),
                new PaneWord("Сина", "\n", null, null, ("jesus", "person")),
            ],
            TextLanguage = "uk",
            Others = [new OtherVerse("Переклад Хоменка", "uk", "Бо так Бог полюбив світ"), new OtherVerse("Berean Standard Bible", "en", "For God so loved the world")],
            Previous = 15,
            Next = 17,
        };

        var page = PageViews.SingleVerse(SiteLanguage.Ukrainian, Ukrainian, verse);
        var html = PageHtml.Render(Shell, page, SiteLanguage.Ukrainian, Origin);

        html.Should().Contain($"<link rel=\"canonical\" href=\"{Origin}/uk/verse/john/3/16\">");
        page.Heading.Should().Be("Від Івана 3:16");
        page.Body.Should().StartWith("<article class=\"prerendered\" lang=\"uk\"><nav><a href=\"/uk/\">Essenthos</a> › <a href=\"/uk/read/john/3\">Від Івана 3</a></nav><h1>Від Івана 3:16</h1>")
            .And.Contain("<blockquote lang=\"uk\"><p>Бо так полюбив <a href=\"/uk/terms/god\" class=\"name\">Бог</a> світ, що дав <a href=\"/uk/people/jesus\" class=\"name\">Сина</a></p></blockquote>")
            .And.Contain("<p class=\"source\">Біблія Огієнка</p>")
            .And.Contain("<a href=\"/uk/read/john/3/16\" class=\"chapter-link\">Читати весь розділ: Від Івана 3</a>")
            .And.Contain("<a href=\"/uk/verse/john/3/15\" rel=\"prev\">← Від Івана 3:15</a> · <a href=\"/uk/verse/john/3/17\" rel=\"next\">Від Івана 3:17 →</a>")
            .And.Contain("<h2>Від Івана 3:16 в інших текстах</h2><h3>Переклад Хоменка</h3><p lang=\"uk\">Бо так Бог полюбив світ</p><h3>Berean Standard Bible</h3><p lang=\"en\">For God so loved the world</p>");
        Regex.Replace(Regex.Match(page.Body, "<blockquote[^>]*><p>(.*?)</p>").Groups[1].Value, "<[^>]+>", "")
            .Should().Be(verse.Text, "the linked words read exactly as the verse's plain text");
        Title(html).Should().StartWith("Від Івана 3:16 — «Бо так полюбив Бог світ");
        html.Should().Contain("\"@type\":\"BreadcrumbList\"").And.Contain($"{Origin}/uk/verse/john/3/16");
    }

    /// <summary>
    /// A chapter read from its stored pane is taken only where the pane says what the word table says: elided
    /// words left out, and never where one address holds two verses of the text.
    /// </summary>
    [Fact]
    public void APaneIsReadAsTheWordTableReadsItOrNotAtAll()
    {
        const string pane = """
            {"format":1,"text":"T","book":43,"chapter":3,"chapters":21,"records":[["jesus","person"]],"provenance":[],"sources":[],
             "verses":[{"n":16,"own":["3:16"],"words":[{"id":1,"s":"For","t":" "},{"id":2,"s":"God","t":"  ","n":"G2316","g":"God","e":[{"r":0}]},{"id":3,"s":"x","el":1},{"id":4,"s":"so."}]},
                       {"n":17,"notes":[{"kind":"footnote","content":"a"}],"words":[]}]}
            """;
        var text = PaneText.Parse(System.Text.Encoding.UTF8.GetBytes(pane));

        text.Single.Should().BeTrue();
        text.Plain.Should().Equal((16, "For God so."));
        text.At(16)!.Words.Select(word => word.Surface).Should().Equal("For", "God", "so.");
        text.At(16)!.Words[1].Should().Be(new PaneWord("God", "  ", "G2316", "God", ("jesus", "person")));
        PaneText.Parse(System.Text.Encoding.UTF8.GetBytes(pane.Replace("\"own\":[\"3:16\"]", "\"own\":[\"3:16\",\"3:16a\"]")))
            .Single.Should().BeFalse();
    }

    /// <summary>
    /// A page carries one validator for the state it is written from; a client holding it, by tag (weak, as a
    /// compressing proxy marks it, or strong) or by date, is told it has the page.
    /// </summary>
    [Fact]
    public void AClientHoldingThePagesValidatorIsToldItHasThePage()
    {
        var validity = new PageValidity("\"p123\"", new DateTimeOffset(2026, 11, 14, 12, 0, 0, TimeSpan.Zero));

        bool Held(string header, string value)
        {
            var context = new Microsoft.AspNetCore.Http.DefaultHttpContext();
            context.Request.Headers[header] = value;
            return validity.HeldBy(context.Request);
        }

        Held("If-None-Match", "\"p123\"").Should().BeTrue();
        Held("If-None-Match", "W/\"p123\"").Should().BeTrue();
        Held("If-None-Match", "\"other\", W/\"p123\"").Should().BeTrue();
        Held("If-None-Match", "\"p124\"").Should().BeFalse();
        Held("If-Modified-Since", "Sat, 14 Nov 2026 12:00:00 GMT").Should().BeTrue();
        Held("If-Modified-Since", "Sat, 14 Nov 2026 11:59:59 GMT").Should().BeFalse();
        Held("If-Range", "\"p123\"").Should().BeFalse();
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

        html.Should().MatchRegex("<div id=\"root\" class=\"h-full\"><main><article class=\"prerendered reading\" lang=\"uk\" data-chapter=\"/read/mark/3\" data-verse=\"3\">.*<h1>Від Марка 3</h1>.*</article></main></div>");
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
    public void AChapterInSeveralTextsShowsThemSideBySideAsTheReaderLaysThemOut()
    {
        var synodal = new ReadingColumn("Синодальный перевод", "ru", false, "text-ltr", new Dictionary<int, ReadingCell>
        {
            [1] = new("Блаженны непорочные в пути.", "118:1"),
        });
        var hebrew = new ReadingColumn("BHSA", "hbo", true, "text-hebrew", new Dictionary<int, ReadingCell>
        {
            [1] = new("אַשְׁרֵ֥י תְמִֽימֵי", null),
            [2] = new("אַ֭שְׁרֵי נֹצְרֵ֣י", null),
        });
        var page = PageViews.Chapter(SiteLanguage.English, PageWording.Compiled("en"),
            new ChapterView(19, "Psalms", 119, 150, [(1, "Блаженны непорочные в пути.")], "Синодальный перевод")
            {
                Columns = [synodal, hebrew],
            });

        page.Body.Should().Contain("<div class=\"reading-texts\" aria-hidden=\"true\"><span class=\"reading-text-on\">Together</span><span>Синодальный перевод</span><span>BHSA</span></div>")
            .And.Contain("<p class=\"reading-aligned\">Aligned to Синодальный перевод</p>")
            .And.Contain("<div class=\"verse-grid\" data-panes=\"2\" style=\"--panes:2\">")
            .And.Contain("<span class=\"reading-base\">base</span>")
            .And.Contain("<span class=\"reading-badge\">118:1</span><p class=\"text-ltr\" lang=\"ru\" dir=\"ltr\">Блаженны непорочные в пути.</p>")
            .And.Contain("<p class=\"text-hebrew\" lang=\"hbo\" dir=\"rtl\">אַ֭שְׁרֵי נֹצְרֵ֣י</p>")
            .And.Contain("<p class=\"reading-absent\">Not present in Синодальный перевод at verse 2</p>");
        Regex.Matches(page.Body, "class=\"verse-row\"").Should().HaveCount(2, "a row for every verse either text holds");
    }

    [Fact]
    public void AReadersAddressNamesItsTextsEachOnce()
    {
        PageAddress.Parse("/read/genesis/1", new Dictionary<string, string> { ["c"] = "KJV, bsb,,kjv,UBIO" }).Texts
            .Should().Equal("KJV", "bsb", "UBIO");
        PageAddress.Parse("/read/john/3/16", new Dictionary<string, string> { ["c"] = "KJV,BSB" }).Texts.Should().Equal("KJV", "BSB");
        PageAddress.Parse("/read/genesis/1").Texts.Should().BeEmpty();
    }

    [Fact]
    public void AColumnReadsEveryVerseOfItsPaneWithTheTextsOwnAddressWhereItDiffers()
    {
        var pane = """
            {"format":1,"verses":[
              {"n":1,"ref":[19,118,1],"words":[{"id":1,"s":"Блаженны","t":" "},{"id":2,"s":"непорочные","t":"."}]},
              {"n":2,"ref":[19,119,2],"stated":["119:2"],"words":[{"id":3,"s":"","el":1,"t":" "},{"id":4,"s":"Блаженны"}]},
              {"n":3,"ref":[19,118,3],"stated":["118:2-3"],"words":[]}]}
            """u8.ToArray();

        var cells = ReadingColumn.CellsOf(pane, 119);

        cells[1].Should().Be(new ReadingCell("Блаженны непорочные.", "118:1"));
        cells[2].Should().Be(new ReadingCell("Блаженны", null));
        cells[3].Should().Be(new ReadingCell("", "118:2-3"));
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
    [InlineData("/uk/verse/JHN/3/16", "SingleVerse", "/verse/john/3/16")]
    [InlineData("/verse/psalms/3/0", "Chapter", "/read/psalms/3")]
    [InlineData("/verse/john/3", "NotFound", "/verse/john/3")]
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
            """</article></main></div><script type="application/json" id="page-data">\{"/v1/entities/moses\?language=ukr\\u0026prose=ukr":.*</script>""");
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

    /// <summary>Brenton's Nehemiah once stood a second time as Ezra 11-23; those addresses lead to Nehemiah (PRB-1946).</summary>
    [Theory]
    [InlineData("/read/ezra/11", "/read/nehemiah/1")]
    [InlineData("/uk/read/ezra/17/1", "/uk/read/nehemiah/7/1")]
    [InlineData("/de/verse/ezra/23/31", "/de/verse/nehemiah/13/31")]
    [InlineData("/es/read/ezra/12", "/es/read/nehemiah/2")]
    public void EzrasChaptersPastTenLeadToNehemiah(string address, string held) =>
        PageAddress.Parse(address).RedirectTo.Should().Be(held);

    [Theory]
    [InlineData("/read/ezra/10")]
    [InlineData("/uk/verse/ezra/10/44")]
    [InlineData("/read/nehemiah/11")]
    public void EzrasOwnChaptersStay(string address) =>
        PageAddress.Parse(address).RedirectTo.Should().BeNull();

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

    [Fact]
    public void RobotsSignalSearchAndAnswerInputButNothingAboutTraining()
    {
        var robots = PageEndpoints.Robots(Origin);

        robots.Should().StartWith("User-agent: *\nContent-Signal: search=yes, ai-input=yes\n");
        robots.Should().NotContain("ai-train");
    }

    private static string Title(string html) =>
        System.Net.WebUtility.HtmlDecode(Regex.Match(html, "<title>(.*?)</title>").Groups[1].Value);

    private static string Meta(string html, string name) =>
        System.Net.WebUtility.HtmlDecode(Regex.Match(html, $"<meta name=\"{name}\" content=\"([^\"]*)\">").Groups[1].Value);
}
