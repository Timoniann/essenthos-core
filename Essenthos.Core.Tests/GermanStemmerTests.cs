using Essenthos.Core.Loading.Links;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// Luther's German, reduced the way the King James and the Synodal already are, so that the forms of
/// one German word meet the one Hebrew word they render instead of splitting its evidence.
/// </summary>
public class GermanStemmerTests
{
    [Theory]
    [InlineData("Himmel", "Himmels")]
    [InlineData("Kind", "Kinder", "Kindern")]
    [InlineData("Sohn", "Söhne", "Söhnen")]
    [InlineData("Vater", "Väter", "Vätern")]
    [InlineData("sprachen", "sprach")]
    [InlineData("Wasser", "Wassers")]
    [InlineData("Erde", "Erden")]
    [InlineData("Land", "Lande", "Landes")]
    public void TheFormsOfOneWordLandTogether(params string[] forms) =>
        forms.Select(GermanStemmer.Stem).Distinct().Should()
            .ContainSingle(because: string.Join(", ", forms.Select(f => $"{f} -> {GermanStemmer.Stem(f)}")));

    [Theory]
    [InlineData("Gott", "gut")]
    [InlineData("Land", "Leute", "Laut")]
    [InlineData("Herr", "Heer")]
    public void DifferentWordsStayApart(string first, string second, string? third = null)
    {
        GermanStemmer.Stem(first).Should().NotBe(GermanStemmer.Stem(second));
        if (third is not null)
        {
            GermanStemmer.Stem(third).Should().NotBe(GermanStemmer.Stem(first));
        }
    }

    [Theory]
    [InlineData("und")]
    [InlineData("der")]
    [InlineData("die")]
    [InlineData("es")]
    public void ShortWordsAreLeftAlone(string word) => GermanStemmer.Stem(word).Should().Be(word);

    [Fact]
    public void TheSharpSIsSpelledOut() =>
        GermanStemmer.Stem("großen").Should().Be(GermanStemmer.Stem("grossen"));
}
