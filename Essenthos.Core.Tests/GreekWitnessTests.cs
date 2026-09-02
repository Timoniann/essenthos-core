using Essenthos.Core.TextusReceptus;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// Nestle is accented and the Textus Receptus is not. Comparing them as written would report every
/// word in the New Testament as a different word, which is the opposite of what two witnesses are
/// held for — so this is the comparison the whole loader rests on.
/// </summary>
public class GreekWitnessTests
{
    [Theory]
    [InlineData("Βίβλος", "βιβλος")]
    [InlineData("γενέσεως", "γενεσεως")]
    [InlineData("Ἰησοῦ", "ιησου")]
    [InlineData("Χριστοῦ", "χριστου")]
    [InlineData("υἱοῦ", "υιου")]
    [InlineData("Ἀβραάμ", "αβρααμ")]
    public void TheTwoEditionsWriteTheSameWordTheSameWay(string nestle, string receptus)
    {
        GreekLetters.Bare(nestle).Should().Be(GreekLetters.Bare(receptus));
    }

    [Fact]
    public void AFinalSigmaIsTheSameLetterAsAMedialOne()
    {
        // The position of a sigma is a fact about where it stands, not about which word it is.
        GreekLetters.Bare("λόγος").Should().Be(GreekLetters.Bare("λογοσ"));
    }

    [Fact]
    public void WordsThatDifferStillDiffer()
    {
        // Δαυείδ and Δαβίδ are the reading the two editions actually disagree on at Matthew 1:1,
        // and setting accents aside must not quietly make them the same word.
        GreekLetters.Bare("Δαυεὶδ").Should().NotBe(GreekLetters.Bare("δαβιδ"));
    }

    [Fact]
    public void NothingIsLostFromAWordThatCarriesNoAccents()
    {
        GreekLetters.Bare("αβρααμ").Should().Be("αβρααμ");
    }

    [Fact]
    public void NoAccentedLetterSurvivesTheStripping()
    {
        // The check the normalising version could not make. Every accented letter the loaded Greek
        // texts contain, run through at once: if the table has a gap, this is where it shows,
        // rather than in a link count nobody reads.
        const string everyAccentedLetter =
            "ἀἈἄἌᾄἂἆἎἁἉἅἍἃἋάᾴὰᾶᾷᾳἐἘἔἜἑἙἕἝἓἛέὲἠἨἤἬᾔἢἪἦἮᾖᾐἡἩἥἭἣἧᾗᾑήῄὴῆῇῃ" +
            "ἰἸἴἼἶἱἹἵἽἳἷίὶῖϊΐῒὀὈὄὌὂὁὉὅὍὃὋόὸῥῬὐὔὒὖὑὙὕὝὓὗὟύὺῦϋΰῢ" +
            "ὠὤὬὢὦὮᾠὡὩὥὭὧὯᾧώῴὼῶῷῳ";

        GreekLetters.Bare(everyAccentedLetter).Should().MatchRegex("^[αβγδεζηθικλμνξοπρστυφχψω]+$");
    }

    [Fact]
    public void ACapitalIsTheSameLetterAsASmallOne()
    {
        GreekLetters.Bare("Θεός").Should().Be(GreekLetters.Bare("qeov".Replace("qeov", "θεος")));
    }
}
