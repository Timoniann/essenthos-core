using Essenthos.Core.TextusReceptus;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// The Textus Receptus arrives in Latin letters. These are the first words of Matthew, against the
/// Nestle text of the same verse — the two witnesses this corpus exists to lay side by side, so if
/// the conversion is wrong the comparison is worthless.
/// </summary>
public class BetaCodeTests
{
    [Theory]
    [InlineData("biblov", "βιβλος")]
    [InlineData("genesewv", "γενεσεως")]
    [InlineData("ihsou", "ιησου")]
    [InlineData("cristou", "χριστου")]
    [InlineData("uiou", "υιου")]
    [InlineData("dabid", "δαβιδ")]
    [InlineData("abraam", "αβρααμ")]
    public void MatthewOneOne(string beta, string greek)
    {
        BetaCode.ToGreek(beta).Should().Be(greek);
    }

    [Theory]
    [InlineData("qeov", "θεος")]
    [InlineData("yuch", "ψυχη")]
    [InlineData("logov", "λογος")]
    [InlineData("xulon", "ξυλον")]
    [InlineData("zwh", "ζωη")]
    [InlineData("fwv", "φως")]
    public void TheLettersThatAreEasyToGetWrong(string beta, string greek)
    {
        // `q` is theta and `y` is psi in the composite's alphabet — the other repository trades
        // them, which is what ScrivenerReader.Fold exists for. `h` is eta, not a breathing, and
        // `v` is a final sigma rather than a letter of its own.
        BetaCode.ToGreek(beta).Should().Be(greek);
    }

    [Fact]
    public void EveryLetterOfTheAlphabetIsMapped()
    {
        var converted = BetaCode.ToGreek("abgdezhqiklmnxoprsvtufcyw");

        converted.Should().Be("αβγδεζηθικλμνξοπρσςτυφχψω");
        converted.Should().NotMatchRegex("[a-z]", "a letter left behind is a word left unreadable");
    }

    [Fact]
    public void WhatIsNotTheAlphabetIsLeftAlone()
    {
        // 32 words in the loaded corpus are subscription lines and division markers that leaked out
        // of the source files. Turning those into Greek would hide them; they are meant to be seen.
        BetaCode.ToGreek("(23:14)").Should().Be("(23:14)");
        BetaCode.ToGreek("{N-DSM}").Should().Be("{N-DSM}");
        BetaCode.ToGreek("[prov").Should().Be("[προς");
    }

    [Fact]
    public void TheSqlArgumentsMatchTheConverter()
    {
        // The rows already loaded are repaired with SQL translate() using this pair. If the two
        // ever disagree, half the corpus is converted one way and half the other.
        var (from, to) = BetaCode.TranslateArguments;

        from.Length.Should().Be(to.Length);
        BetaCode.ToGreek(from).Should().Be(to);
    }
}
