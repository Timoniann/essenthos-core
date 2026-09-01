using Essenthos.Core.Utils;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// Tests for <see cref="WordSeparation"/>. A verse is rebuilt by concatenating each word's text
/// and its trailer, so a trailer that loses the space after its punctuation produces
/// "their sister,and her nurse".
/// </summary>
public class WordSeparationTests
{
    [Theory]
    [InlineData(",", ", ")]
    [InlineData(".", ". ")]
    [InlineData(";", "; ")]
    [InlineData(":", ": ")]
    [InlineData("?", "? ")]
    [InlineData("!", "! ")]
    [InlineData(")", ") ")]
    [InlineData("", " ")]
    public void EnsureSeparator_PunctuationAndNothing_GainASpace(string trailer, string expected)
    {
        WordSeparation.EnsureSeparator(trailer).Should().Be(expected);
    }

    [Theory]
    [InlineData(" ")]
    [InlineData(", ")]
    [InlineData(". ")]
    public void EnsureSeparator_AlreadySeparated_IsLeftAlone(string trailer)
    {
        WordSeparation.EnsureSeparator(trailer).Should().Be(trailer);
    }

    [Theory]
    [InlineData(" (")]
    [InlineData("(")]
    [InlineData(", (")]
    [InlineData("[")]
    [InlineData("«")]
    [InlineData("“")]
    public void EnsureSeparator_OpeningPunctuation_IsLeftAlone(string trailer)
    {
        WordSeparation.EnsureSeparator(trailer).Should().Be(trailer);
    }

    [Fact]
    public void EnsureSeparator_IsIdempotent()
    {
        var once = WordSeparation.EnsureSeparator(",");
        WordSeparation.EnsureSeparator(once).Should().Be(once);
    }

    [Theory]
    [InlineData("", "")]
    [InlineData(" ", " ")]
    [InlineData(", ", ", ")]
    [InlineData(",\n   ", ", ")]
    [InlineData("  ", " ")]
    [InlineData("\t", " ")]
    [InlineData(".\r\n", ". ")]
    public void NormalizeWhitespace_CollapsesRunsToOneSpace(string trailer, string expected)
    {
        WordSeparation.NormalizeWhitespace(trailer).Should().Be(expected);
    }

    [Fact]
    public void NormalizeWhitespace_KeepsPunctuationOrder()
    {
        WordSeparation.NormalizeWhitespace(" — ").Should().Be(" — ");
    }
}
