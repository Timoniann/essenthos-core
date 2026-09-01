using Essenthos.Core.Loading.Links;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// The King James side of the same problem. The pair that exposed it is "divide": the Synodal
/// reduces to one word and the English does not, so one word faces three and its evidence is split
/// three ways rather than gathered.
/// </summary>
public class EnglishStemmerTests
{
    [Theory]
    [InlineData("divide", "divided", "divideth", "divides")]
    [InlineData("gather", "gathered", "gathereth", "gathers")]
    [InlineData("light", "lights")]
    [InlineData("water", "waters")]
    [InlineData("create", "created", "createth")]
    [InlineData("bring", "bringeth", "brings")]
    public void TheFormsOfOneWordLandTogether(params string[] forms) =>
        forms.Select(EnglishStemmer.Stem).Distinct().Should()
            .ContainSingle(because: string.Join(", ", forms.Select(f => $"{f} -> {EnglishStemmer.Stem(f)}")));

    /// <summary>The archaic endings are the ones a modern stemmer does not know and this text is full of.</summary>
    [Theory]
    [InlineData("sayeth", "say")]
    [InlineData("divideth", "divid")]
    [InlineData("sayest", "say")]
    public void TheKingJamesOwnEndingsAreStripped(string form, string expected) =>
        EnglishStemmer.Stem(form).Should().Be(expected);

    [Theory]
    [InlineData("firmament", "firm")]
    [InlineData("water", "waste")]
    [InlineData("god", "good")]
    [InlineData("seas", "season")]
    [InlineData("heaven", "heat")]
    public void DifferentWordsStayApart(string first, string second) =>
        EnglishStemmer.Stem(first).Should().NotBe(EnglishStemmer.Stem(second));

    /// <summary>Below a few letters an ending is most of the word and what is left is not a stem.</summary>
    [Theory]
    [InlineData("is")]
    [InlineData("was")]
    [InlineData("the")]
    [InlineData("and")]
    [InlineData("his")]
    public void ShortWordsAreLeftAlone(string word) => EnglishStemmer.Stem(word).Should().Be(word);

    [Theory]
    [InlineData("divided")]
    [InlineData("waters")]
    [InlineData("gathereth")]
    public void StemmingIsStableOnceApplied(string word)
    {
        var once = EnglishStemmer.Stem(word);

        EnglishStemmer.Stem(once).Should().Be(once);
    }
}
