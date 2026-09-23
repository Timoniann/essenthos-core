using Essenthos.Core.Database;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Endpoints;

/// <summary>
/// The pictures of people and places, served from a folder beside the corpus rather than out of it.
///
/// <para>
/// A picture's address is its path under that folder — the gazetteer's own file name, a curated
/// work's, a slug — which does not change when the corpus is rebuilt, and a <c>v</c> query carrying
/// the file's digest. The digest is what lets a reader's browser keep a picture for a year: a
/// replaced portrait arrives under a new address instead of behind a cached old one.
/// </para>
/// </summary>
internal static class ImageEndpoints
{
    public const string ConfigurationKey = "Images:Path";

    /// <summary>This checkout's own images folder, one level above the content root.</summary>
    private const string DevelopmentDefault = "../Resources/Images";

    private const string Route = "/v1/images/";

    /// <summary>A year, which a browser reads as for ever; the address changes when the picture does.</summary>
    private const string Immutable = "public, max-age=31536000, immutable";

    /// <summary>For an address without a digest, which nothing this API writes ever hands out.</summary>
    private const string Revalidated = "public, max-age=3600";

    public const string Primary = "primary";

    public const string Generated = "generated";

    private static readonly Dictionary<string, string> ContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".png"] = "image/png",
        [".webp"] = "image/webp",
    };

    /// <summary>
    /// The folder, resolved against the content root. Not required to exist: a server without the
    /// pictures still serves the corpus, and every picture answers 404.
    /// </summary>
    public static string Folder(IConfiguration configuration, string contentRootPath) =>
        Path.GetFullPath(Path.Combine(
            contentRootPath,
            configuration[ConfigurationKey] is { Length: > 0 } configured ? configured : DevelopmentDefault));

    public static void MapImages(this IEndpointRouteBuilder routes, string folder)
    {
        var root = Path.TrimEndingDirectorySeparator(folder) + Path.DirectorySeparatorChar;

        routes.MapMethods("/images/{**file}", [HttpMethods.Get, HttpMethods.Head], (string file, HttpContext context) =>
        {
            var path = Path.GetFullPath(Path.Combine(root, file));
            if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                || !ContentTypes.TryGetValue(Path.GetExtension(path), out var contentType)
                || !File.Exists(path))
            {
                return Results.NotFound();
            }

            context.Response.Headers.CacheControl = context.Request.Query.ContainsKey("v") ? Immutable : Revalidated;
            context.Response.Headers.XContentTypeOptions = "nosniff";
            return Results.File(path, contentType);
        });
    }

    /// <summary>The address a picture is served at, each segment of its path escaped.</summary>
    public static string Url(string file, string digest) =>
        Route + string.Join('/', file.Split('/').Select(Uri.EscapeDataString)) + "?v=" + digest;

    /// <summary>
    /// Every picture of one entity, the one a page leads with first: our own portrait before a
    /// public work, because the portrait is the one meant to be recognised at a glance.
    ///
    /// <para>
    /// Our own pictures go out bare. Their caption, credit, licence and collection are our record of
    /// how each was made and stay in the corpus; a reader is shown a picture, which plainly is not a
    /// photograph, and nothing about how it was drawn. Somebody else's work always carries its credit.
    /// </para>
    /// </summary>
    public static async Task<List<EntityImageResponse>> Of(AppDbContext db, int entityId, CancellationToken cancellationToken)
    {
        var images = await db.EntityImages
            .Where(i => i.EntityId == entityId)
            .OrderBy(i => i.Role == Primary ? 0 : 1)
            .ThenBy(i => i.Kind == Generated ? 0 : 1)
            .ThenBy(i => i.Ordinal)
            .Select(i => new
            {
                i.File, i.Digest, i.Kind, i.Role, i.Width, i.Height, i.Caption, i.Credit, i.CreditUrl,
                i.Licence, i.LicenceUrl, i.Source, i.FocusX, i.FocusY,
            })
            .ToListAsync(cancellationToken);

        return
        [
            .. images.Select(i => i.Kind == Generated
                ? new EntityImageResponse(
                    Url(i.File, i.Digest), i.Kind, i.Role, i.Width, i.Height, null, null, null, null, null, null,
                    i.FocusX, i.FocusY)
                : new EntityImageResponse(
                    Url(i.File, i.Digest), i.Kind, i.Role, i.Width, i.Height, i.Caption, i.Credit, i.CreditUrl,
                    i.Licence, i.LicenceUrl, i.Source, i.FocusX, i.FocusY)),
        ];
    }

    /// <summary>
    /// The picture each of several entities leads with, by slug, for a list or a tree to show small.
    /// Only the entities that have one are in it.
    /// </summary>
    public static async Task<Dictionary<string, EntityThumbnailResponse>> Leading(
        AppDbContext db, IReadOnlyCollection<string> slugs, CancellationToken cancellationToken)
    {
        var primaries = await db.EntityImages
            .Where(i => i.Role == Primary && slugs.Contains(i.Entity!.Slug))
            .Select(i => new { i.Entity!.Slug, i.File, i.Digest, i.Kind, i.FocusX, i.FocusY })
            .ToListAsync(cancellationToken);

        return primaries
            .GroupBy(i => i.Slug)
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(i => i.Kind == Generated ? 0 : 1)
                    .Select(i => new EntityThumbnailResponse(Url(i.File, i.Digest), i.Kind, i.FocusX, i.FocusY))
                    .First(),
                StringComparer.Ordinal);
    }
}

/// <summary>
/// One picture of a person or a place, with everything its credit line needs. Ours has no credit
/// line, and every field of one is null on it.
/// </summary>
/// <param name="Url">Where it is served, relative to the API's own address.</param>
/// <param name="Kind"><c>public</c> for an openly licensed work of somebody else's, <c>generated</c> for ours.</param>
/// <param name="Role"><c>primary</c> for the picture a page leads with, <c>gallery</c> for the rest.</param>
/// <param name="Caption">What it shows, fit to be its alternative text. Null where the source says nothing.</param>
/// <param name="Credit">Who made it, as its source asks to be credited. Null on ours.</param>
/// <param name="CreditUrl">The page it was published on.</param>
/// <param name="Licence">The licence's short name, such as <c>CC BY-SA 4.0</c>. Null on ours.</param>
/// <param name="Source">The collection it was taken from. Null on ours.</param>
/// <param name="FocusX">Where its subject is across its width, from 0 to 1; null for the middle.</param>
/// <param name="FocusY">Where its subject is down its height, from 0 to 1; null for the middle.</param>
internal sealed record EntityImageResponse(
    string Url,
    string Kind,
    string Role,
    int Width,
    int Height,
    string? Caption,
    string? Credit,
    string? CreditUrl,
    string? Licence,
    string? LicenceUrl,
    string? Source,
    double? FocusX,
    double? FocusY);

/// <summary>The picture an entity leads with, as small as a list needs it: where it is and what kind.</summary>
internal sealed record EntityThumbnailResponse(string Url, string Kind, double? FocusX, double? FocusY);
