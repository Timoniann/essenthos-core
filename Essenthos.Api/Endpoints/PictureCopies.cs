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
/// actual bytes, so no address anyone can write fills the disk with more than four copies a picture.
/// </para>
/// </summary>
internal sealed class PictureCopies
{
    /// <summary>The WebP quality the pre-warming script writes at, so a copy looks the same whoever made it.</summary>
    private const int Quality = 80;

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
    private readonly ConcurrentDictionary<string, Lazy<Task<bool>>> _making = new();

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
    public async Task<string?> Serve(string file, string path, string? digest, int width, CancellationToken cancellationToken)
    {
        if (digest is not { Length: DigestLength } || !digest.All(char.IsAsciiHexDigitLower))
        {
            return null;
        }

        var sized = ImageEndpoints.SizedWidth(width);
        string? target = null;
        if (sized is { } copyWidth)
        {
            if (ImageEndpoints.Sized(Folder, file, digest, copyWidth) is { } premade)
            {
                return premade;
            }

            target = Path.GetFullPath(Path.Combine(_cache, ImageEndpoints.CopyPath(file, digest, copyWidth)));
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

        if (sized is not { } made || target is null || picture.Width <= made)
        {
            return picture.Width > 0 ? path : null;
        }

        var making = _making.GetOrAdd(target, key => new Lazy<Task<bool>>(() => Make(path, digest, made, key)));
        try
        {
            return await making.Value.WaitAsync(cancellationToken) ? target : null;
        }
        finally
        {
            _making.TryRemove(KeyValuePair.Create(target, making));
        }
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
        var width = codec is null ? 0 : Upright(codec).Width;
        return _pictures[path] = new Picture(info.LastWriteTimeUtc, info.Length, Digest(bytes), width);
    }

    /// <summary>
    /// Makes one copy into the cache, writing it beside where it goes and moving it there whole, so a
    /// request never reads half a copy. The bytes are hashed again here, from the same read that is
    /// resized, so a picture replaced meanwhile is never kept under its old digest.
    /// </summary>
    private async Task<bool> Make(string path, string digest, int width, string target)
    {
        await _slots.WaitAsync();
        var partial = $"{target}.{Guid.NewGuid():N}.part";
        try
        {
            if (File.Exists(target))
            {
                return true;
            }

            var bytes = await File.ReadAllBytesAsync(path);
            if (Digest(bytes) != digest || Resize(bytes, width) is not { } copy)
            {
                return false;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await File.WriteAllBytesAsync(partial, copy);
            File.Move(partial, target, overwrite: true);
            return true;
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

            return false;
        }
        finally
        {
            _slots.Release();
        }
    }

    /// <summary>
    /// The picture turned as its camera recorded, halved while it is at least twice the width, and
    /// resampled the rest of the way: halving averages every pixel it drops, where one long step
    /// would skip most of them and shimmer.
    /// </summary>
    internal static byte[]? Resize(byte[] bytes, int width)
    {
        using var data = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(data);
        if (codec is null || SKBitmap.Decode(codec) is not { } decoded)
        {
            return null;
        }

        var current = Turn(decoded, codec.EncodedOrigin);
        if (!ReferenceEquals(current, decoded))
        {
            decoded.Dispose();
        }

        try
        {
            var height = Math.Max(1, (int)Math.Round((double)current.Height * width / current.Width, MidpointRounding.ToEven));
            while (current.Width >= width * 2)
            {
                var half = current.Resize(
                    current.Info.WithSize((current.Width + 1) / 2, (current.Height + 1) / 2),
                    new SKSamplingOptions(SKFilterMode.Linear));
                current.Dispose();
                current = half;
            }

            using var smaller = current.Resize(current.Info.WithSize(width, height), new SKSamplingOptions(SKCubicResampler.Mitchell));
            using var encoded = smaller.Encode(SKEncodedImageFormat.Webp, Quality);
            return encoded?.ToArray();
        }
        finally
        {
            current.Dispose();
        }
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
        canvas.DrawBitmap(bitmap, 0, 0);
        return upright;
    }

    private static string Digest(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes))[..DigestLength];

    /// <param name="Width">Its width as drawn, after its orientation; 0 where it cannot be decoded.</param>
    private sealed record Picture(DateTime Written, long Length, string Digest, int Width);
}
