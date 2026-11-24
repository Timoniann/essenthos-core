using System.Collections.Concurrent;
using System.Globalization;
using Essenthos.Core.Database;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Endpoints;

/// <summary>
/// A part of a picture, in its pixels as a browser draws it: a square where a person's bust is, or the
/// band of a broad picture a tall frame shows.
/// </summary>
internal readonly record struct PictureCrop(int Left, int Top, int Width, int Height)
{
    /// <summary>Smaller than this, a bust is too small a part of its picture to be worth cutting.</summary>
    private const int SmallestSide = 16;

    /// <summary>
    /// The broadest frame, as width over height, that the band a broad picture is cut to still covers:
    /// the lead frame on a phone up to 432 pixels across (at most 9:10) and on a wide screen (3:4).
    /// </summary>
    public const double CoverAspect = 0.9;

    /// <summary>A square of a picture.</summary>
    public PictureCrop(int left, int top, int side) : this(left, top, side, side)
    {
    }

    /// <summary>
    /// The square a bust stated as fractions of the picture covers, kept inside the picture. Null where
    /// the picture's size is not known or the square is too small.
    /// </summary>
    public static PictureCrop? Of(int width, int height, double x, double y, double across)
    {
        if (width <= 0 || height <= 0)
        {
            return null;
        }

        var left = Math.Clamp((int)Math.Round(x * width), 0, width - 1);
        var top = Math.Clamp((int)Math.Round(y * height), 0, height - 1);
        var side = Math.Min((int)Math.Round(across * width), Math.Min(width - left, height - top));
        return side >= SmallestSide ? new PictureCrop(left, top, side) : null;
    }

    /// <summary>
    /// The band of a picture broader than <see cref="CoverAspect"/> that a frame that tall shows, its
    /// whole height, placed across as a browser places a picture covering a frame about its subject
    /// (<c>object-position</c> at <paramref name="focusX"/>): the part beside it is shown by no frame
    /// up to that broad. Null for a picture no broader, or of no known size.
    /// </summary>
    public static PictureCrop? Cover(int width, int height, double? focusX)
    {
        if (width <= 0 || height <= 0)
        {
            return null;
        }

        var across = (int)Math.Round(height * CoverAspect);
        if (across >= width)
        {
            return null;
        }

        var left = (int)Math.Round((width - across) * Math.Clamp(focusX ?? 0.5, 0, 1));
        return new PictureCrop(left, 0, across, height);
    }

    /// <summary>
    /// An address's <c>crop</c>, <c>left,top,side</c> for a square or <c>left,top,width,height</c>, in
    /// whole pixels; null for anything else.
    /// </summary>
    public static PictureCrop? Parse(string text)
    {
        var parts = text.Split(',');
        if (parts.Length is not (3 or 4) || parts.Select(Number).ToArray() is not [{ } left, { } top, { } width, .. var rest])
        {
            return null;
        }

        var height = rest is [{ } down] ? down : rest is [] ? width : -1;
        return width >= SmallestSide && height >= SmallestSide ? new PictureCrop(left, top, width, height) : null;

        static int? Number(string part) =>
            part.Length is > 0 and <= 5 && part.All(char.IsAsciiDigit)
                ? int.Parse(part, CultureInfo.InvariantCulture)
                : null;
    }

    /// <summary>Whether it is a square, written with one side.</summary>
    public bool IsSquare => Width == Height;

    public override string ToString() => IsSquare
        ? FormattableString.Invariant($"{Left},{Top},{Width}")
        : FormattableString.Invariant($"{Left},{Top},{Width},{Height}");
}

/// <summary>
/// The parts the corpus states for each picture, which are the only ones a picture is cut to: the
/// busts, and the band a tall frame shows of a broad picture about its focus. Read once a picture is
/// first asked for with a crop, and read again when a crop it does not hold is asked for and the last
/// reading is a minute old: a bust or a focus the owner has just moved is found, and an address naming
/// any other part costs at most one query a minute.
/// </summary>
internal sealed class PictureCrops(Func<string, CancellationToken, Task<IReadOnlySet<PictureCrop>>> read)
{
    private static readonly TimeSpan Fresh = TimeSpan.FromMinutes(1);

    private readonly ConcurrentDictionary<string, (DateTime Read, IReadOnlySet<PictureCrop> Crops)> _known =
        new(StringComparer.Ordinal);

    /// <summary>The busts and bands of every row that shows the picture at <c>file</c>, from the corpus.</summary>
    public static PictureCrops FromCorpus(IServiceScopeFactory scopes) => new(async (file, cancellationToken) =>
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rows = await db.EntityImages
            .Where(i => i.File == file)
            .Select(i => new { i.Width, i.Height, i.FocusX, i.BustX, i.BustY, i.BustWidth })
            .ToListAsync(cancellationToken);
        return rows
            .SelectMany(i => new[]
            {
                i is { BustX: { } x, BustY: { } y, BustWidth: { } across } ? PictureCrop.Of(i.Width, i.Height, x, y, across) : null,
                PictureCrop.Cover(i.Width, i.Height, i.FocusX),
            })
            .OfType<PictureCrop>()
            .ToHashSet();
    });

    /// <summary>Whether <paramref name="crop"/> is a bust the corpus states for the picture at <paramref name="file"/>.</summary>
    public async Task<bool> Allows(string file, PictureCrop crop, CancellationToken cancellationToken)
    {
        if (_known.TryGetValue(file, out var known)
            && (known.Crops.Contains(crop) || DateTime.UtcNow - known.Read < Fresh))
        {
            return known.Crops.Contains(crop);
        }

        var crops = await read(file, cancellationToken);
        _known[file] = (DateTime.UtcNow, crops);
        return crops.Contains(crop);
    }
}
