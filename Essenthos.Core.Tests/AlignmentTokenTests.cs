using Essenthos.Core.Loading.Links;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// The one-word-one-token rule, which is the whole of what the aligner's indices mean. Every case
/// here shifted 4,804 verses in the corpus before it was checked, or would have.
/// </summary>
public class AlignmentTokenTests
{
    [Theory]
    [InlineData("אור")]
    [InlineData("beginning")]
    [InlineData("ἀρχῇ")]
    public void AnOrdinaryWordIsItsOwnToken(string word) => AlignmentTokens.One(word).Should().Be(word);

    /// <summary>
    /// The article in לָאוֹר is a vowel on the preposition and has no consonant of its own. It is a
    /// word in BHSA, it is reached by links, and it has nothing to write.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("   ")]
    public void AWordWithNothingWritableStillWritesOneToken(string? form)
    {
        var token = AlignmentTokens.One(form);

        token.Should().NotBeEmpty();
        token.Should().NotContainAny(" ", "\t");
    }

    /// <summary>Two tokens shift the verse exactly as badly as none.</summary>
    [Fact]
    public void AWordWithASpaceInItIsStillOneToken() =>
        AlignmentTokens.One("son of man").Split(' ').Should().ContainSingle();

    [Fact]
    public void AVerseWritesOneTokenPerWord() =>
        AlignmentTokens.Line(["in", "beginning", "create"], "1 1:1").Should().Be("in beginning create");

    /// <summary>
    /// The failure this exists to make impossible: the line reads correctly, the tool accepts it,
    /// and every index it returns past the missing word is somebody else's.
    /// </summary>
    [Fact]
    public void AVerseThatWouldShiftIsRefusedAndSaysWhere()
    {
        var shifted = () => AlignmentTokens.Line(["to", string.Empty, "light"], "1 1:5");

        shifted.Should().Throw<InvalidOperationException>()
            .WithMessage("*1 1:5*3 words*2 tokens*");
    }

    [Fact]
    public void AVerseIsCheckedWhicheverSideTheExtraTokenComesFrom()
    {
        var split = () => AlignmentTokens.Line(["son of man"], "26 2:1");

        split.Should().Throw<InvalidOperationException>().WithMessage("*1 words*3 tokens*");
    }
}
