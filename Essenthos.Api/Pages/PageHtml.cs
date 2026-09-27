using System.Text;
using System.Text.RegularExpressions;

namespace Essenthos.Core.Pages;

/// <summary>
/// Everything the server says about one page before the reader's script runs: what a search engine
/// indexes it under, what a shared link previews, and the page's own words for anybody who never
/// runs the script at all.
/// </summary>
/// <param name="Heading">The page's name, which a link preview shows without the site's.</param>
/// <param name="Body">The page's essentials as markup, already escaped; the application replaces it as it mounts.</param>
internal sealed record PageContent(int Status, string Title, string Heading, string Description, string Body)
{
    public const string SiteName = "Essenthos";

    /// <summary>The page's address in English, from which every language's address is made; null on a page nobody should index.</summary>
    public string? Canonical { get; init; }

    public bool Indexed { get; init; } = true;

    /// <summary>The picture a link preview shows: the record's own where it has one, otherwise the site's.</summary>
    public string? Image { get; init; }

    /// <summary><c>article</c> for a page about one thing, <c>website</c> for the rest.</summary>
    public string Type { get; init; } = "website";

    public static string Titled(string heading) => $"{heading} · {SiteName}";
}

/// <summary>Markup written a piece at a time, with every piece of text escaped on the way in.</summary>
internal sealed class HtmlWriter
{
    private readonly StringBuilder _html = new();

    public HtmlWriter Open(string tag, string? className = null, string? id = null, string? lang = null)
    {
        _html.Append('<').Append(tag);
        Attribute("class", className);
        Attribute("id", id);
        Attribute("lang", lang);
        _html.Append('>');
        return this;
    }

    public HtmlWriter Close(string tag)
    {
        _html.Append("</").Append(tag).Append('>');
        return this;
    }

    public HtmlWriter Element(string tag, string text, string? className = null, string? lang = null) =>
        Open(tag, className, lang: lang).Text(text).Close(tag);

    public HtmlWriter Text(string text)
    {
        _html.Append(PageHtml.Escape(text));
        return this;
    }

    public HtmlWriter Link(string href, string text, string? rel = null)
    {
        _html.Append("<a href=\"").Append(PageHtml.Escape(href)).Append('"');
        Attribute("rel", rel);
        _html.Append('>').Append(PageHtml.Escape(text)).Append("</a>");
        return this;
    }

    public override string ToString() => _html.ToString();

    private void Attribute(string name, string? value)
    {
        if (value is not null)
        {
            _html.Append(' ').Append(name).Append("=\"").Append(PageHtml.Escape(value)).Append('"');
        }
    }
}

/// <summary>
/// The reader's own index.html with one page's head and essentials written into it.
///
/// The shell is the one the web container serves, so the scripts and styles it names are always the
/// ones that container has. What the shell says about the site in general — its title, description,
/// preview tags — is taken out and this page's put in its place, and the page's essentials go inside
/// <c>#root</c>, where the application's first render replaces them.
/// </summary>
internal static partial class PageHtml
{
    /// <summary>The site's own preview picture, which the reader's build serves from its root.</summary>
    public const string DefaultImage = "/og-image.png";

    private const int DefaultImageWidth = 1200;
    private const int DefaultImageHeight = 630;

    /// <summary>
    /// Plain type for the few seconds before the application mounts, and for a reader without
    /// scripts: the site's own colours come from its stylesheet, which the shell already loads.
    /// </summary>
    private const string Style =
        "<style>.prerendered{max-width:44rem;margin:0 auto;padding:2rem 1rem 3rem;font:17px/1.65 Spectral,Georgia,serif}" +
        ".prerendered h1{font-size:1.8rem;line-height:1.2;margin:0 0 .75rem}" +
        ".prerendered nav,.prerendered .source{font:14px/1.5 'IBM Plex Sans',system-ui,sans-serif;margin:1rem 0}" +
        ".prerendered p{margin:0 0 .75rem}.prerendered a{color:inherit}.prerendered ul{padding-left:1.2rem}" +
        ".prerendered blockquote{margin:1rem 0;font-size:1.25rem}.prerendered sup{margin-right:.25em;opacity:.6}</style>";

    public static string Escape(string text)
    {
        if (text.AsSpan().IndexOfAny("&<>\"'") < 0)
        {
            return text;
        }

        var escaped = new StringBuilder(text.Length + 16);
        foreach (var character in text)
        {
            escaped.Append(character switch
            {
                '&' => "&amp;",
                '<' => "&lt;",
                '>' => "&gt;",
                '"' => "&quot;",
                '\'' => "&#39;",
                _ => character.ToString(),
            });
        }

        return escaped.ToString();
    }

