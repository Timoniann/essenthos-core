using Essenthos.Core.Products;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Products;

public class WordKeyTests
{
    private static readonly WordKey Context = WordKey.Parse("KJV 1.1:1.1");

    [Fact]
    public void TheFullKeyIsTextAddressAndPosition()
    {
        var key = WordKey.Parse("KJV 1.1:1.3");

        key.Should().Be(new WordKey("KJV", new VerseKey(1, 1, 1), 3));
        key.ToString().Should().Be("KJV 1.1:1.3");
        key.Own.Should().Be("1.1:1.3");
    }

    [Theory]
    [InlineData("KJV 2.3:4.5", "KJV 2.3:4.5")]
    [InlineData("2.3:4.5", "KJV 2.3:4.5")]
    [InlineData("3:4.5", "KJV 1.3:4.5")]
    [InlineData("4.5", "KJV 1.1:4.5")]
    [InlineData(".5", "KJV 1.1:1.5")]
    [InlineData("31:50a.2", "KJV 1.31:50a.2")]
    [InlineData("4a:1.2", "KJV 1.4a:1.2")]
    [InlineData("50a.2", "KJV 1.1:50a.2")]
    [InlineData("1-10.7", "KJV 1.1:1-10.7")]
    public void EachShapeIsToldByItsPunctuationAndFilledFromTheContext(string text, string full)
    {
        WordKey.Parse(text, Context).ToString().Should().Be(full);
    }

    [Fact]
    public void AnotherTextsKeyInContextResetsTheAddressSoNothingOfTheContextLeaksIn()
    {
        var parse = () => WordKey.Parse("NESTLE1904 .5", Context);

        parse.Should().Throw<FormatException>("the other text's verse is not the context's");
    }

    [Theory]
    [InlineData("KJV 1.1:1.3", "KJV", ".3")]
    [InlineData("KJV 1.1:1.3", "KJV", "1.1:1.3")]
    [InlineData("KJV 1.1:2.3", "KJV", "2.3")]
    public void InsideAFileOfOneTextOnlyTheVerseAndPositionAreNeeded(string full, string text, string written)
    {
        var expected = WordKey.Parse(full);
        if (written.StartsWith('.'))
        {
            var parse = () => WordKey.Parse(written, text);
            parse.Should().Throw<FormatException>("a file of one text states no verse, so a position alone names nothing");
            WordKey.Parse(written, new WordKey(text, expected.Verse, 1)).Should().Be(expected);
        }
        else if (written.Contains(':'))
        {
            WordKey.Parse(written, text).Should().Be(expected);
        }
        else
        {
            var parse = () => WordKey.Parse(written, text);
            parse.Should().Throw<FormatException>();
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("KJV")]
    [InlineData("KJV 1.1:1")]
    [InlineData("KJV 1.1:1.0")]
    [InlineData("kjv 1.1:1.3")]
    [InlineData("KJV  1.1:1.3")]
    [InlineData("KJV 1.1:1.3 ")]
    [InlineData("KJV 1.1:1.-3")]
    [InlineData("1.1:1.3")]
    [InlineData(".3")]
    public void RefusesWhatIsNotAWordKeyWithoutContext(string text)
    {
        WordKey.TryParse(text, null, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("KJV 1.1:1.3", "KJV 1.1:1.1", ".3")]
    [InlineData("KJV 1.1:2.3", "KJV 1.1:1.1", "2.3")]
    [InlineData("KJV 1.31:50a.3", "KJV 1.1:1.1", "31:50a.3")]
    [InlineData("KJV 2.1:1.3", "KJV 1.1:1.1", "2.1:1.3")]
    [InlineData("KJV 1.1:1.3", "BHSA 1.1:1.1", "KJV 1.1:1.3")]
    [InlineData("KJV 19.4a:1.3", "KJV 19.4:1.1", "4a:1.3")]
    public void TheShortestFormIsWhatTheContextLeavesUnsaid(string full, string context, string shortest)
    {
        var key = WordKey.Parse(full);
        var standing = WordKey.Parse(context);

        key.ToString(standing).Should().Be(shortest);
        WordKey.Parse(shortest, standing).Should().Be(key, "the short form reads back to the key");
    }

    [Fact]
    public void OrdersByTextThenAddressThenPosition()
    {
        var keys = new[] { "KJV 1.1:1.10", "KJV 1.1:1.2", "BHSA 1.1:1.1", "KJV 1.1:2.1", "KJV 1.1:1a.1" }
            .Select(text => WordKey.Parse(text))
            .Order()
            .Select(key => key.ToString());

        keys.Should().Equal("BHSA 1.1:1.1", "KJV 1.1:1.2", "KJV 1.1:1.10", "KJV 1.1:1a.1", "KJV 1.1:2.1");
    }
}
