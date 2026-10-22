using Essenthos.Core.Corpus;
using Essenthos.Core.Endpoints;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Lexicon;

/// <summary>What a verse search makes of a word: the stem its forms share, and the joints a verse need not hold.</summary>
public class SearchWordsTests
{
    [Theory]
    [InlineData("працює", "працювати", "працюють", "працював", "працюючи")]
    [InlineData("воскресіння", "воскресінні")]
    [InlineData("життя", "життям")]
    public void TheFormsOfAUkrainianWordShareAStem(params string[] forms) =>
        forms.Select(form => SearchWords.StemHere(form, "ukr")).Distinct().Should()
            .ContainSingle(because: string.Join(", ", forms.Select(f => $"{f} -> {SearchWords.StemHere(f, "ukr")}")));

    [Fact]
    public void TwoSpellingsOfOneWordAreNotOneStem() =>
        SearchWords.StemHere("воскресіння", "ukr").Should().NotBe(SearchWords.StemHere("воскресення", "ukr"),
            "a near spelling is offered, never matched in silence");

    [Theory]
    [InlineData("і", "й")]
    [InlineData("у", "в")]
    public void AJointIsEitherOfItsSpellings(string typed, string printed) =>
        SearchWords.Joint(typed, "ukr").Should().Contain(printed);

    [Fact]
    public void NotIsNoJoint() => SearchWords.Joint("не", "ukr").Should().BeNull();

    [Theory]
    [InlineData("воскресення", "воскресіння", true)]
    [InlineData("борони", "ворони", false)]
    public void AWordThePrintedTextHoldsIsSecondGuessedOnlyInItsVowels(string near, string typed, bool vowelsOnly) =>
        (VerseSearch.Consonants(near) == VerseSearch.Consonants(typed)).Should().Be(vowelsOnly);

    [Theory]
    [InlineData("їсти", "їсть", true)]
    [InlineData("рамесес", "рамсес", false)]
    public void AnotherEndingIsNoMisspelling(string near, string typed, bool ending) =>
        VerseSearch.OtherEnding(near, typed).Should().Be(ending);

    [Fact]
    public void AWordHeldWholeInALongerOneIsOneLetterAway() =>
        VerseSearch.Apart("всевишнього", "вишнього").Should().Be(1);
}
