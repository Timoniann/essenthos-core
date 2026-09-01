using Essenthos.Core.Strong;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// Tests for <see cref="StrongNumbers"/> and <see cref="StrongMorphemeCodes"/>. Strong's numbers
/// arrive from clients in whatever form the source they copied used, and the corpus stores one.
/// </summary>
public class StrongNumbersTests
{
    [Theory]
    [InlineData("H430", "H430")]
    [InlineData("h430", "H430")]
    [InlineData("H0430", "H430")]
    [InlineData("  G26  ", "G26")]
    [InlineData("g0026", "G26")]
    public void Normalize_AcceptedForms_ProduceCanonicalNumber(string input, string expected)
    {
        StrongNumbers.Normalize(input).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("430")]
    [InlineData("X430")]
    [InlineData("H")]
    [InlineData("H0")]
    [InlineData("H-5")]
    [InlineData("H43a")]
    public void Normalize_Rejected_ReturnsNull(string? input)
    {
        StrongNumbers.Normalize(input).Should().BeNull();
    }

    [Fact]
    public void FormatHint_NamesTheExpectedForm()
    {
        StrongNumbers.FormatHint("430").Should().Contain("H430").And.Contain("G26");
    }

    [Theory]
    [InlineData("H9000")]
    [InlineData("H9009")]
    [InlineData("H9042")]
    public void IsMorphemeCode_HebrewMorphemeRange_IsRecognised(string strongNumber)
    {
        StrongMorphemeCodes.IsMorphemeCode(strongNumber).Should().BeTrue();
        StrongMorphemeCodes.GetDescription(strongNumber).Should().NotBeNullOrEmpty();
    }

    [Theory]
    [InlineData("H430")]
    [InlineData("G26")]
    [InlineData("G9000")]
    [InlineData("H8999")]
    [InlineData("H9100")]
    public void IsMorphemeCode_EverythingElse_IsNot(string strongNumber)
    {
        StrongMorphemeCodes.IsMorphemeCode(strongNumber).Should().BeFalse();
        StrongMorphemeCodes.GetDescription(strongNumber).Should().BeNull();
    }

    [Fact]
    public void GetDescription_ObservedCode_NamesTheMorpheme()
    {
        StrongMorphemeCodes.GetDescription("H9009").Should().Contain("article");
        StrongMorphemeCodes.GetDescription("H9000").Should().Contain("conjunction");
    }
}
