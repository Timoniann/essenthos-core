using Essenthos.Core.Corpus;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Lexicon;

/// <summary>The spellings a search takes for one word: every name Ukrainian gives Hebrew, in every form.</summary>
public class SearchSynonymsTests
{
    [Theory]
    [InlineData("єврейською", "гебрейською", "давньоєврейською")]
    [InlineData("гебрейського", "давньоєврейського", "єврейського")]
    [InlineData("давньоєврейська", "гебрейська", "єврейська")]
    [InlineData("по-єврейськи", "по-гебрейськи", "по-давньоєврейськи")]
    public void AUkrainianNameOfHebrewIsAlsoSoughtAsTheOthers(string folded, params string[] others) =>
        SearchSynonyms.Others(folded, "ukr").Should().BeEquivalentTo(others);

    [Theory]
    [InlineData("грецькою", "ukr")]
    [InlineData("єврей", "ukr")]
    [InlineData("єврейською", "rus")]
    [InlineData("єврейською", null)]
    public void AnyOtherWordHasNoOtherSpelling(string folded, string? language) =>
        SearchSynonyms.Others(folded, language).Should().BeEmpty();
}
