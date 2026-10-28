using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Unicode;

namespace Essenthos.Core.Pages;

/// <summary>One step of the trail from the home page to a page.</summary>
/// <param name="English">The step's English address, from which every language's is made.</param>
internal sealed record Crumb(string Name, string English);

/// <summary>
/// A node of a page's structured data, made once the page's origin and language are known: it is
/// handed the function that turns an English address into this page's absolute one, and the origin
/// for an address that is not a page.
/// </summary>
internal delegate JsonObject Described(Func<string, string> link, string origin);

/// <summary>
/// The page's schema.org description, as JSON-LD in its head: what search engines read for the
/// site's name and logo, the trail shown above a result, and what the page is about.
///
/// Every page that is indexed says what kind of page it is, which site it belongs to and its trail.
/// The home page also names the site and who publishes it. A page about one thing — a person, a
/// place, a verse, a lexicon entry — names that thing as its main entity, and the downloads page
/// describes each text that can be taken as a dataset. A page nobody should index says nothing.
/// </summary>
internal static class StructuredData
{
    public const string Context = "https://schema.org";

    /// <summary>The picture search engines show for the site: square, over 112 pixels, on its own address.</summary>
    public const string Logo = "/icon-512.png";

    private const int LogoSize = 512;

    private static readonly JsonSerializerOptions Writing = new()
    {
        // Every character that could close the script element or start markup is written as an
        // escape, and the rest of Unicode as itself, so Cyrillic stays readable and nothing breaks out.
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
    };

    public static string WebsiteId(string origin) => origin + "/#website";

    public static string OrganizationId(string origin) => origin + "/#organization";

    /// <summary>The page's structured data as one JSON-LD document, or null where the page is not to be indexed.</summary>
    public static string? Of(PageContent page, SiteLanguage language, string origin)
    {
        if (!page.Indexed || page.Canonical is not { } canonical)
        {
            return null;
        }

        string Link(string english) => origin + language.Path(english);
        var url = Link(canonical);
        var graph = new JsonArray();
        if (page.IsHome)
        {
            graph.Add(Website(origin));
            graph.Add(Organization(origin));
        }

        var webPage = new JsonObject
        {
            ["@type"] = page.PageType,
            ["@id"] = url,
            ["url"] = url,
            ["name"] = page.Heading,
            ["description"] = page.Description,
            ["inLanguage"] = language.Code,
            ["isPartOf"] = Reference(WebsiteId(origin)),
        };
        if (page.Trail.Count >= 2)
        {
            webPage["breadcrumb"] = Reference(url + "#breadcrumb");
        }

        if (page.Image is { } image)
        {
            webPage["primaryImageOfPage"] = new JsonObject { ["@type"] = "ImageObject", ["url"] = Absolute(origin, image) };
        }

        if (page.About is { } about)
        {
            var subject = about(Link, origin);
            if (page.Image is { } picture && subject["image"] is null)
            {
                subject["image"] = Absolute(origin, picture);
            }

            webPage["mainEntity"] = subject;
        }

        graph.Add(webPage);
        if (page.Trail.Count >= 2)
        {
            graph.Add(Breadcrumbs(url, page.Trail, Link));
        }

        foreach (var node in page.Nodes)
        {
            graph.Add(node(Link, origin));
        }

        return new JsonObject { ["@context"] = Context, ["@graph"] = graph }.ToJsonString(Writing);
    }

    /// <summary>The site, under the name search engines are asked to give it.</summary>
    public static JsonObject Website(string origin) => new()
    {
        ["@type"] = "WebSite",
        ["@id"] = WebsiteId(origin),
        ["url"] = origin + "/",
        ["name"] = PageContent.SiteName,
        ["inLanguage"] = new JsonArray([.. SiteLanguage.All.Select(language => (JsonNode)language.Code)]),
        ["publisher"] = Reference(OrganizationId(origin)),
    };

    public static JsonObject Organization(string origin) => new()
    {
        ["@type"] = "Organization",
        ["@id"] = OrganizationId(origin),
        ["name"] = PageContent.SiteName,
        ["url"] = origin + "/",
        ["logo"] = new JsonObject
        {
            ["@type"] = "ImageObject",
            ["url"] = origin + Logo,
            ["width"] = LogoSize,
            ["height"] = LogoSize,
        },
    };

    /// <summary>Every step names its page, the last one included, which is the page itself.</summary>
    public static JsonObject Breadcrumbs(string url, IReadOnlyList<Crumb> trail, Func<string, string> link) => new()
    {
        ["@type"] = "BreadcrumbList",
        ["@id"] = url + "#breadcrumb",
        ["itemListElement"] = new JsonArray(
        [
            .. trail.Select((crumb, index) => (JsonNode)new JsonObject
            {
                ["@type"] = "ListItem",
                ["position"] = index + 1,
                ["name"] = crumb.Name,
                ["item"] = link(crumb.English),
            }),
        ]),
    };

    public static JsonObject Reference(string id) => new() { ["@id"] = id };

    /// <summary>A node of the given type with the properties that have a value; the rest are left out.</summary>
    public static JsonObject Node(string type, params (string Name, JsonNode? Value)[] properties)
    {
        var node = new JsonObject { ["@type"] = type };
        foreach (var (name, value) in properties)
        {
            if (value is not null && !(value is JsonValue text && text.TryGetValue<string>(out var said) && string.IsNullOrWhiteSpace(said)))
            {
                node[name] = value;
            }
        }

        return node;
    }

    /// <summary>
    /// A text's language as a web page names one, from the corpus's ISO 639-3: the two-letter code
    /// where the language has one, the three letters where it has not (Koine Greek, Ge'ez), and nothing
    /// for a language not listed here rather than a code that is not a language tag.
    /// </summary>
    public static string? LanguageTag(string? corpus) => corpus switch
    {
        "eng" => "en",
        "ukr" => "uk",
        "deu" => "de",
        "spa" => "es",
        "rus" => "ru",
        "heb" => "he",
        "ell" => "el",
        "lat" => "la",
        "por" => "pt",
        "fra" => "fr",
        "ita" => "it",
        "zho" => "zh",
        "ara" => "ar",
        "hin" => "hi",
        "chu" => "cu",
        "grc" or "gez" or "arc" or "hbo" or "syc" or "cop" => corpus,
        _ => null,
    };

    public static string Absolute(string origin, string url) =>
        url.StartsWith("http://", StringComparison.Ordinal) || url.StartsWith("https://", StringComparison.Ordinal)
            ? url
            : origin + url;
}
