using System.Text.RegularExpressions;
using Essenthos.Core.Endpoints;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

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
        Write($"sized/256/generated/aaron.{Digest}.webp");
        Write($"sized/128/openbible/gerar.i1.{Digest}.webp");
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Theory]
    [InlineData(1, 128)]
    [InlineData(128, 128)]
    [InlineData(129, 256)]
    [InlineData(1024, 1024)]
    public void AWidthIsServedByTheNarrowestCopyAsWideAsIt(int asked, int served) =>
        ImageEndpoints.SizedWidth(asked).Should().Be(served);

    [Fact]
    public void AWidthPastTheWidestCopyIsThePictureItself() =>
        ImageEndpoints.SizedWidth(1025).Should().BeNull();

    [Fact]
    public void ACopyIsNamedByThePictureItWasMadeFrom() =>
        ImageEndpoints.SizedPath("openbible/gerar.i1.jpg", Digest, 128).Should().Be($"sized/128/openbible/gerar.i1.{Digest}.webp");

    [Fact]
    public void ACopyIsFoundByItsWidthAndTheDigestOfItsPicture()
    {
        ImageEndpoints.Sized(_folder, "generated/aaron.webp", Digest, 200).Should()
            .Be(Path.GetFullPath(Path.Combine(_folder, "sized", "256", "generated", $"aaron.{Digest}.webp")));
        ImageEndpoints.Sized(_folder, "openbible/gerar.i1.jpg", Digest, 90).Should().NotBeNull();
    }

    [Theory]
    [InlineData("fedcba987654", 200)]
    [InlineData(Digest, 100)]
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
    public void TheScriptMakesTheCopiesAtTheWidthsTheApiServes()
    {
        var script = File.ReadAllText(Path.Combine(Checkout(), "scripts", "picture-sizes.py"));
        var widths = Regex.Match(script, @"^WIDTHS = \(([^)]*)\)", RegexOptions.Multiline).Groups[1].Value;

        widths.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Select(int.Parse)
            .Should().Equal(ImageEndpoints.SizedWidths);
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
