using System.Text.Json;
using System.Text.RegularExpressions;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Endpoints;
using Essenthos.Core.Pages;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Reader;

/// <summary>
/// The JSON-LD each kind of page carries, checked as a validator would: every type is a schema.org
/// type, every property is one schema.org gives that type, every address is absolute, and what
/// Google asks of a breadcrumb trail, a dataset and a logo is there.
/// </summary>
public sealed class StructuredDataTests
{
    private const string Origin = "https://essenthos.org";

    private const string Shell =
        "<!doctype html><html lang=\"en\"><head><title>Essenthos</title></head><body><div id=\"root\"></div></body></html>";

    private static readonly string[] Thing =
        ["@id", "name", "description", "url", "image", "sameAs", "alternateName", "identifier", "mainEntityOfPage"];

    private static readonly string[] CreativeWork =
    [
        .. Thing, "text", "isPartOf", "hasPart", "inLanguage", "license", "position", "about", "mainEntity", "creator",
        "publisher", "citation", "dateModified", "isAccessibleForFree", "keywords",
    ];

    private static readonly string[] WebPage = [.. CreativeWork, "breadcrumb", "primaryImageOfPage"];

    private static readonly string[] MediaObject = [.. CreativeWork, "contentUrl", "encodingFormat", "width", "height"];

    /// <summary>
    /// The types the pages use, each with the properties schema.org defines on it or on a type it
    /// extends (schema.org/Thing, /CreativeWork, /WebPage, /MediaObject and the type's own page).
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string[]> Vocabulary = new Dictionary<string, string[]>
    {
        ["WebSite"] = CreativeWork,
        ["WebPage"] = WebPage,
        ["ItemPage"] = WebPage,
        ["CollectionPage"] = WebPage,
        ["AboutPage"] = WebPage,
        ["ContactPage"] = WebPage,
        ["Organization"] = [.. Thing, "logo"],
        ["ImageObject"] = MediaObject,
        ["DataDownload"] = MediaObject,
        ["BreadcrumbList"] = [.. Thing, "itemListElement"],
        ["ListItem"] = [.. Thing, "position", "item"],
        ["CreativeWork"] = CreativeWork,
        ["Chapter"] = CreativeWork,
        ["Book"] = CreativeWork,
        ["Dataset"] = [.. CreativeWork, "distribution"],
        ["Person"] = Thing,
        ["Place"] = [.. Thing, "geo"],
        ["Thing"] = Thing,
        ["GeoCoordinates"] = [.. Thing, "latitude", "longitude"],
        ["DefinedTerm"] = [.. Thing, "termCode", "inDefinedTermSet"],
        ["DefinedTermSet"] = [.. CreativeWork, "hasDefinedTerm"],
    };

    private static readonly HashSet<string> Addresses = ["@id", "url", "item", "contentUrl", "sameAs", "image"];

    private static readonly PageWording English = PageWording.Compiled("en");

    private static readonly PageWording Ukrainian = PageWording.Compiled("uk");

    public static TheoryData<string> Pages() => [.. Rendered.Keys];

