using System.Globalization;
using System.Text.Json.Nodes;
using Essenthos.Core.Endpoints;

namespace Essenthos.Core.Pages;

internal static partial class PageViews
{
    /// <summary>
    /// The downloads page: every text that can be taken as a file, linked, and each described as a
    /// dataset — what dataset search engines list, with its licence and where the file is.
    /// </summary>
    public static PageContent Downloads(SiteLanguage language, PageWording t, PageAddress address, DownloadsResponse downloads)
    {
        var page = Section(language, t, address);
        if (!page.Indexed || downloads.Texts.Count == 0)
        {
            return page;
        }

        var body = Open(language)
            .Open("nav").Link(language.Path("/"), PageContent.SiteName).Close("nav")
            .Element("h1", page.Heading)
            .Element("p", page.Description)
            .Open("ul");
        foreach (var text in downloads.Texts)
        {
            body.Open("li");
            if (text.File.Url is { } file)
            {
                body.Link(file, text.Name);
            }
            else
            {
                body.Text(text.Name);
            }

            if (!string.IsNullOrWhiteSpace(text.Licence))
            {
                body.Text(" — ").Text(text.Licence);
            }

            body.Close("li");
        }

        body.Close("ul").Close("article");
        return page with
        {
            Body = body.ToString(),
            Nodes = [.. downloads.Texts.Select(text => (Described)((link, origin) => Dataset(t, address, downloads, text, link, origin)))],
        };
    }

    private static JsonObject Dataset(
        PageWording t, PageAddress address, DownloadsResponse downloads, DownloadResponse text, Func<string, string> link, string origin)
    {
        JsonNode? licence = text.LicenceUrl is { Length: > 0 } licenceUrl
            ? licenceUrl
            : text.Licence is { Length: > 0 } named
                ? StructuredData.Node("CreativeWork", ("name", named))
                : null;
        var files = new JsonArray();
        foreach (var file in new[] { text.File, text.Attribution })
        {
            if (file.Url is { } url)
            {
                files.Add(StructuredData.Node("DataDownload",
                    ("contentUrl", StructuredData.Absolute(origin, url)),
                    ("encodingFormat", url.EndsWith(".gz", StringComparison.Ordinal) ? "application/gzip" : "text/plain")));
            }
        }

        return StructuredData.Node("Dataset",
            ("name", text.Name),
            ("alternateName", text.NameNative is { } native && native != text.Name ? native : null),
            ("description", t.Text("dataset.description",
                ("name", text.Name),
                ("verses", text.Verses.ToString(CultureInfo.InvariantCulture)),
                ("books", text.Books.ToString(CultureInfo.InvariantCulture)))),
            ("url", link(address.English)),
            ("identifier", text.Id),
            ("inLanguage", StructuredData.LanguageTag(text.Language)),
            ("license", licence),
            ("isAccessibleForFree", true),
            ("creator", text.RightsHolder is { Length: > 0 } holder ? StructuredData.Node("Organization", ("name", holder)) : null),
            ("citation", text.Citation),
            ("dateModified", downloads.GeneratedAt?.ToString("yyyy-MM-dd'T'HH:mm:ssK", CultureInfo.InvariantCulture)),
            ("distribution", files.Count > 0 ? files : null));
    }
}
