using System.Collections.Concurrent;
using System.Security.Cryptography;
using SkiaSharp;

namespace Essenthos.Core.Endpoints;

/// <summary>A place's picture drawn from the site's map, in one language: its path under the pictures' folder, its digest and size.</summary>
internal sealed record PlacePicture(string File, string Digest, int Width, int Height);

/// <summary>
/// The pictures of the places drawn from the site's own map, one per language the site speaks, which
/// lead every place's pictures.
///
/// <para>
/// They are files, not rows: <c>scripts/place-pictures/render.mjs</c> in essenthos-web draws them from
/// the corpus this API serves, so they can only be drawn once it is served, and stand beside it as the
/// small copies do. A picture is <c>places/&lt;language&gt;/&lt;slug&gt;.webp</c> under the pictures'
/// folder, the language spelled as the corpus spells one (<c>eng</c>, <c>ukr</c>, <c>deu</c>, <c>spa</c>);
/// a place with no file has no such picture, and a language with none is given the English one. A file
/// is read once, for its digest and size, and again when it is replaced.
/// </para>
///
/// <para>
/// The picture is ours, drawn from others' data, so it carries the map's credit: OpenStreetMap's
/// contributors under the ODbL wherever their coast or outlines are drawn, OpenBible.info's places and
/// outlines under CC BY 4.0, Natural Earth's coast and rivers and the Terrain Tiles' relief.
/// </para>
/// </summary>
internal sealed class PlacePictures(string folder)
{
    public const string Folder = "places";

    public const string Kind = "map";

    /// <summary>The language a place's picture is given in where the reader's has none.</summary>
    public const string Fallback = "eng";

    public const string Credit =
        "© OpenStreetMap contributors · OpenBible.info · Natural Earth · Terrain Tiles (SRTM, GMTED2010, ETOPO1)";

    public const string CreditUrl = "https://www.openstreetmap.org/copyright";

    public const string Licence = "ODbL 1.0 · CC BY 4.0";

    public const string LicenceUrl = "https://opendatacommons.org/licenses/odbl/1-0/";

    public const string Source = "Essenthos, drawn from its own map";

    private const int DigestLength = 12;

    private readonly string _root = ImageEndpoints.Root(folder);

    private readonly ConcurrentDictionary<string, (DateTime Written, long Length, PlacePicture? Picture)> _read = new();

    /// <summary>The picture of the place in <paramref name="language"/>, else in English; null where it has none.</summary>
    public PlacePicture? For(string slug, string? language)
    {
        if (slug.Length == 0 || slug.Contains('/') || slug.Contains('\\') || slug.Contains(".."))
        {
            return null;
        }

        foreach (var code in language is { Length: 3 } && language != Fallback ? [language, Fallback] : new[] { Fallback })
        {
            if (Read($"{Folder}/{code}/{slug}.webp") is { } picture)
            {
                return picture;
            }
        }

        return null;
    }

    private PlacePicture? Read(string file)
    {
        var path = Path.GetFullPath(Path.Combine(_root, file));
        if (!path.StartsWith(_root, ImageEndpoints.PathComparison))
        {
            return null;
        }

        var info = new FileInfo(path);
        if (!info.Exists)
        {
            return null;
        }

        if (_read.TryGetValue(path, out var known) && known.Written == info.LastWriteTimeUtc && known.Length == info.Length)
        {
            return known.Picture;
        }

        PlacePicture? picture = null;
        try
        {
            var bytes = File.ReadAllBytes(path);
            using var codec = SKCodec.Create(new SKMemoryStream(bytes));
            if (codec is { Info: { Width: > 0, Height: > 0 } size })
            {
                picture = new PlacePicture(
                    file, Convert.ToHexStringLower(SHA256.HashData(bytes))[..DigestLength], size.Width, size.Height);
            }
        }
        catch (IOException)
        {
            // Being replaced as it was read: the next request reads the new one.
            return null;
        }

        _read[path] = (info.LastWriteTimeUtc, info.Length, picture);
        return picture;
    }

    /// <summary>The picture as a page's list of pictures gives it, leading the list.</summary>
    public static EntityImageResponse Response(PlacePicture picture) =>
        new(
            ImageEndpoints.Url(picture.File, picture.Digest), Kind, ImageEndpoints.Primary, picture.Width, picture.Height,
            null, Credit, CreditUrl, Licence, LicenceUrl, Source, 0.5, 0.5);
}
