using System.Collections.Concurrent;
using System.Security.Cryptography;
using SkiaSharp;

namespace Essenthos.Core.Endpoints;

/// <summary>
/// The smaller copies of the pictures, made on the first request for one and kept.
///
/// <para>
/// A copy is looked for beside the pictures first, where <c>scripts/picture-sizes.py</c> may have
/// made it ahead of time, then in the cache folder, and made into the cache only when neither has it.
/// What can be made is bounded: only at <see cref="ImageEndpoints.SizedWidths"/>, only from a picture
/// under the pictures' folder that is not itself a copy, and only under the digest of that picture's
/// actual bytes, so no address anyone can write fills the disk with more than one copy a picture at each
/// of those widths.
/// </para>
/// </summary>
internal sealed class PictureCopies
{
    /// <summary>
    /// The WebP quality a copy this wide is written at, as the pre-warming script writes it, so a copy
    /// looks the same whoever made it. At 80 a face a few dozen pixels across loses its eyes to the
    /// encoder's blocks, so the small copies take 85, a fifth more bytes; a wide copy is drawn shrunk
    /// and keeps 80.
    /// </summary>
    internal static int Quality(int width) => width <= 256 ? 85 : 80;

    private const int DigestLength = 12;

    private readonly string _root;

    private readonly string _cache;

    private readonly ILogger<PictureCopies> _logger;

    /// <summary>
    /// Every picture whose digest and width have been read, with the write time and length they were
    /// read at; bounded by the pictures in the folder, and read again when a picture is replaced.
    /// </summary>
    private readonly ConcurrentDictionary<string, Picture> _pictures = new();

    /// <summary>The copies being made now, so that a page asking for one twenty times makes it once.</summary>
    private readonly ConcurrentDictionary<string, Lazy<Task<Making>>> _making = new();

    /// <summary>
    /// The WebPs at a picture's own width that came out no smaller than the picture, which is served
    /// instead; bounded by the pictures, and keyed by digest, so a replaced picture is tried again.
    /// </summary>
    private readonly ConcurrentDictionary<string, bool> _noSmaller = new();

    /// <summary>Resizing is all processor; a cold page of fifty avatars queues here instead of taking every core.</summary>
    private readonly SemaphoreSlim _slots = new(Math.Max(1, Environment.ProcessorCount / 2));

    public PictureCopies(string folder, string cache, ILogger<PictureCopies> logger)
    {
        Folder = folder;
        _root = ImageEndpoints.Root(folder);
        _cache = ImageEndpoints.Root(cache);
        _logger = logger;
    }

    /// <summary>The pictures' folder.</summary>
    public string Folder { get; }

    /// <summary>
    /// The file to serve for a picture drawn at most <paramref name="width"/> pixels wide: a copy
    /// already made, one made now, or the picture itself where it is no wider than the copy would be.
    /// Null where the digest is not the picture's, or the copy cannot be made; the caller serves the
    /// picture itself, as for an address that asks for no width.
    /// </summary>
    /// <param name="file">The picture's path under the pictures' folder, as it was asked for.</param>
    /// <param name="path">The same, resolved, and already known to be a picture inside the folder.</param>
    /// <param name="crop">
    /// The part to cut it to first, already known to be one the corpus states; the copy is then that
    /// part, no wider than the part itself.
    /// </param>
    public async Task<string?> Serve(
        string file, string path, string? digest, int width, PictureCrop? crop, CancellationToken cancellationToken)
    {
        if (digest is not { Length: DigestLength } || !digest.All(char.IsAsciiHexDigitLower))
        {
            return null;
        }

        var sized = ImageEndpoints.SizedWidth(width);
        string? target = null;
        if (sized is { } copyWidth)
        {
            if (ImageEndpoints.Sized(Folder, file, digest, copyWidth, crop) is { } premade)
            {
                return premade;
            }

            target = Path.GetFullPath(Path.Combine(_cache, ImageEndpoints.CopyPath(file, digest, copyWidth, crop)));
            if (!target.StartsWith(_cache, ImageEndpoints.PathComparison))
            {
                return null;
            }

            if (File.Exists(target))
            {
                return target;
            }
        }

        if (IsCopy(path) || await Read(path, cancellationToken) is not { } picture || picture.Digest != digest)
        {
            return null;
        }

        if (crop is { } part)
        {
            if (sized is null || target is null
                || part.Left + part.Width > picture.Width || part.Top + part.Height > picture.Height)
            {
                return null;
            }
        }
        else if (sized is not { } whole || target is null || picture.Width <= whole)
        {
            if (picture.Width <= 0)
            {
                return null;
            }

            return Reencoded(path) ? await Own(file, path, digest, picture.Width, cancellationToken) : path;
        }

        return await Made(path, digest, sized.Value, crop, false, target, cancellationToken) == Making.Made ? target : null;
    }

