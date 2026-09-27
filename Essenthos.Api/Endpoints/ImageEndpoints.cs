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
///
/// <para>
/// A list draws a picture a few dozen pixels across, and a portrait is a quarter of a megabyte. So a
/// <c>w</c> asks for a copy at most that many pixels wide, made once beside the pictures by
/// <c>scripts/picture-sizes.py</c> and named by the digest of the picture it was made from; the
/// copy keeps the picture's proportions, so its focus and its bust hold on it as they do on the
/// original. Where no copy is there yet, or the picture is no wider than the copy would be, the
/// picture itself is served, and for an hour only, so the copy is taken up once it is made.
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

    /// <summary>The folder under the pictures holding their smaller copies, one folder per width.</summary>
    public const string SizedFolder = "sized";

    /// <summary>The widths a picture is copied at, narrowest first; a request between two gets the wider.</summary>
    public static readonly int[] SizedWidths = [128, 256, 512, 1024];

    private const string SizedExtension = ".webp";

    public const string Primary = "primary";

    public const string Generated = "generated";

    /// <summary>
    /// How a path is compared with the folder it must stay in: as the file system compares names. On
    /// Linux <c>../IMAGES</c> is another folder, and ignoring case there would let a path into it.
    /// </summary>
    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

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
        var root = Root(folder);

        routes.MapMethods("/images/{**file}", [HttpMethods.Get, HttpMethods.Head], (string file, string? v, int? w, HttpContext context) =>
        {
            var path = Path.GetFullPath(Path.Combine(root, file));
            if (!path.StartsWith(root, PathComparison)
                || !ContentTypes.TryGetValue(Path.GetExtension(path), out var contentType)
                || !File.Exists(path))
            {
                return Results.NotFound();
            }

            context.Response.Headers.XContentTypeOptions = "nosniff";
            if (w is { } width && Sized(folder, file, v, width) is { } copy)
            {
                context.Response.Headers.CacheControl = Immutable;
                return Results.File(copy, ContentTypes[SizedExtension]);
            }

            context.Response.Headers.CacheControl = v is not null && w is null ? Immutable : Revalidated;
            return Results.File(path, contentType, lastModified: File.GetLastWriteTimeUtc(path));
        });
    }

    private static string Root(string folder) => Path.TrimEndingDirectorySeparator(folder) + Path.DirectorySeparatorChar;

    /// <summary>The narrowest width a picture is copied at that is at least <paramref name="width"/>; null past the widest.</summary>
    public static int? SizedWidth(int width) => SizedWidths.Where(sized => sized >= width).Cast<int?>().FirstOrDefault();

    /// <summary>
    /// Where the copy of a picture at a width is kept, relative to the pictures' folder: under the
    /// width's own folder, at the picture's path with its digest before a <c>.webp</c> of its own.
    /// </summary>
    public static string SizedPath(string file, string digest, int width)
    {
        var extension = Path.GetExtension(file);
        return $"{SizedFolder}/{width}/{file[..^extension.Length]}.{digest}{SizedExtension}";
    }

    /// <summary>
    /// The copy of a picture at least <paramref name="width"/> pixels wide, made from the bytes the
    /// digest names, where there is one; null where it is not made, or the address carries no digest
    /// to know it by.
    /// </summary>
    public static string? Sized(string folder, string file, string? digest, int width)
    {
        var root = Root(folder);
        if (digest is not { Length: > 0 and <= 64 } || !digest.All(char.IsAsciiHexDigitLower)
            || SizedWidth(width) is not { } sized)
        {
            return null;
        }

        var path = Path.GetFullPath(Path.Combine(root, SizedPath(file, digest, sized)));
        return path.StartsWith(root, PathComparison) && File.Exists(path) ? path : null;
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
    /// <param name="generated">Whether our own generated pictures are shown, which the owner switches for the site.</param>
    /// <param name="language">
    /// The reader's language, whose caption is given where the list wrote one in it; the English
    /// caption otherwise.
    /// </param>
    public static async Task<List<EntityImageResponse>> Of(
        AppDbContext db,
        int entityId,
        CancellationToken cancellationToken,
        bool generated = true,
        string? language = null)
    {
        var images = await db.EntityImages
            .Where(i => i.EntityId == entityId && (generated || i.Kind != Generated))
            .OrderBy(i => i.Role == Primary ? 0 : 1)
            .ThenBy(i => i.Kind == Generated ? 0 : 1)
            .ThenBy(i => i.Ordinal)
            .Select(i => new
            {
                i.File, i.Digest, i.Kind, i.Role, i.Width, i.Height, i.Caption, i.Credit, i.CreditUrl,
                i.Licence, i.LicenceUrl, i.Source, i.FocusX, i.FocusY, i.BustX, i.BustY, i.BustWidth, i.BustHeight,
                Local = i.Captions.Where(c => c.Language == language).Select(c => c.Caption).FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);

        return
        [
            .. images.Select(i => i.Kind == Generated
                ? new EntityImageResponse(
                    Url(i.File, i.Digest), i.Kind, i.Role, i.Width, i.Height, null, null, null, null, null, null,
                    i.FocusX, i.FocusY)
                {
                    Bust = PictureBustResponse.Of(i.BustX, i.BustY, i.BustWidth, i.BustHeight),
                }
                : new EntityImageResponse(
                    Url(i.File, i.Digest), i.Kind, i.Role, i.Width, i.Height, i.Local ?? i.Caption, i.Credit,
                    i.CreditUrl, i.Licence, i.LicenceUrl, i.Source, i.FocusX, i.FocusY)
                {
                    CaptionLanguage = i.Local is null ? null : language,
                    Bust = PictureBustResponse.Of(i.BustX, i.BustY, i.BustWidth, i.BustHeight),
                }),
        ];
    }

    /// <summary>
    /// The picture each of several entities leads with, by slug, for a list or a tree to show small.
    /// Only the entities that have one are in it.
    /// </summary>
    public static async Task<Dictionary<string, EntityThumbnailResponse>> Leading(
        AppDbContext db, IReadOnlyCollection<string> slugs, CancellationToken cancellationToken, bool generated = true)
    {
        var primaries = await db.EntityImages
            .Where(i => i.Role == Primary && slugs.Contains(i.Entity!.Slug) && (generated || i.Kind != Generated))
            .Select(i => new
            {
                i.Entity!.Slug, i.File, i.Digest, i.Kind, i.FocusX, i.FocusY, i.BustX, i.BustY, i.BustWidth,
                i.BustHeight,
            })
            .ToListAsync(cancellationToken);

        return primaries
            .GroupBy(i => i.Slug)
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(i => i.Kind == Generated ? 0 : 1)
                    .Select(i => new EntityThumbnailResponse(Url(i.File, i.Digest), i.Kind, i.FocusX, i.FocusY)
                    {
                        Bust = PictureBustResponse.Of(i.BustX, i.BustY, i.BustWidth, i.BustHeight),
                    })
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
    double? FocusY)
{
    /// <summary>
    /// The language the caption is in where it is one written in the reader's; null for the English
    /// its source or its list states.
    /// </summary>
    public string? CaptionLanguage { get; init; }

    /// <summary>A person's head and shoulders, for any rendering smaller than the picture; null on a place or a thing.</summary>
    public PictureBustResponse? Bust { get; init; }
}

/// <summary>The picture an entity leads with, as small as a list needs it: where it is and what kind.</summary>
internal sealed record EntityThumbnailResponse(string Url, string Kind, double? FocusX, double? FocusY)
{
    /// <summary>A person's head and shoulders, which is what a thumbnail shows; null on a place or a thing.</summary>
    public PictureBustResponse? Bust { get; init; }
}

/// <summary>
/// The square of a picture that holds a person's head and shoulders, as fractions of its width and
/// height from the top left.
/// </summary>
internal sealed record PictureBustResponse(double X, double Y, double Width, double Height)
{
    public static PictureBustResponse? Of(double? x, double? y, double? width, double? height) =>
        x is { } left && y is { } top && width is { } across && height is { } down
            ? new PictureBustResponse(left, top, across, down)
            : null;
}
