using Essenthos.Core.Endpoints;
using FluentAssertions;
using SkiaSharp;
using Xunit;

namespace Essenthos.Core.Tests.Platform;

/// <summary>
/// The filter the smaller copies are made with: fine detail keeps its contrast however far the picture
/// is shrunk, a flat colour stays that colour, and a transparent part does not darken what it borders.
/// </summary>
public sealed class PictureResamplerTests
{
    /// <summary>
    /// Detail four copy pixels across keeps what sampling it at all can keep (a sine sampled four to
    /// its period shows 0.71 of its swing); a halving pyramid finished by a Mitchell pass keeps 0.57 to
    /// 0.67 of it.
    /// </summary>
    [Theory]
    [InlineData(1024, 128)]
    [InlineData(1024, 192)]
    [InlineData(1024, 256)]
    [InlineData(600, 384)]
    [InlineData(1536, 1024)]
    public void FineDetailKeepsItsContrast(int from, int to)
    {
        const double period = 4;
        var scale = (double)from / to;
        using var picture = Picture(from, 8, x =>
        {
            var grey = (byte)Math.Round(128 + 100 * Math.Sin(2 * Math.PI * (x + 0.5) / (period * scale)));
            return new SKColor(grey, grey, grey);
        });

        using var copy = PictureResampler.Resize(picture, to, 4);

        var row = Enumerable.Range(to / 4, to / 2).Select(x => (int)copy.GetPixel(x, 1).Red).ToArray();
        (row.Max() - row.Min()).Should().BeGreaterThanOrEqualTo(136);
    }

    [Fact]
    public void AFlatColourStaysThatColour()
    {
        var colour = new SKColor(181, 122, 63);
        using var picture = Picture(1024, 1536, _ => colour);

        using var copy = PictureResampler.Resize(picture, 96, 144);

        copy.Pixels.Should().AllSatisfy(pixel => pixel.Should().Be(colour));
    }

    [Fact]
    public void ATransparentPartDoesNotDarkenWhatItBorders()
    {
        using var picture = Picture(512, 512, x => x < 256 ? SKColors.Transparent : SKColors.White);

        using var copy = PictureResampler.Resize(picture, 100, 100);

        copy.Pixels.Where(pixel => pixel.Alpha > 16).Should()
            .AllSatisfy(pixel => pixel.Red.Should().BeGreaterThan(245, "white over transparency is still white"));
    }

    private static SKBitmap Picture(int width, int height, Func<int, SKColor> colour)
    {
        var bitmap = new SKBitmap(new SKImageInfo(width, height, SKImageInfo.PlatformColorType, SKAlphaType.Premul));
        for (var x = 0; x < width; x++)
        {
            var pixel = colour(x);
            for (var y = 0; y < height; y++)
            {
                bitmap.SetPixel(x, y, pixel);
            }
        }

        return bitmap;
    }
}
