using Essenthos.Core.Products;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Products;

public class VerseKeyTests
{
    [Theory]
    [InlineData("1.1:1", 1, 1, "", 1, "")]
    [InlineData("1.31:50a", 1, 31, "", 50, "a")]
    [InlineData("19.4a:1", 19, 4, "a", 1, "")]
    [InlineData("19.4b:12-14", 19, 4, "b", 12, "-14")]
    [InlineData("93.151:7", 93, 151, "", 7, "")]
    [InlineData("3.1:5a1", 3, 1, "", 5, "a1")]
    [InlineData("3.1:5I", 3, 1, "", 5, "I")]
    [InlineData("19.3:0", 19, 3, "", 0, "")]
    public void ParsesTheEditionsOwnAddressWithItsLabels(string text, int book, int chapter, string chapterLabel, int verse, string label)
    {
        var key = VerseKey.Parse(text);

        key.Should().Be(new VerseKey(book, chapter, chapterLabel, verse, label));
        key.ToString().Should().Be(text, "formatting is the inverse of parsing");
    }

    [Theory]
    [InlineData("")]
    [InlineData("1:1")]
    [InlineData("1.1")]
    [InlineData("0.1:1")]
    [InlineData("1.1:1.3")]
    [InlineData("1.a:1")]
    [InlineData("1.1:a")]
    [InlineData("1.1:1 ")]
    [InlineData("1.1:1.")]
    [InlineData("1.1:1a.b")]
    [InlineData("1.1:1_a")]
    [InlineData("-1.1:1")]
    [InlineData("1.1:1\t")]
    public void RefusesWhatIsNotAVerseKey(string text)
    {
        VerseKey.TryParse(text, out _).Should().BeFalse();
        var parse = () => VerseKey.Parse(text);
        parse.Should().Throw<FormatException>().WithMessage($"*{text.Trim()}*");
    }

    [Fact]
    public void OrdersByBookChapterLabelVerseLabelWithThePlainOneFirst()
    {
        var keys = new[] { "2.1:1", "1.31:50a", "1.31:50", "1.31:51", "19.4a:1", "19.4:9", "19.5:1", "1.31:50b" }
            .Select(text => VerseKey.Parse(text))
            .Order()
            .Select(key => key.ToString());

        keys.Should().Equal("1.31:50", "1.31:50a", "1.31:50b", "1.31:51", "2.1:1", "19.4:9", "19.4a:1", "19.5:1");
    }

    [Fact]
    public void TheShortFormsDropWhatTheContextStates()
    {
        var key = VerseKey.Parse("19.4a:12b");

        key.ChapterAndVerse.Should().Be("4a:12b");
        key.VerseOnly.Should().Be("12b");
    }

    [Fact]
    public void ALabelNeverStartsWithADigitSoTheNumberEndsWhereTheDigitsDo()
    {
        VerseKey.IsLabel("").Should().BeTrue();
        VerseKey.IsLabel("a").Should().BeTrue();
        VerseKey.IsLabel("-10").Should().BeTrue();
        VerseKey.IsLabel("a1").Should().BeTrue();
        VerseKey.IsLabel("1a").Should().BeFalse();
        VerseKey.IsLabel("a b").Should().BeFalse();
        VerseKey.IsLabel("a.b").Should().BeFalse();
        VerseKey.IsLabel("a:b").Should().BeFalse();
        VerseKey.IsLabel("α").Should().BeFalse("a label is ASCII so every tool sorts and greps it alike");
    }
}
