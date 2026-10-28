using System.Text.RegularExpressions;
using Essenthos.Core.Endpoints;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Reader;

/// <summary>
/// The smaller copies of a picture that a list, a card and an avatar are served: found by the digest
/// of the picture they were made from, at the narrowest width that is still wide enough, and never
/// anywhere the pictures' folder does not hold.
/// </summary>
public sealed class PictureSizeTests : IDisposable
{
    private const string Digest = "0123456789ab";

    private readonly string _folder = Directory.CreateTempSubdirectory("picture-sizes-").FullName;

    public PictureSizeTests()
    {
        Write("generated/aaron.webp");
        Write($"sized/256/generated/aaron.{Digest}.{ImageEndpoints.CopyRecipe}.webp");
        Write($"sized/128/openbible/gerar.i1.{Digest}.{ImageEndpoints.CopyRecipe}.webp");
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Theory]
    [InlineData(1, 64)]
    [InlineData(65, 80)]
    [InlineData(128, 128)]
    [InlineData(129, 160)]
    [InlineData(200, 256)]
    [InlineData(1024, 1024)]
    public void AWidthIsServedByTheNarrowestCopyAsWideAsIt(int asked, int served) =>
        ImageEndpoints.SizedWidth(asked).Should().Be(served);

    [Fact]
    public void AWidthPastTheWidestCopyIsThePictureItself() =>
        ImageEndpoints.SizedWidth(1025).Should().BeNull();

    [Fact]
    public void ACopyIsNamedByThePictureItWasMadeFrom() =>
        ImageEndpoints.SizedPath("openbible/gerar.i1.jpg", Digest, 128).Should().Be($"sized/128/openbible/gerar.i1.{Digest}.l3q85-80.webp");

    [Fact]
    public void ACopyOfABustIsNamedByItsSquare() =>
        ImageEndpoints.SizedPath("generated/aaron.webp", Digest, 128, new PictureCrop(205, 11, 634)).Should()
            .Be($"sized/128/generated/aaron.{Digest}.c205-11-634.l3q85-80.webp");

    [Fact]
    public void ABustIsCutInThePicturesOwnPixels() =>
        PictureCrop.Of(1024, 1536, 0.200375, 0.007425, 0.61875).Should().Be(new PictureCrop(205, 11, 634));

    [Fact]
    public void ABustRunningPastAnEdgeIsCutInsideThePicture()
    {
        PictureCrop.Of(200, 200, 0.8, 0, 0.5).Should().Be(new PictureCrop(160, 0, 40));
        PictureCrop.Of(0, 0, 0.2, 0, 0.6).Should().BeNull("a picture of no known size has no pixels to cut");
    }

    [Theory]
    [InlineData("205,11,634", true)]
    [InlineData("205,11", false)]
    [InlineData("205,11,6", false)]
    [InlineData("205,11,634,1", false)]
    [InlineData("-5,11,634", false)]
    [InlineData("205,11,1e3", false)]
    [InlineData("999999,1,634", false)]
    public void ACropIsThreeWholeNumbers(string text, bool read) =>
        (PictureCrop.Parse(text) is not null).Should().Be(read);

    [Fact]
    public void ACopyMadeAnOlderWayIsNotServed()
    {
        Write($"sized/512/generated/aaron.{Digest}.webp");

        ImageEndpoints.Sized(_folder, "generated/aaron.webp", Digest, 400).Should().BeNull();
    }

    [Fact]
    public void ACopyIsFoundByItsWidthAndTheDigestOfItsPicture()
    {
        ImageEndpoints.Sized(_folder, "generated/aaron.webp", Digest, 200).Should()
            .Be(Path.GetFullPath(Path.Combine(_folder, "sized", "256", "generated", $"aaron.{Digest}.l3q85-80.webp")));
        ImageEndpoints.Sized(_folder, "openbible/gerar.i1.jpg", Digest, 120).Should().NotBeNull();
    }

    [Theory]
    [InlineData("fedcba987654", 200)]
    [InlineData(Digest, 90)]
    [InlineData(Digest, 2000)]
    [InlineData(null, 200)]
    [InlineData("0123456789AB", 200)]
    [InlineData("../../x", 200)]
    public void NoCopyIsServedButTheOneMadeFromThesePicturesBytesAtThatWidth(string? digest, int width) =>
        ImageEndpoints.Sized(_folder, "generated/aaron.webp", digest, width).Should().BeNull();

    [Fact]
    public void NoCopyIsFoundOutsideThePicturesFolder()
    {
        var outside = Path.Combine(Path.GetDirectoryName(_folder)!, $"{Path.GetFileName(_folder)}-outside.{Digest}.webp");
        File.WriteAllBytes(outside, [0]);
        try
        {
            ImageEndpoints.Sized(_folder, $"../../../{Path.GetFileName(_folder)}-outside.webp", Digest, 200).Should().BeNull();
        }
        finally
        {
            File.Delete(outside);
        }
    }

    [Fact]
    public void TheScriptMakesTheCopiesAtTheWidthsTheApiServesAndNamesThemAsItDoes()
    {
        var script = File.ReadAllText(Path.Combine(Checkout(), "scripts", "picture-sizes.py"));
        var widths = Regex.Match(script, @"^WIDTHS = \(([^)]*)\)", RegexOptions.Multiline).Groups[1].Value;

        widths.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Select(int.Parse)
            .Should().Equal(ImageEndpoints.SizedWidths);
        Regex.Match(script, @"^RECIPE = '([^']*)'", RegexOptions.Multiline).Groups[1].Value
            .Should().Be(ImageEndpoints.CopyRecipe);
    }

    private void Write(string file)
    {
        var path = Path.Combine(_folder, file);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, [0]);
    }

    private static string Checkout()
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            if (File.Exists(Path.Combine(folder.FullName, "Essenthos.Core.sln")))
            {
                return folder.FullName;
            }
        }

        throw new DirectoryNotFoundException("The tests were not built inside an essenthos-core checkout.");
    }
}