    /// <summary>
    /// A photograph kept as JPEG or PNG, which a WebP of the same pixels holds in fewer bytes: one
    /// asked for at its own width or wider is served that WebP instead of itself.
    /// </summary>
    private static bool Reencoded(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".jpg" or ".jpeg" or ".png";

    /// <summary>
    /// The WebP of a JPEG or PNG picture at its own width, kept with its copies under that width; the
    /// picture itself where the WebP would be no smaller, null where it cannot be kept.
    /// </summary>
    private async Task<string?> Own(string file, string path, string digest, int width, CancellationToken cancellationToken)
    {
        var copy = ImageEndpoints.CopyPath(file, digest, width);
        var premade = Path.GetFullPath(Path.Combine(_root, ImageEndpoints.SizedFolder, copy));
        if (premade.StartsWith(_root, ImageEndpoints.PathComparison) && File.Exists(premade))
        {
            return premade;
        }

        var target = Path.GetFullPath(Path.Combine(_cache, copy));
        if (!target.StartsWith(_cache, ImageEndpoints.PathComparison))
        {
            return null;
        }

        if (File.Exists(target))
        {
            return target;
        }

        return await Made(path, digest, width, null, true, target, cancellationToken) switch
        {
            Making.Made => target,
            Making.NoSmaller => path,
            _ => null,
        };
    }

    private async Task<Making> Made(
        string path, string digest, int width, PictureCrop? crop, bool own, string target, CancellationToken cancellationToken)
    {
        if (_noSmaller.ContainsKey(target))
        {
            return Making.NoSmaller;
        }

        var making = _making.GetOrAdd(target, key => new Lazy<Task<Making>>(() => Make(path, digest, width, crop, own, key)));
        try
        {
            return await making.Value.WaitAsync(cancellationToken);
        }
        finally
        {
            _making.TryRemove(KeyValuePair.Create(target, making));
        }
    }

    private enum Making
    {
        Made,
        NoSmaller,
        Failed,
    }

    /// <summary>Whether a path is one of the copies, which are never copied again.</summary>
    private bool IsCopy(string path) =>
        path.StartsWith(_cache, ImageEndpoints.PathComparison)
        || path.StartsWith(_root + ImageEndpoints.SizedFolder + Path.DirectorySeparatorChar, ImageEndpoints.PathComparison);

    private async Task<Picture?> Read(string path, CancellationToken cancellationToken)
    {
        var info = new FileInfo(path);
        if (_pictures.TryGetValue(path, out var known) && known.Written == info.LastWriteTimeUtc && known.Length == info.Length)
        {
            return known;
        }

        byte[] bytes;
        try
        {
            bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        }
        catch (IOException)
        {
            return null;
        }

        using var data = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(data);
        var size = codec is null ? default : Upright(codec);
        return _pictures[path] = new Picture(info.LastWriteTimeUtc, info.Length, Digest(bytes), size.Width, size.Height);
    }

    /// <summary>
    /// Makes one copy into the cache, writing it beside where it goes and moving it there whole, so a
    /// request never reads half a copy. The bytes are hashed again here, from the same read that is
    /// resized, so a picture replaced meanwhile is never kept under its old digest.
    /// </summary>
    /// <param name="own">
    /// Whether it is the picture at its own width, which is not kept where it comes out no smaller.
    /// </param>
    private async Task<Making> Make(string path, string digest, int width, PictureCrop? crop, bool own, string target)
    {
        await _slots.WaitAsync();
        var partial = $"{target}.{Guid.NewGuid():N}.part";
        try
        {
            if (File.Exists(target))
            {
                return Making.Made;
            }

            var bytes = await File.ReadAllBytesAsync(path);
            if (Digest(bytes) != digest || Resize(bytes, width, crop) is not { } copy)
            {
                return Making.Failed;
            }

            if (own && copy.Length >= bytes.Length)
            {
                _noSmaller[target] = true;
                return Making.NoSmaller;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await File.WriteAllBytesAsync(partial, copy);
            File.Move(partial, target, overwrite: true);
            return Making.Made;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(
                exception,
                "No {Width} px copy of {Picture} could be kept in {Cache}; the picture itself is served. Point Images:CachePath at a folder the API can write",
                width, path, _cache);
            if (File.Exists(partial))
            {
                File.Delete(partial);
            }

            return Making.Failed;
        }
        finally
        {
            _slots.Release();
        }
    }

    /// <summary>
    /// The picture turned as its camera recorded, cut to <paramref name="crop"/> if one is given, then
    /// made smaller by <see cref="PictureResampler"/>; a part narrower than the width, and a picture
    /// asked for at its own width, are kept at their own size.
    /// </summary>
    internal static byte[]? Resize(byte[] bytes, int width, PictureCrop? crop = null)
    {
        using var data = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(data);
        if (codec is null || SKBitmap.Decode(codec) is not { } decoded)
        {
            return null;
        }

        var upright = Turn(decoded, codec.EncodedOrigin);
        if (!ReferenceEquals(upright, decoded))
        {
            decoded.Dispose();
        }

        using (upright)
        {
            if (crop is not { } part)
            {
                return Encoded(upright, Math.Min(width, upright.Width));
            }

            using var subset = new SKBitmap();
            if (!upright.ExtractSubset(subset, SKRectI.Create(part.Left, part.Top, part.Width, part.Height))
                || subset.Copy() is not { } cut)
            {
                return null;
            }

            using (cut)
            {
                return Encoded(cut, Math.Min(width, part.Width));
            }
        }
    }

    /// <summary>A picture made <paramref name="width"/> wide, keeping its proportions, as a WebP.</summary>
    private static byte[]? Encoded(SKBitmap picture, int width)
    {
        var height = Math.Max(1, (int)Math.Round((double)picture.Height * width / picture.Width, MidpointRounding.ToEven));
        using var smaller = width == picture.Width ? null : PictureResampler.Resize(picture, width, height);
        using var encoded = (smaller ?? picture).Encode(SKEncodedImageFormat.Webp, Quality(width));
        return encoded?.ToArray();
    }

    private static SKSizeI Upright(SKCodec codec) =>
        codec.EncodedOrigin >= SKEncodedOrigin.LeftTop
            ? new SKSizeI(codec.Info.Height, codec.Info.Width)
            : new SKSizeI(codec.Info.Width, codec.Info.Height);

    /// <summary>The picture as a browser draws it, which honours the orientation its camera wrote.</summary>
    private static SKBitmap Turn(SKBitmap bitmap, SKEncodedOrigin origin)
    {
        if (origin == SKEncodedOrigin.TopLeft)
        {
            return bitmap;
        }

        float w = bitmap.Width, h = bitmap.Height;
        var matrix = origin switch
        {
            SKEncodedOrigin.TopRight => new SKMatrix(-1, 0, w, 0, 1, 0, 0, 0, 1),
            SKEncodedOrigin.BottomRight => new SKMatrix(-1, 0, w, 0, -1, h, 0, 0, 1),
            SKEncodedOrigin.BottomLeft => new SKMatrix(1, 0, 0, 0, -1, h, 0, 0, 1),
            SKEncodedOrigin.LeftTop => new SKMatrix(0, 1, 0, 1, 0, 0, 0, 0, 1),
            SKEncodedOrigin.RightTop => new SKMatrix(0, -1, h, 1, 0, 0, 0, 0, 1),
            SKEncodedOrigin.RightBottom => new SKMatrix(0, -1, h, -1, 0, w, 0, 0, 1),
            _ => new SKMatrix(0, 1, 0, -1, 0, w, 0, 0, 1),
        };

        var turned = origin >= SKEncodedOrigin.LeftTop
            ? bitmap.Info.WithSize(bitmap.Height, bitmap.Width)
            : bitmap.Info;
        var upright = new SKBitmap(turned);
        using var canvas = new SKCanvas(upright);
        canvas.SetMatrix(matrix);
        // A quarter turn or a mirror lands every pixel on a pixel, so the nearest one is exact.
        canvas.DrawBitmap(bitmap, 0, 0, new SKSamplingOptions(SKFilterMode.Nearest));
        return upright;
    }

    private static string Digest(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes))[..DigestLength];

    /// <param name="Width">Its width as drawn, after its orientation; 0 where it cannot be decoded.</param>
    /// <param name="Height">Its height as drawn.</param>
    private sealed record Picture(DateTime Written, long Length, string Digest, int Width, int Height);
}
