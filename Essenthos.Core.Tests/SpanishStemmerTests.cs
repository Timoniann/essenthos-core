using Essenthos.Core.Loading.Links;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// The 1909 Reina-Valera, reduced so that the person, tense and number on the end of a Spanish verb
/// stop splitting one Hebrew verb's evidence across a dozen strings.
/// </summary>
public class SpanishStemmerTests
{
    [Theory]
    [InlineData("hablar", "hablaba", "hablaron", "hablando", "habló")]
    [InlineData("hijo", "hijos", "hija", "hijas")]
    [InlineData("tierra", "tierras")]
    [InlineData("respondió", "respondieron", "responder")]
    public void TheFormsOfOneWordLandTogether(params string[] forms) =>
        forms.Select(SpanishStemmer.Stem).Distinct().Should()
            .ContainSingle(because: string.Join(", ", forms.Select(f => $"{f} -> {SpanishStemmer.Stem(f)}")));

    /// <summary>The 1909 spelling writes the pronoun onto the verb and marks it with an accent.</summary>
    [Theory]
    [InlineData("respondióle", "respondió")]
    [InlineData("levantóse", "levantó")]
    [InlineData("díjole", "dijo")]
    [InlineData("haciéndolo", "haciendo")]
    public void APronounWrittenOntoTheVerbComesOff(string joined, string verb) =>
        SpanishStemmer.Stem(joined).Should().Be(SpanishStemmer.Stem(verb));

    /// <summary>Without the accent there is no verb that grew: a noun ending in -la keeps it.</summary>
    [Theory]
    [InlineData("escuela")]
    [InlineData("tabla")]
    [InlineData("valle")]
    public void ANounEndingLikeAPronounKeepsIt(string noun) =>
        SpanishStemmer.Stem(noun).Should().NotBe(SpanishStemmer.Stem(noun[..^2]));

    [Theory]
    [InlineData("de")]
    [InlineData("y")]
    [InlineData("á")]
    [InlineData("él")]
    [InlineData("el")]
    public void ShortWordsAreLeftAlone(string word) => SpanishStemmer.Stem(word).Should().Be(word);
}