    /// <summary>A text with its runs of white space made single.</summary>
    public static string Plain(string text) => Spaces().Replace(text, " ").Trim();

    /// <summary>Every language's address for one page, and the one a reader of no listed language is sent to.</summary>
    public static IEnumerable<(string Language, string Url)> Alternates(string origin, string english) =>
        SiteLanguage.All
            .Select(language => (language.Code, origin + language.Path(english)))
            .Append(("x-default", origin + english));

    public static string Head(PageContent page, SiteLanguage language, string origin)
    {
        var head = new StringBuilder();
        head.Append("<title>").Append(Escape(page.Title)).Append("</title>\n");
        Meta(head, "name", "description", page.Description);
        if (!page.Indexed)
        {
            Meta(head, "name", "robots", "noindex, follow");
        }

        if (page.Canonical is { } canonical)
        {
            var url = origin + language.Path(canonical);
            head.Append("<link rel=\"canonical\" href=\"").Append(Escape(url)).Append("\">\n");
            foreach (var (code, href) in Alternates(origin, canonical))
            {
                head.Append("<link rel=\"alternate\" hreflang=\"").Append(code).Append("\" href=\"")
                    .Append(Escape(href)).Append("\">\n");
            }

            Meta(head, "property", "og:url", url);
        }

        Meta(head, "property", "og:type", page.Type);
        Meta(head, "property", "og:site_name", PageContent.SiteName);
        Meta(head, "property", "og:title", page.Heading);
        Meta(head, "property", "og:description", page.Description);
        Meta(head, "property", "og:locale", language.Locale);
        foreach (var other in SiteLanguage.All.Where(other => other != language))
        {
            Meta(head, "property", "og:locale:alternate", other.Locale);
        }

        var image = Absolute(origin, page.Image ?? DefaultImage);
        Meta(head, "property", "og:image", image);
        if (page.Image is null)
        {
            Meta(head, "property", "og:image:width", DefaultImageWidth.ToString());
            Meta(head, "property", "og:image:height", DefaultImageHeight.ToString());
        }

        Meta(head, "name", "twitter:card", "summary_large_image");
        Meta(head, "name", "twitter:title", page.Heading);
        Meta(head, "name", "twitter:description", page.Description);
        Meta(head, "name", "twitter:image", image);
        head.Append(Style).Append('\n');
        return head.ToString();
    }

    /// <summary>The shell with this page written into it.</summary>
    public static string Render(string shell, PageContent page, SiteLanguage language, string origin)
    {
        var html = Taken().Replace(shell, "");
        html = Language().Replace(html, $"$1{language.Code}$2", 1);
        var headEnd = html.IndexOf("</head>", StringComparison.OrdinalIgnoreCase);
        if (headEnd >= 0)
        {
            html = html.Insert(headEnd, Head(page, language, origin));
        }

        return Root().Replace(html, match => match.Groups[1].Value + page.Body + match.Groups[2].Value, 1);
    }

    private static string Absolute(string origin, string url) =>
        url.StartsWith("http://", StringComparison.Ordinal) || url.StartsWith("https://", StringComparison.Ordinal)
            ? url
            : origin + url;

    private static void Meta(StringBuilder head, string attribute, string name, string content) =>
        head.Append("<meta ").Append(attribute).Append("=\"").Append(name).Append("\" content=\"")
            .Append(Escape(content)).Append("\">\n");

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    /// <summary>What the shell says about the site in general, which a page says about itself instead.</summary>
    [GeneratedRegex(
        """[ \t]*(?:<title\b[^>]*>[\s\S]*?</title>|<meta\b[^>]*\b(?:name|property)\s*=\s*"(?:description|robots|og:[^"]*|twitter:[^"]*)"[^>]*>|<link\b[^>]*\brel\s*=\s*"(?:canonical|alternate)"[^>]*>)[ \t]*\r?\n?""",
        RegexOptions.IgnoreCase)]
    private static partial Regex Taken();

    [GeneratedRegex("""(<html\b[^>]*\blang\s*=\s*")[^"]*(")""", RegexOptions.IgnoreCase)]
    private static partial Regex Language();

    [GeneratedRegex("""(<div\b[^>]*\bid\s*=\s*"root"[^>]*>)\s*(</div>)""", RegexOptions.IgnoreCase)]
    private static partial Regex Root();
}
