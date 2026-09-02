using Essenthos.Core.Endpoints;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

public class SearchTermsTests
{
    [Fact]
    public void SplitsOnWhitespace()
    {
        SearchTerms.Parse("  God   so  loved ").Should().Equal("God", "so", "loved");
    }

    [Fact]
    public void StripsPunctuationThatWouldNeverMatchAWord()
    {
        SearchTerms.Parse("\"loved,\" (world).").Should().Equal("loved", "world");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(",.;")]
    public void AnEmptyQueryHasNoTerms(string? query)
    {
        SearchTerms.Parse(query).Should().BeEmpty();
    }

    [Fact]
    public void LongQueriesAreCappedRatherThanRefused()
    {
        var query = string.Join(' ', Enumerable.Range(1, 20).Select(i => $"word{i}"));
        SearchTerms.Parse(query).Should().HaveCount(SearchTerms.MaxTerms);
    }

    [Theory]
    [InlineData("en", SearchTerms.EnglishDictionary)]
    [InlineData("EN", SearchTerms.EnglishDictionary)]
    [InlineData("ru", SearchTerms.RussianDictionary)]
    public void LanguagesWithADictionaryAreStemmed(string language, string dictionary)
    {
        SearchTerms.Dictionary(language).Should().Be(dictionary);
        SearchTerms.Matching(language).Should().Be(SearchTerms.FullTextMatching);
    }

    [Theory]
    [InlineData("uk")]
    [InlineData("hbo")]
    [InlineData("grc")]
    public void LanguagesWithoutOneFallBackToSubstringAndSaySo(string language)
    {
        SearchTerms.Dictionary(language).Should().BeNull();
        SearchTerms.Matching(language).Should().Be(SearchTerms.SubstringMatching);
    }

    [Fact]
    public void AQueryMatchedOneWayIsReportedAsThatWay()
    {
        SearchTerms.Matching([Term("god", TermMatching.Stemmed), Term("loved", TermMatching.Stemmed)])
            .Should().Be(SearchTerms.FullTextMatching);
        SearchTerms.Matching([Term("logos", TermMatching.Folded), Term("theos", TermMatching.Folded)])
            .Should().Be(SearchTerms.WholeWordMatching);
        SearchTerms.Matching([Term("logo", TermMatching.Substring), Term("theo", TermMatching.Substring)])
            .Should().Be(SearchTerms.SubstringMatching);
    }

    [Fact]
    public void MatchingAWholeWordIsNotMatchingPartOfOne()
    {
        // The words are stored one to a row, so matching one is matching a word. Reporting that
        // as "substring" told the caller the opposite of what happened.
        SearchTerms.Matching([Term("logos", TermMatching.Folded), Term("theos", TermMatching.Substring)])
            .Should().Be(SearchTerms.MixedMatching);
    }

    /// <summary>
    /// a stop word cannot be stemmed, so it is matched literally instead of answering
    /// nothing — and the response has to say that is what happened.
    /// </summary>
    [Fact]
    public void AQueryMatchedTwoWaysSaysSo()
    {
        SearchTerms.Matching([Term("the", TermMatching.Literal), Term("beginning", TermMatching.Stemmed)])
            .Should().Be(SearchTerms.MixedMatching);
    }

    [Theory]
    [InlineData(TermMatching.Stemmed, "stemmed")]
    [InlineData(TermMatching.Literal, "literal")]
    [InlineData(TermMatching.Folded, "folded")]
    [InlineData(TermMatching.Substring, "substring")]
    internal void EveryTermSaysHowItWasMatched(TermMatching matching, string name)
    {
        SearchTerms.Name(matching).Should().Be(name);
    }

    private static SearchTerm Term(string text, TermMatching matching)
    {
        return new SearchTerm(text, matching);
    }
}
