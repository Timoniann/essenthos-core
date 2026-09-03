using Essenthos.Core.Strong;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// Reading the concordance's own derivations. Every string here is quoted from the Greek entries
/// the corpus holds, so a change to the vocabulary test is scored against what Strong actually
/// wrote rather than against an invented example.
/// </summary>
public class GreekFormDerivationTests
{
    [Theory]
    [InlineData("third person singular present indicative of G1510;", "G1510")]
    [InlineData("imperfect of G1510;", "G1510")]
    [InlineData("genitive case of G4771;", "G4771")]
    [InlineData("irregular dative case of G5210;", "G5210")]
    [InlineData("nominative or accusative case neuter plural of G3778;", "G3778")]
    [InlineData("contracted for G1438;", "G1438")]
    [InlineData("the simpler form of G1700;", "G1700")]
    [InlineData("present infinitive from G1510;", "G1510")]
    public void ReadsAFormAsTheEntryItIsAFormOf(string derivation, string head)
    {
        GreekFormDerivations.Head(derivation).Should().Be(head);
    }

    [Theory]
    // Origin, not identity. Following either would file "this" under "the" and Christ under anoint.
    [InlineData("from the article G3588 and G846;")]
    [InlineData("from G5548;")]
    // An origin word anywhere in the phrase refuses it, however grammatical the rest sounds.
    [InlineData("probably adverb of comparative from G3739;")]
    [InlineData("comparative of a derivative of G2904;")]
    [InlineData("neuter of a presumed derivative of G3739;")]
    [InlineData("neuter singular of the same as G3062;")]
    // Several references, or one that is an aside rather than the head.
    [InlineData("a primary verb (used only in the definite past tense, the others being borrowed " +
                "from G2046, G4483, and G5346);")]
    [InlineData("a primary verb;")]
    [InlineData(null)]
    public void RefusesEverythingThatIsNotAForm(string? derivation)
    {
        GreekFormDerivations.Head(derivation).Should().BeNull();
    }

    [Fact]
    public void ReadsAPhraseEntryAsTheWordsItIsMadeOf()
    {
        GreekFormDerivations.Parts("οὐ μή", "i.e. G3756 and G3361;").Should().Equal("G3756", "G3361");
    }

    [Fact]
    public void LeavesAOneWordEntryAlone()
    {
        GreekFormDerivations.Parts("Χριστός", "from G5548;").Should().BeNull();
    }

    /// <summary>
    /// ὑμῖν is a form of ὑμεῖς, which the editions do not tag either. Stopping there would leave it
    /// unreachable; σύ beyond it is a word on the page.
    /// </summary>
    [Fact]
    public void FollowsTheChainToTheFirstNumberTheGreekWrites()
    {
        var resolved = GreekFormDerivations.Resolve(
            [
                new GreekEntry("G5213", "ὑμῖν", "irregular dative case of G5210;"),
                new GreekEntry("G5210", "ὑμεῖς", "irregular plural of G4771;"),
                new GreekEntry("G4771", "σύ", "the personal pronoun of the second person singular;"),
            ],
            number => number == "G4771");

        resolved["G5213"].Should().Equal("G4771");
        resolved["G5210"].Should().Equal("G4771");
        resolved.Should().NotContainKey("G4771");
    }

    /// <summary>
    /// A number the Greek witness tags is a lemma there. Redirecting it would rewrite the text's own
    /// tagging, and it is what keeps the etymologies the vocabulary test lets through harmless.
    /// </summary>
    [Fact]
    public void LeavesANumberTheGreekAlreadyWritesWhereItIs()
    {
        var resolved = GreekFormDerivations.Resolve(
            [new GreekEntry("G2076", "ἐστί", "third person singular present indicative of G1510;")],
            number => number is "G1510" or "G2076");

        resolved.Should().BeEmpty();
    }

    [Fact]
    public void SurvivesADerivationThatPointsAtItself()
    {
        var resolved = GreekFormDerivations.Resolve(
            [
                new GreekEntry("G3450", "μοῦ", "the simpler form of G1700;"),
                new GreekEntry("G1700", "ἐμοῦ", "a prolonged form of G3450;"),
            ],
            number => number == "G1473");

        resolved.Should().BeEmpty();
    }
}
