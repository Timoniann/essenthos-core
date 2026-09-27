using System.Diagnostics;
using System.Text;

namespace Essenthos.Core.Pages;

/// <summary>
/// The reader's pages as a search engine or a link preview sees them, and the sitemaps and robots
/// file that tell a crawler where they are.
///
/// None of this is reached at these addresses by a reader. The proxy in front sends every page
/// request of the site here — <c>/uk/read/mark/3</c> arrives as <c>/v1/pages/uk/read/mark/3</c> —
/// and <c>/robots.txt</c>, <c>/sitemap.xml</c> and <c>/sitemaps/…</c> under <c>/v1</c> the same way.
/// Where this answers with a server error, or cannot be reached, the proxy serves the plain
/// application instead, so a fault here costs the preview and never the page.
/// </summary>
internal static class PageEndpoints
{
    /// <summary>The address the site is served at, for every absolute link written; the request's own host where unset.</summary>
    public const string OriginKey = "Site:Origin";

    private const string Html = "text/html; charset=utf-8";
    private const string Xml = "application/xml; charset=utf-8";

    /// <summary>A reader's own pages, which no crawler has any business in.</summary>
    private static readonly string[] Private = ["/account", "/bookmarks", "/admin"];

    public static void MapPages(this IEndpointRouteBuilder routes)
    {
        string[] reading = [HttpMethods.Get, HttpMethods.Head];

        routes.MapMethods("/pages/{**path}", reading, async (
            string? path,
            HttpContext context,
            PageLoader loader,
            PageShell shell,
            PageCache cache,
            IConfiguration configuration,
            CancellationToken cancellationToken) =>
        {
            var started = Stopwatch.GetTimestamp();
            var request = context.Request;
            var query = request.Query.ToDictionary(pair => pair.Key, pair => pair.Value.ToString());
            var address = PageAddress.Parse("/" + path, query);
            if (address.RedirectTo is { } english)
            {
                return Results.Redirect(english + request.QueryString, permanent: true, preserveMethod: true);
            }

            if (await shell.Get(cancellationToken) is not { } reading)
            {
                return Results.Text(
                    $"The reader's index.html is not available, so pages are not rendered here. Set {PageShell.ConfigurationKey} " +
                    "to the web container's /index.html; until then the proxy serves the plain application.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            // Only what a search engine may index is kept: a reader's own pages and the lists a query
            // picked are as many as the queries people type, and each is cheap to write again.
            var wording = reading.WordingFor(address.Language);
            var page = address.Indexed
                ? await cache.Remember($"page:{address.Language.Code}:{address.Kind}:{address.English}", PageCache.Page,
                    () => loader.Load(address, wording, cancellationToken), made => made.Body.Length * sizeof(char))
                : await loader.Load(address, wording, cancellationToken);
            var html = PageHtml.Render(reading.Html, page, address.Language, Origin(context, configuration));

            context.Response.Headers.CacheControl = "no-cache";
            context.Response.Headers["Server-Timing"] =
                $"render;dur={Stopwatch.GetElapsedTime(started).TotalMilliseconds:0.0}";
            return Results.Content(html, Html, Encoding.UTF8, page.Status);
        });

        routes.MapMethods("/sitemap.xml", reading, async (
            HttpContext context,
            Sitemaps sitemaps,
            IConfiguration configuration,
            CancellationToken cancellationToken) =>
        {
            context.Response.ContentType = Xml;
            await sitemaps.WriteIndex(context.Response.Body, Origin(context, configuration), cancellationToken);
        });

        routes.MapMethods("/sitemaps/{file}", reading, async (
            string file,
            HttpContext context,
            Sitemaps sitemaps,
            IConfiguration configuration,
            CancellationToken cancellationToken) =>
        {
            if (await sitemaps.Addresses(file, cancellationToken) is not { } addresses)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            context.Response.ContentType = Xml;
            await Sitemaps.Write(context.Response.Body, Origin(context, configuration), addresses);
        });

        routes.MapMethods("/robots.txt", reading, (HttpContext context, IConfiguration configuration) =>
            Results.Text(Robots(Origin(context, configuration)), "text/plain; charset=utf-8"));
    }

    /// <summary>
    /// Everything public is open; a reader's own pages and a search somebody typed are not. The API
    /// itself stays open, because a search engine that runs the page's script fetches the page's
    /// data from it — all but <c>/v1/me</c>, which is somebody's account.
    /// </summary>
    public static string Robots(string origin)
    {
        var robots = new StringBuilder("User-agent: *\nAllow: /\nDisallow: /v1/me\n");
        foreach (var language in SiteLanguage.All)
        {
            foreach (var page in Private)
            {
                robots.Append("Disallow: ").Append(language.Prefix).Append(page).Append('\n');
            }

            robots.Append("Disallow: ").Append(language.Prefix).Append("/search?\n");
        }

        return robots.Append("\nSitemap: ").Append(origin).Append("/sitemap.xml\n").ToString();
    }

    private static string Origin(HttpContext context, IConfiguration configuration) =>
        configuration[OriginKey] is { Length: > 0 } configured
            ? configured.TrimEnd('/')
            : $"{context.Request.Scheme}://{context.Request.Host}";
}
