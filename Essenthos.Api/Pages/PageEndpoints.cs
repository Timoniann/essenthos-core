using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Caching.Memory;

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
            PageValidator validator,
            IConfiguration configuration,
            CancellationToken cancellationToken) =>
        {
            var started = Stopwatch.GetTimestamp();
            var request = context.Request;
            var query = request.Query.ToDictionary(pair => pair.Key, pair => pair.Value.ToString());
            var address = PageAddress.Parse("/" + path, query);
            if (address.RedirectTo is { } english)
            {
                return Results.Redirect(PageAddress.Escaped(english) + request.QueryString, permanent: true, preserveMethod: true);
            }

            if (Respelled(address, "/" + path, query) is { } spelled)
            {
                return Results.Redirect(spelled, permanent: true, preserveMethod: true);
            }

            if (await shell.Get(cancellationToken) is not { } reading)
            {
                return Results.Text(
                    $"The reader's index.html is not available, so pages are not rendered here. Set {PageShell.ConfigurationKey} " +
                    "to the web container's /index.html; until then the proxy serves the plain application.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            // A page is a function of the release, the build and the shell, so a client that holds it as it
            // would be written now is told so before anything is read. Only an indexed page gives out a
            // validator, and only with a 200, so a client can hold one only for an address that exists.
            var validity = address.Indexed ? await validator.For(reading, cancellationToken) : null;
            context.Response.Headers.CacheControl = "no-cache";
            if (validity is not null && validity.HeldBy(request))
            {
                validity.WriteTo(context.Response);
                return Results.StatusCode(StatusCodes.Status304NotModified);
            }

            // Only what a search engine may index is kept: a reader's own pages and the lists a query
            // picked are as many as the queries people type, and each is cheap to write again.
            var wording = reading.WordingFor(address.Language);
            var page = address.Indexed
                ? await cache.Remember($"page:{address.Language.Code}:{address.Kind}:{address.English}", PageCache.Page,
                    () => loader.Load(address, wording, cancellationToken), made => made.Bytes, CacheItemPriority.Normal)
                : await loader.Load(address, wording, cancellationToken);
            if (page.MovedTo is { } moved)
            {
                return Results.Redirect(PageAddress.Escaped(address.Language.Path(moved)) + request.QueryString, permanent: true, preserveMethod: true);
            }

            var html = PageHtml.Render(reading.Html, page, address.Language, Origin(context, configuration));
            if (validity is not null && page.Status == StatusCodes.Status200OK && page.Indexed)
            {
                validity.WriteTo(context.Response);
            }

            context.Response.Headers["Server-Timing"] =
                $"render;dur={Stopwatch.GetElapsedTime(started).TotalMilliseconds:0.0}";
            return Results.Content(html, Html, Encoding.UTF8, page.Status);
        });

        // Each list is dated by when what it lists last changed, not by the release: no date is claimed for a
        // single address, since nothing records when one page's words changed, and a search engine trusts a
        // date only while every date it is given is true.
        routes.MapMethods("/sitemap.xml", reading, async (
            HttpContext context,
            Sitemaps sitemaps,
            SitemapDates dates,
            IConfiguration configuration,
            CancellationToken cancellationToken) =>
        {
            context.Response.ContentType = Xml;
            await sitemaps.WriteIndex(context.Response.Body, Origin(context, configuration), async name =>
                await sitemaps.Addresses(name + ".xml", cancellationToken) is { } addresses
                    ? await dates.ChangedAt(name, SitemapDates.Hash(addresses), cancellationToken)
                    : null, cancellationToken);
        });

        routes.MapMethods("/sitemaps/{file}", reading, async (
            string file,
            HttpContext context,
            Sitemaps sitemaps,
            SitemapDates dates,
            IConfiguration configuration,
            CancellationToken cancellationToken) =>
        {
            if (await sitemaps.Addresses(file, cancellationToken) is not { } addresses)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            // A list is the same list while its addresses are, so a crawler that holds it is told so.
            var hash = SitemapDates.Hash(addresses);
            var validity = new PageValidity($"\"s{hash}\"",
                await dates.ChangedAt(file[..^".xml".Length], hash, cancellationToken) ?? DateTimeOffset.MaxValue);
            context.Response.Headers.ETag = validity.ETag;
            if (validity.LastModified != DateTimeOffset.MaxValue)
            {
                validity.WriteTo(context.Response);
            }

            if (validity.HeldBy(context.Request))
            {
                context.Response.StatusCode = StatusCodes.Status304NotModified;
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
        var robots = new StringBuilder("User-agent: *\nAllow: /\nDisallow: /v1/me\nDisallow: /count\n");
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

    /// <summary>
    /// The page's one address where it was asked for by another spelling — <c>/read/MRK/3</c>,
    /// <c>/strong/h430</c>, <c>/read/mark/3?v=3</c>, <c>/uk</c> for <c>/uk/</c>, a trailing slash —
    /// with the rest of the query kept; null where it was asked for as it is spelled, or is a page
    /// nobody indexes. A permanent redirect rather than only a canonical link, so a search engine and
    /// whoever copies the address both end at the one spelling.
    /// </summary>
    public static string? Respelled(PageAddress address, string path, IReadOnlyDictionary<string, string> query)
    {
        if (!address.Indexed || address.Kind is PageKind.FamilyTree or PageKind.Search)
        {
            return null;
        }

        var spelled = address.Language.Path(address.English);
        if (spelled == path)
        {
            return null;
        }

        var kept = query.Where(pair => pair.Key != "v" || address.Kind != PageKind.Verse)
            .Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}")
            .ToList();
        var location = PageAddress.Escaped(spelled);
        return kept.Count == 0 ? location : $"{location}?{string.Join('&', kept)}";
    }

    private static string Origin(HttpContext context, IConfiguration configuration) =>
        configuration[OriginKey] is { Length: > 0 } configured
            ? configured.TrimEnd('/')
            : $"{context.Request.Scheme}://{context.Request.Host}";
}
