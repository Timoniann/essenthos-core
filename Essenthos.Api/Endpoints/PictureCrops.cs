using System.Collections.Concurrent;
using System.Globalization;
using Essenthos.Core.Database;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Endpoints;

/// <summary>A square of a picture, in its pixels as a browser draws it: where a person's bust is.</summary>
internal readonly record struct PictureCrop(int Left, int Top, int Side)
{
    /// <summary>Smaller than this, a bust is too small a part of its picture to be worth cutting.</summary>
    private const int SmallestSide = 16;

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

    /// <summary>An address's <c>crop</c>, <c>left,top,side</c> in whole pixels; null for anything else.</summary>
    public static PictureCrop? Parse(string text)
    {
        var parts = text.Split(',');
        return parts.Length == 3
            && Number(parts[0]) is { } left && Number(parts[1]) is { } top && Number(parts[2]) is { } side
            && side >= SmallestSide
                ? new PictureCrop(left, top, side)
                : null;

        static int? Number(string part) =>
            part.Length is > 0 and <= 5 && part.All(char.IsAsciiDigit)
                ? int.Parse(part, CultureInfo.InvariantCulture)
                : null;
    }

    public override string ToString() => FormattableString.Invariant($"{Left},{Top},{Side}");
}

/// <summary>
/// The busts the corpus states for each picture, which are the only squares a picture is cut to. Read
/// once a picture is first asked for with a crop, and read again when a crop it does not hold is asked
/// for and the last reading is a minute old: a bust the owner has just moved is found, and an address
/// naming any other square costs at most one query a minute.
/// </summary>
internal sealed class PictureCrops(Func<string, CancellationToken, Task<IReadOnlySet<PictureCrop>>> read)
{
    private static readonly TimeSpan Fresh = TimeSpan.FromMinutes(1);

    private readonly ConcurrentDictionary<string, (DateTime Read, IReadOnlySet<PictureCrop> Crops)> _known =
        new(StringComparer.Ordinal);

    /// <summary>The busts of every row that shows the picture at <c>file</c>, from the corpus.</summary>
    public static PictureCrops FromCorpus(IServiceScopeFactory scopes) => new(async (file, cancellationToken) =>
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var busts = await db.EntityImages
            .Where(i => i.File == file && i.BustX != null && i.BustY != null && i.BustWidth != null)
            .Select(i => new { i.Width, i.Height, i.BustX, i.BustY, i.BustWidth })
            .ToListAsync(cancellationToken);
        return busts
            .Select(i => PictureCrop.Of(i.Width, i.Height, i.BustX!.Value, i.BustY!.Value, i.BustWidth!.Value))
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
