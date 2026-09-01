using Essenthos.Core.Nestle;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// Tests for the Nestle 1904 word/trailer tokeniser. The source wraps punctuation inside the word
/// element — <c>&lt;w..&gt;Λόγος,&lt;/w&gt;</c> — and an off-by-one here silently removed the
/// final letter of 19,740 Greek words, which in Greek is the case ending.
/// </summary>
public class NestleParserTests
{
    [Theory]
    [InlineData("Λόγος,", "Λόγος", ", ")]
    [InlineData("Θεόν,", "Θεόν", ", ")]
    [InlineData("ἀνθρώπων.", "ἀνθρώπων", ". ")]
    [InlineData("αὐτοῦ·", "αὐτοῦ", "· ")]
    [InlineData("ἐγένετο;", "ἐγένετο", "; ")]
    public void ParseWordAndTrailer_TrailingPunctuation_KeepsTheWholeWord(string source, string word, string trailer)
    {
        var result = NestleParser.ParseWordAndTrailer(source);

        result.Word.Should().Be(word);
        result.Trailer.Should().Be(trailer);
    }

    [Theory]
    [InlineData("Λόγος")]
    [InlineData("ἀρχῇ")]
    public void ParseWordAndTrailer_NoPunctuation_IsUnchangedAndSpaced(string source)
    {
        var result = NestleParser.ParseWordAndTrailer(source);

        result.Word.Should().Be(source);
        result.Trailer.Should().Be(" ");
    }

    [Fact]
    public void ParseWordAndTrailer_SeveralPunctuationMarks_AllGoToTheTrailer()
    {
        var result = NestleParser.ParseWordAndTrailer("Λόγος).");

        result.Word.Should().Be("Λόγος");
        result.Trailer.Should().Be("). ");
    }

    [Fact]
    public void ParseWordAndTrailer_NeverLosesACharacter()
    {
        const string source = "Θεόν,";

        var result = NestleParser.ParseWordAndTrailer(source);

        (result.Word + result.Trailer.TrimEnd()).Should().Be(source);
    }
}
