using Essenthos.Core.Loading.Links;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// The 1911 Almeida, reduced so that the person, tense and number on the end of a Portuguese verb,
/// and the pronoun hyphenated onto it, stop splitting one Hebrew or Greek word's evidence.
/// </summary>
public class PortugueseStemmerTests
{
    [Theory]
    [InlineData("disse", "disse-lhe", "disse-lhes", "Disse-lhe")]
    [InlineData("filho", "filhos", "filha", "filhas")]
    [InlineData("comer", "comemos", "comeram", "comendo", "comia")]
    [InlineData("levantou", "levantou-se", "levantaram")]
    public void TheFormsOfOneWordLandTogether(params string[] forms) =>
        forms.Select(PortugueseStemmer.Stem).Distinct().Should()
            .ContainSingle(because: string.Join(", ", forms.Select(f => $"{f} -> {PortugueseStemmer.Stem(f)}")));

    /// <summary>A hyphen joining two words of a name or a compound is not a pronoun joined to a verb.</summary>
    [Theory]
    [InlineData("Beth-lehem")]
    [InlineData("Todo-poderoso")]
    public void ANameWithAHyphenStaysWhole(string word) =>
        PortugueseStemmer.Stem(word).Should().Contain("-");

    [Theory]
    [InlineData("e")]
    [InlineData("é")]
    [InlineData("de")]
    [InlineData("que")]
    [InlineData("os")]
    public void ShortWordsAreLeftAlone(string word) => PortugueseStemmer.Stem(word).Should().Be(word);
}
