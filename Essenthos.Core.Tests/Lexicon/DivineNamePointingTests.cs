using Essenthos.Core.Conversion;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Lexicon;

/// <summary>
/// BHSA's divine name takes its Strong number from its pointing: H3069 where the vav carries the hireq of Elohim's
/// vowels, H3068 everywhere else, whatever the Open Hebrew Bible's file gave the word.
/// </summary>
public class DivineNamePointingTests
{
    [Theory]
    [InlineData("יְהוָ֑ה", "H3068")] // Lev 1:11, pointed to be read Adonai
    [InlineData("יהוָ֑ה", "H3068")]
    [InlineData("יְהֹוָה", "H3068")]
    [InlineData("יֱהוִה֙", "H3069")] // Gen 15:2, after אֲדֹנָי
    [InlineData("יְהוִ֗ה", "H3069")]
    [InlineData("יְהֹוִ֣ה", "H3069")] // Ps 140:8, before אֲדֹנָי
    [InlineData("יְה֘וִ֤ה", "H3069")]
    public void TheNameIsNumberedByItsPointing(string surface, string expected) =>
        BhsaStrongNumbers.DivineName("יְהוָה", surface).Should().Be(expected);

    [Fact]
    public void OtherLemmasAreLeftToTheFile()
    {
        BhsaStrongNumbers.DivineName("אֲדֹנָי", "אֲדֹנָ֣י").Should().BeNull();
        BhsaStrongNumbers.DivineName(null, "יְהוָ֑ה").Should().BeNull();
    }

    [Fact]
    public void TheRelativeParticleIsAlwaysItsOwnNumber()
    {
        BhsaStrongNumbers.Relative("שַׁ").Should().Be("H7945");
        BhsaStrongNumbers.Relative("אֲשֶׁר").Should().BeNull();
        BhsaStrongNumbers.Relative(null).Should().BeNull();
    }
}
