using Essenthos.Core.Usfm;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Essenthos.Core.Tests;

/// <summary>
/// Whether an edition's Strong numbers name the word that renders each original word, or only the
/// numbers its verse holds — decided by counting, so a text taken next is judged the way these were
/// rather than by whether somebody remembered to add it to a list.
/// </summary>
[Trait(TestCategory.Name, TestCategory.Corpus)]
public class StrongTaggingTests(ITestOutputHelper output)
{
    private static readonly string[] NotScripture = ["FRT", "INT", "GLO", "BAK"];

    private static IReadOnlyList<UsfmBook> Books(string folder) =>
        [.. Directory.GetFiles(TestResources.EbibleFolder(folder), "*.usfm")
            .Select(File.ReadAllText)
            .Where(content => !NotScripture.Any(code => content.StartsWith($"\\id {code}", StringComparison.Ordinal)))
            .Select(UsfmReader.Read)];

    [Theory]
    [InlineData("AmericanStandard1901")]
    [InlineData("WorldEnglish")]
    public void TheEnglishLayerIsTheVersesNumbersSprayedOverItsWords(string folder)
    {
        var books = Books(folder);
        var measure = StrongTagging.Measure(books);
        output.WriteLine($"{folder}: {measure}");

        measure.Tags.Should().BeGreaterThan(600_000, "the layer is there to be refused");
        measure.PlacesPerNumber.Should().BeGreaterThan(2);
        StrongTagging.IsWordLevel(books).Should().BeFalse();
    }

    /// <summary>
    /// The Reina-Valera's numbers are refused for whose they are, not for what they are: tagged
    /// phrase by phrase, they pass.
    /// </summary>
    [Theory]
    [InlineData("Luther1912")]
    [InlineData("ReinaValera1909")]
    public void AHandMadeTaggingPutsANumberOnTheWordThatRendersIt(string folder)
    {
        var books = Books(folder);
        var measure = StrongTagging.Measure(books);
        output.WriteLine($"{folder}: {measure}");

        measure.Tags.Should().BeGreaterThan(300_000);
        measure.PlacesPerNumber.Should().BeLessThan(1.2);
        StrongTagging.IsWordLevel(books).Should().BeTrue();
    }

    /// <summary>The American Standard's Genesis 1:1, as the file tags it.</summary>
    [Fact]
    public void GenesisOneOneIsThreeNumbersAtEightPlaces()
    {
        var book = UsfmReader.Read(
            """
            \id GEN
            \c 1
            \p
            \v 1 \w In|strong="H8064"\w* \w the|strong="H1254"\w* \w beginning|strong="H7225"\w* \w God|strong="H8064"\w* \w created|strong="H1254"\w* \w the|strong="H1254"\w* \w heavens|strong="H8064"\w* \w and|strong="H8064"\w* \w the|strong="H1254"\w* \w earth|strong="H8064"\w*.
            """);

        StrongTagging.Measure([book]).PlacesPerNumber.Should().BeApproximately(8 / 3.0, 0.001);
        StrongTagging.IsWordLevel([book]).Should().BeFalse();
    }

    [Fact]
    public void AnEditionThatTagsNothingHasNothingToRefuse()
    {
        var book = UsfmReader.Read(
            """
            \id GEN
            \c 1
            \p
            \v 1 In the beginning God created the heaven and the earth.
            """);

        StrongTagging.Measure([book]).Should().Be(new StrongTaggingMeasure(0, 0, 0, 0));
        StrongTagging.IsWordLevel([book]).Should().BeTrue();
    }
}
