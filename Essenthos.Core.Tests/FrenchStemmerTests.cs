using Essenthos.Core.Loading.Links;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// The 1910 Louis Segond, reduced so that the elided article and the person, tense and number on
/// the end of a French verb stop splitting one Hebrew or Greek word's evidence across many strings.
/// </summary>
public class FrenchStemmerTests
{
    [Theory]
    [InlineData("donner", "donna", "donnait", "donnais", "donnaient", "donnèrent", "donnez", "donnons", "donné", "données")]
    [InlineData("enfant", "enfants")]
    [InlineData("homme", "hommes", "l’homme", "d’hommes")]
    [InlineData("parla", "parler", "parlerai")]
    public void TheFormsOfOneWordLandTogether(params string[] forms) =>
        forms.Select(FrenchStemmer.Stem).Distinct().Should()
            .ContainSingle(because: string.Join(", ", forms.Select(f => $"{f} -> {FrenchStemmer.Stem(f)}")));

    /// <summary>A longer word elided onto the next one is the word that means something, and stays.</summary>
    [Theory]
    [InlineData("jusqu’à")]
    [InlineData("lorsqu’il")]
    public void ALongerElidedWordStays(string word) =>
        FrenchStemmer.Stem(word).Should().StartWith(word[..word.IndexOf('’')]);

    [Theory]
    [InlineData("de")]
    [InlineData("et")]
    [InlineData("il")]
    [InlineData("les")]
    [InlineData("ils")]
    public void ShortWordsAreLeftAlone(string word) => FrenchStemmer.Stem(word).Should().Be(word);
}
