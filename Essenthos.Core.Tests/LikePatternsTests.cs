using Essenthos.Core.Endpoints;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

public class LikePatternsTests
{
    [Fact]
    public void OrdinaryTextIsWrappedUntouched()
    {
        LikePatterns.Containing("mercy").Should().Be("%mercy%");
        LikePatterns.Exactly("the").Should().Be("the");
    }

    [Theory]
    [InlineData("%", "%\\%%")]
    [InlineData("_", "%\\_%")]
    [InlineData("a_b%c", "%a\\_b\\%c%")]
    public void WildcardsTypedByTheCallerAreMatchedAsCharacters(string query, string pattern)
    {
        LikePatterns.Containing(query).Should().Be(pattern);
    }

    [Fact]
    public void TheEscapeCharacterItselfIsEscaped()
    {
        LikePatterns.Containing("\\").Should().Be("%\\\\%");
        LikePatterns.Exactly("\\%").Should().Be("\\\\\\%");
    }
}