    private static readonly IReadOnlyDictionary<string, (PageContent Page, SiteLanguage Language)> Rendered =
        new Dictionary<string, (PageContent, SiteLanguage)>
        {
            ["home"] = (PageViews.Home(SiteLanguage.Ukrainian, Ukrainian, [new BookLink(1, "Буття")]), SiteLanguage.Ukrainian),
            ["verse"] = (PageViews.Verse(SiteLanguage.Ukrainian, Ukrainian,
                new VerseView(41, "Від Марка", 3, 3, "Стань посередині! </script><b>", "Біблія Огієнка")), SiteLanguage.Ukrainian),
            ["chapter"] = (PageViews.Chapter(SiteLanguage.English, English,
                new ChapterView(41, "Mark", 3, 16, [(1, "He entered the synagogue."), (2, "They watched Him.")], "Berean Standard Bible")),
                SiteLanguage.English),
            ["person"] = (PageViews.Record(SiteLanguage.English, English,
                new RecordView(EntityKind.Person, "moses", "Moses", "the lawgiver (EXO 2:10)", [], 810, [new VerseMention(2, 2, 10, "Exodus 2:10")])
                {
                    Image = "/v1/images/people/moses.png",
                    SameAs = ["https://www.wikidata.org/wiki/Q9077", "https://en.wikipedia.org/wiki/Moses"],
                }), SiteLanguage.English),
            ["place"] = (PageViews.Record(SiteLanguage.English, English,
                new RecordView(EntityKind.Place, "bethel", "Bethel", null, [], 71, [])
                {
                    Coordinates = (31.93, 35.22),
                }), SiteLanguage.English),
            ["term"] = (PageViews.Record(SiteLanguage.English, English,
                new RecordView(EntityKind.Title, "messiah", "Messiah", "the anointed one", [], 2, [])), SiteLanguage.English),
            ["strong"] = (PageViews.Strong(SiteLanguage.English, English,
                new StrongView("H4872", "מֹשֶׁה", "Môsheh", "Moses, the legislator", null)), SiteLanguage.English),
            ["text"] = (PageViews.Named(SiteLanguage.English, English, "/texts/BSB", "Berean Standard Bible", "Read it online.", null,
                [("/read/genesis/1", "Genesis")], section: "texts",
                about: (link, _) => StructuredData.Node("Book", ("name", "Berean Standard Bible"), ("url", link("/texts/BSB")))),
                SiteLanguage.English),
            ["event"] = (PageViews.Named(SiteLanguage.English, English, "/events/exodus", "The Exodus", "It happened.", null, [],
                section: "events"), SiteLanguage.English),
            ["section"] = (PageViews.Section(SiteLanguage.English, English, PageAddress.Parse("/about")), SiteLanguage.English),
            ["downloads"] = (PageViews.Downloads(SiteLanguage.Ukrainian, Ukrainian, PageAddress.Parse("/uk/downloads"), Downloads),
                SiteLanguage.Ukrainian),
        };

    private static DownloadsResponse Downloads => new(
        new DateTimeOffset(2026, 10, 28, 12, 0, 0, TimeSpan.Zero),
        "abc",
        [
            new DownloadResponse("KJV", "King James Version", null, "eng", "translation", "Public Domain", null, null, null, 66, 31102,
                new DownloadFile("/downloads/texts/KJV/KJV.fad704a8bc0f.jsonl.gz", 1, "x"),
                new DownloadFile("/downloads/texts/KJV/ATTRIBUTION.0123456789ab.txt", 1, "y")),
            new DownloadResponse("BSB", "Berean Standard Bible", null, "eng", "translation", "CC0 1.0",
                "https://creativecommons.org/publicdomain/zero/1.0/", "Bible Hub", "The Holy Bible, Berean Standard Bible", 66, 31086,
                new DownloadFile("/downloads/texts/BSB/BSB.0123456789ab.jsonl.gz", 1, "z"),
                new DownloadFile(null, 1, "w")),
        ]);

