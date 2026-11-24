using System.Runtime.InteropServices;
using SkiaSharp;

namespace Essenthos.Core.Endpoints;

/// <summary>
/// Makes a picture smaller with a Lanczos filter of three lobes, widened by how much smaller it is
/// made, so every source pixel under an output pixel counts and an edge stays an edge: what Pillow's
/// <c>LANCZOS</c> does, and what the pre-warming script uses, so a copy looks the same whoever made it.
///
/// <para>
/// Skia's own samplers are made for drawing, not for shrinking: a cubic one reads a fixed four by
/// four pixels however far it shrinks, and a halving pyramid finished by a Mitchell pass blurs even
/// where the last step is one to one. Both leave a copy visibly soft.
/// </para>
/// </summary>
internal static class PictureResampler
{
    private const double Lobes = 3;

    /// <summary>
    /// The picture at <paramref name="width"/> by <paramref name="height"/>, in its own colour type and
    /// alpha type. It works on premultiplied pixels, so a transparent edge does not darken.
    /// </summary>
    public static SKBitmap Resize(SKBitmap source, int width, int height)
    {
        var input = source.ColorType is SKColorType.Rgba8888 or SKColorType.Bgra8888 && source.AlphaType != SKAlphaType.Unpremul
            ? source
            : source.Copy(SKImageInfo.PlatformColorType) ?? throw new InvalidOperationException("The picture could not be converted to 32-bit pixels.");
        try
        {
            var across = Pass(input.GetPixelSpan(), input.RowBytes, input.Width, input.Height, width);
            var pixels = Down(across, width, input.Height, height);
            var result = new SKBitmap(new SKImageInfo(width, height, input.ColorType, input.AlphaType));
            Marshal.Copy(pixels, 0, result.GetPixels(), pixels.Length);
            return result;
        }
        finally
        {
            if (!ReferenceEquals(input, source))
            {
                input.Dispose();
            }
        }
    }

    /// <summary>Each row resampled to <paramref name="width"/>, kept as floats for the second pass.</summary>
    private static float[] Pass(ReadOnlySpan<byte> pixels, int rowBytes, int from, int rows, int width)
    {
        var (starts, counts, weights, taps) = Weights(from, width);
        var result = new float[width * rows * 4];
        for (var y = 0; y < rows; y++)
        {
            var row = pixels.Slice(y * rowBytes, from * 4);
            var target = y * width * 4;
            for (var x = 0; x < width; x++)
            {
                float c0 = 0, c1 = 0, c2 = 0, c3 = 0;
                var at = starts[x] * 4;
                var weight = x * taps;
                for (var k = 0; k < counts[x]; k++, at += 4)
                {
                    var w = weights[weight + k];
                    c0 += row[at] * w;
                    c1 += row[at + 1] * w;
                    c2 += row[at + 2] * w;
                    c3 += row[at + 3] * w;
                }

                result[target++] = c0;
                result[target++] = c1;
                result[target++] = c2;
                result[target++] = c3;
            }
        }

        return result;
    }

    /// <summary>Every column resampled to <paramref name="height"/>, rounded and kept premultiplied.</summary>
    private static byte[] Down(float[] across, int width, int from, int height)
    {
        var (starts, counts, weights, taps) = Weights(from, height);
        var stride = width * 4;
        var result = new byte[height * stride];
        var sums = new float[stride];
        for (var y = 0; y < height; y++)
        {
            Array.Clear(sums);
            var weight = y * taps;
            for (var k = 0; k < counts[y]; k++)
            {
                var w = weights[weight + k];
                var row = (starts[y] + k) * stride;
                for (var i = 0; i < stride; i++)
                {
                    sums[i] += across[row + i] * w;
                }
            }

            var target = y * stride;
            for (var i = 0; i < stride; i += 4)
            {
                var alpha = Clamp(sums[i + 3], 255);
                result[target + i] = Clamp(sums[i], alpha);
                result[target + i + 1] = Clamp(sums[i + 1], alpha);
                result[target + i + 2] = Clamp(sums[i + 2], alpha);
                result[target + i + 3] = alpha;
            }
        }

        return result;
    }

    /// <summary>A premultiplied channel never exceeds its alpha; the filter's negative lobes can overshoot both ways.</summary>
    private static byte Clamp(float value, byte most) => (byte)Math.Clamp((int)MathF.Round(value), 0, (int)most);

    /// <summary>
    /// For each output pixel along one axis, the first input pixel it reads, how many, and their
    /// weights summing to one, laid out <c>taps</c> to a pixel.
    /// </summary>
    private static (int[] Starts, int[] Counts, float[] Weights, int Taps) Weights(int from, int to)
    {
        var scale = (double)from / to;
        var spread = Math.Max(scale, 1);
        var support = Lobes * spread;
        var taps = (int)Math.Ceiling(support) * 2 + 1;
        var starts = new int[to];
        var counts = new int[to];
        var weights = new float[to * taps];
        var raw = new double[taps];
        for (var i = 0; i < to; i++)
        {
            var centre = (i + 0.5) * scale;
            var first = Math.Max((int)(centre - support + 0.5), 0);
            var count = Math.Min((int)(centre + support + 0.5), from) - first;
            count = Math.Min(count, taps);
            double total = 0;
            for (var k = 0; k < count; k++)
            {
                raw[k] = Lanczos((first + k - centre + 0.5) / spread);
                total += raw[k];
            }

            for (var k = 0; k < count; k++)
            {
                weights[i * taps + k] = (float)(total == 0 ? 0 : raw[k] / total);
            }

            starts[i] = first;
            counts[i] = count;
        }

        return (starts, counts, weights, taps);
    }

    private static double Lanczos(double x) => x is > -Lobes and < Lobes ? Sinc(x) * Sinc(x / Lobes) : 0;

    private static double Sinc(double x)
    {
        if (x == 0)
        {
            return 1;
        }

        x *= Math.PI;
        return Math.Sin(x) / x;
    }
}
