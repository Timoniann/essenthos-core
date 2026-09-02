using Essenthos.Core.Loading.Links;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// Greek declines a noun eight ways and conjugates a verb hundreds. A model counting words over
/// twenty-three thousand verses cannot learn any one of those forms, which is the same problem
/// BHSA's vowel pointing posed and takes the same answer.
///
/// The risk runs the other way too: a stemmer that takes off too much merges words that are not
/// the same word, and a model cannot tell it has been lied to. Both halves are tested.
/// </summary>
public class GreekStemmerTests
{
    [Theory]
    [InlineData("Θεός")]
    [InlineData("Θεοῦ")]
    [InlineData("Θεῷ")]
    [InlineData("Θεόν")]
    [InlineData("θεοὶ")]
    [InlineData("θεῶν")]
    [InlineData("θεοῖς")]
    [InlineData("θεούς")]
    public void EveryCaseOfGodIsOneWord(string form)
    {
        GreekStemmer.Stem(form).Should().Be("θε");
    }

    [Theory]
    [InlineData("λόγος")]
    [InlineData("λόγου")]
    [InlineData("λόγῳ")]
    [InlineData("λόγον")]
    [InlineData("λόγοι")]
    [InlineData("λόγων")]
    [InlineData("λόγοις")]
    [InlineData("λόγους")]
    public void EveryCaseOfWordIsOneWord(string form)
    {
        GreekStemmer.Stem(form).Should().Be("λογ");
    }

    [Theory]
    [InlineData("ἀρχή")]
    [InlineData("ἀρχῇ")]
    [InlineData("ἀρχήν")]
    [InlineData("ἀρχῆς")]
    public void TheFirstWordOfGenesisIsOneWordInEveryCase(string form)
    {
        GreekStemmer.Stem(form).Should().Be("αρχ");
    }

    [Fact]
    public void ShortWordsAreLeftAlone()
    {
        // Articles, prepositions and particles: they have no ending to lose, and taking a letter
        // off ὁ or ἐν leaves nothing at all.
        foreach (var word in new[] { "ὁ", "ἡ", "τό", "ἐν", "καὶ", "δὲ", "τοῦ", "εἰς", "γάρ", "οὐκ" })
        {
            GreekStemmer.Stem(word).Should().Be(Essenthos.Core.TextusReceptus.GreekLetters.Bare(word));
        }
    }

    [Fact]
    public void AccentsGoFirst()
    {
        // Brenton is accented and the accent moves with the ending, so the same word differs in
        // more places than its ending. Without this the stemmer would be working on the wrong
        // string before it started.
        GreekStemmer.Stem("οὐρανὸν").Should().Be(GreekStemmer.Stem("ουρανον"));
    }

    [Fact]
    public void FormsOfOneWordMeetWhereTheyCan()
    {
        // γῆ and γῆν meet, because the ending is all that separates them.
        GreekStemmer.Stem("γῆν").Should().Be(GreekStemmer.Stem("γῆ"));

        // φῶς and φωτός do not, because Greek changes the stem itself there. This stemmer
        // deliberately does not guess at that: a rule reaching φωτ- from φως would reach a great
        // many wrong places on the way.
        GreekStemmer.Stem("φῶς").Should().NotBe(GreekStemmer.Stem("φωτός"));
    }

    [Fact]
    public void TheArticleAndThePrepositionsAreLeftExactlyAsTheyAre()
    {
        // εἰς stripped of its sigma is ει, which is also εἰ, "if". Merging a preposition into a
        // conjunction teaches the model a word that does not exist.
        GreekStemmer.Stem("εἰς").Should().Be("εισ");
        GreekStemmer.Stem("εἰ").Should().Be("ει");
        GreekStemmer.Stem("τοῦ").Should().Be("του");
        GreekStemmer.Stem("τῶν").Should().Be("των");
    }

    [Theory]
    [InlineData("θεός", "λόγος")]
    [InlineData("ἀρχή", "ἀρετή")]
    [InlineData("υἱός", "οἶκος")]
    public void WordsThatAreNotTheSameWordStayApart(string one, string other)
    {
        GreekStemmer.Stem(one).Should().NotBe(GreekStemmer.Stem(other));
    }

    [Fact]
    public void AStemNeverShrinksBelowWhatIdentifiesIt()
    {
        foreach (var word in new[] { "ἄνθρωπος", "οὐρανός", "ἡμέρα", "ὕδατος", "πνεῦμα" })
        {
            GreekStemmer.Stem(word).Length.Should().BeGreaterThanOrEqualTo(2);
        }
    }
}