    [Theory]
    [MemberData(nameof(Pages))]
    public void EveryTypeAndPropertyIsSchemaOrgsAndEveryAddressIsAbsolute(string name)
    {
        var (graph, _) = Described(name);

        foreach (var node in Walk(graph))
        {
            if (!node.TryGetProperty("@type", out var type))
            {
                node.EnumerateObject().Select(p => p.Name).Should().Equal(["@id"], $"an untyped node on {name} is a reference");
                continue;
            }

            Vocabulary.Should().ContainKey(type.GetString()!, $"{name} uses it");
            foreach (var property in node.EnumerateObject().Where(p => p.Name != "@type"))
            {
                Vocabulary[type.GetString()!].Should().Contain(property.Name, $"{type} on {name}");
                if (Addresses.Contains(property.Name))
                {
                    foreach (var value in Strings(property.Value))
                    {
                        value.Should().StartWith("https://", $"{property.Name} of {type} on {name}");
                    }
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(Pages))]
    public void EveryTrailStartsAtHomeAndEndsAtThePageItself(string name)
    {
        var (graph, page) = Described(name);
        var url = Nodes(graph, "ItemPage", "WebPage", "CollectionPage", "AboutPage").Single().GetProperty("url").GetString();
        var trails = Nodes(graph, "BreadcrumbList").ToList();
        if (page.IsHome)
        {
            trails.Should().BeEmpty("the home page is the first step of every trail, not a trail of its own");
            return;
        }

        var items = trails.Should().ContainSingle().Subject.GetProperty("itemListElement").EnumerateArray().ToList();
        items.Count.Should().BeGreaterThanOrEqualTo(2, "Google shows a trail of two steps or more");
        items.Select(item => item.GetProperty("position").GetInt32()).Should().Equal(Enumerable.Range(1, items.Count));
        items.Should().AllSatisfy(item => item.GetProperty("name").GetString().Should().NotBeNullOrWhiteSpace());
        items[0].GetProperty("item").GetString().Should().BeOneOf($"{Origin}/", $"{Origin}/uk/");
        items[^1].GetProperty("item").GetString().Should().Be(url);
    }

    [Fact]
    public void TheHomePageNamesTheSiteAndItsLogo()
    {
        var (graph, _) = Described("home");

        var site = Nodes(graph, "WebSite").Single();
        site.GetProperty("name").GetString().Should().Be("Essenthos");
        site.GetProperty("url").GetString().Should().Be($"{Origin}/");
        var logo = Nodes(graph, "Organization").Single().GetProperty("logo");
        logo.GetProperty("url").GetString().Should().Be($"{Origin}/icon-512.png");
        logo.GetProperty("width").GetInt32().Should().BeGreaterThanOrEqualTo(112);
        graph.ToString().Should().NotContain("SearchAction", "Google retired the sitelinks search box in 2024");
    }

    [Fact]
    public void EachDownloadableTextIsADatasetWithWhatDatasetSearchAsksFor()
    {
        var (graph, _) = Described("downloads");

        var datasets = Nodes(graph, "Dataset").ToList();
        datasets.Should().HaveCount(2);
        foreach (var dataset in datasets)
        {
            dataset.GetProperty("name").GetString().Should().NotBeNullOrWhiteSpace();
            dataset.GetProperty("description").GetString()!.Length.Should().BeInRange(50, 5000);
            dataset.GetProperty("license").ValueKind.Should().NotBe(JsonValueKind.Null);
            dataset.GetProperty("distribution").EnumerateArray().Should().AllSatisfy(file =>
                file.GetProperty("contentUrl").GetString().Should().StartWith($"{Origin}/downloads/", "a file is not under a language"));
        }

        datasets[0].GetProperty("description").GetString().Should().Contain("31102").And.Contain("King James Version");
        datasets[1].GetProperty("license").GetString().Should().Be("https://creativecommons.org/publicdomain/zero/1.0/");
        datasets[1].GetProperty("distribution").GetArrayLength().Should().Be(1, "a file with no address is not offered");
    }

    [Fact]
    public void APlaceIsWhereItsGazetteerPutsItAndAPersonIsTheSameAsItsWikidataItem()
    {
        var place = Nodes(Described("place").Graph, "ItemPage").Single().GetProperty("mainEntity");
        place.GetProperty("@type").GetString().Should().Be("Place");
        place.GetProperty("geo").GetProperty("latitude").GetDouble().Should().Be(31.93);

        var person = Nodes(Described("person").Graph, "ItemPage").Single().GetProperty("mainEntity");
        person.GetProperty("@type").GetString().Should().Be("Person");
        person.GetProperty("description").GetString().Should().Be("the lawgiver");
        person.GetProperty("image").GetString().Should().Be($"{Origin}/v1/images/people/moses.png");
        person.GetProperty("sameAs").EnumerateArray().Select(a => a.GetString()).Should().Contain("https://www.wikidata.org/wiki/Q9077");
    }

    [Fact]
    public void TextCannotCloseTheScriptItIsWrittenIn()
    {
        var html = PageHtml.Render(Shell, Rendered["verse"].Page, SiteLanguage.Ukrainian, Origin);
        var script = Regex.Match(html, "<script type=\"application/ld\\+json\" id=\"structured-data\">(.*?)</script>").Groups[1].Value;

        script.Should().NotContain("</").And.NotContain("<b>");
        script.Should().Contain("Від Марка", "Cyrillic is written as itself");
        JsonDocument.Parse(script).RootElement.GetProperty("@context").GetString().Should().Be("https://schema.org");
    }

    [Fact]
    public void APageNobodyIndexesDescribesNothingAndAnIndexedOneMayShowALargePreview()
    {
        var hidden = PageHtml.Render(Shell, PageViews.NotFound(SiteLanguage.English, English), SiteLanguage.English, Origin);
        var shown = PageHtml.Render(Shell, Rendered["chapter"].Page, SiteLanguage.English, Origin);

        hidden.Should().NotContain("application/ld+json");
        shown.Should().Contain("<meta name=\"robots\" content=\"max-image-preview:large\">");
        Regex.Matches(shown, "application/ld\\+json").Should().HaveCount(1);
    }

    [Theory]
    [InlineData("/read/MRK/3", null, "/read/mark/3")]
    [InlineData("/uk/read/mark/3", "v=3", "/uk/read/mark/3/3")]
    [InlineData("/strong/h430", null, "/strong/H430")]
    [InlineData("/uk", null, "/uk/")]
    [InlineData("/read/mark/3/", "c=KJV", "/read/mark/3?c=KJV")]
    [InlineData("/read/mark/3", "c=KJV", null)]
    [InlineData("/uk/", null, null)]
    [InlineData("/", null, null)]
    [InlineData("/search", "q=love", null)]
    public void AnotherSpellingOfAnAddressIsSentToItsOwn(string path, string? query, string? expected)
    {
        var parameters = query is null
            ? new Dictionary<string, string>()
            : query.Split('&').Select(pair => pair.Split('=')).ToDictionary(pair => pair[0], pair => pair[1]);

        PageEndpoints.Respelled(PageAddress.Parse(path, parameters), path, parameters).Should().Be(expected);
    }

    private static (JsonElement Graph, PageContent Page) Described(string name)
    {
        var (page, language) = Rendered[name];
        var html = PageHtml.Render(Shell, page, language, Origin);
        var script = Regex.Match(html, "<script type=\"application/ld\\+json\" id=\"structured-data\">(.*?)</script>").Groups[1].Value;
        script.Should().NotBeEmpty($"{name} is indexed");
        var root = JsonDocument.Parse(script).RootElement;
        root.GetProperty("@context").GetString().Should().Be(StructuredData.Context);
        return (root.GetProperty("@graph"), page);
    }

    private static IEnumerable<JsonElement> Nodes(JsonElement graph, params string[] types) =>
        graph.EnumerateArray().Where(node => types.Contains(node.GetProperty("@type").GetString()));

    private static IEnumerable<JsonElement> Walk(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var inner in element.EnumerateArray().SelectMany(Walk))
            {
                yield return inner;
            }
        }
        else if (element.ValueKind == JsonValueKind.Object)
        {
            yield return element;
            foreach (var inner in element.EnumerateObject().SelectMany(property => Walk(property.Value)))
            {
                yield return inner;
            }
        }
    }

    private static IEnumerable<string> Strings(JsonElement value) =>
        value.ValueKind switch
        {
            JsonValueKind.String => [value.GetString()!],
            JsonValueKind.Array => value.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String).Select(v => v.GetString()!),
            _ => [],
        };
}
