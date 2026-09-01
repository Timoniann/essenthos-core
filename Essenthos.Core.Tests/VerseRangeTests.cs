using Essenthos.Core.Endpoints;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

public class VerseRangeTests
{
    [Theory]
    [InlineData("3", 3, 3)]
    [InlineData("3-7", 3, 7)]
    [InlineData(" 3 - 7 ", 3, 7)]
    [InlineData("5-5", 5, 5)]
    public void ParsesASingleVerseOrARange(string value, int expectedFrom, int expectedTo)
    {
        VerseRange.TryParse(value, out var from, out var to).Should().BeTrue();
        from.Should().Be(expectedFrom);
        to.Should().Be(expectedTo);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("7-3")]
    [InlineData("0")]
    [InlineData("0-4")]
    [InlineData("a-b")]
    [InlineData("1-2-3")]
    public void RejectsAnythingThatIsNotAForwardRange(string? value)
    {
        VerseRange.TryParse(value, out _, out _).Should().BeFalse();
    }

    [Fact]
    public void FormatHintNamesTheExpectedForm()
    {
        VerseRange.FormatHint("7-3").Should().Contain("7-3").And.Contain("3-7");
    }
}
