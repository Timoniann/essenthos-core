using Essenthos.Core.Loading;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// The property itself, shown to catch the two corruptions it exists for. A check that passes
/// whatever it is given is worse than no check, so each of these feeds it output that is wrong in
/// the way the corpus was actually wrong.
/// </summary>
public class VerseRoundTripTests
{
    private const string Source = "In the beginning God created the heaven, and the earth.";

    private static readonly (string Surface, string Trailer)[] Correct =
    [
        ("In", " "), ("the", " "), ("beginning", " "), ("God", " "), ("created", " "),
        ("the", " "), ("heaven", ", "), ("and", " "), ("the", " "), ("earth", "."),
    ];

    [Fact]
    public void WordsThatReproduceTheSourceRoundTrip()
    {
        Check(Correct).Should().BeNull();
    }

    /// <summary>
    /// The Greek tokeniser sliced before the last letter instead of after it, so every word
    /// followed by punctuation lost its case ending — 19,740 of them.
    /// </summary>
    [Fact]
    public void AWordMissingItsLastLetterIsCaught()
    {
        var corrupted = Correct.ToArray();
        corrupted[6] = ("heave", ", ");

        var failure = Check(corrupted);

        failure.Should().NotBeNull();
        failure!.Describe().Should().Contain("Genesis 1:1");
    }

    /// <summary>
    /// Trailers lost the space that followed their punctuation, and the verse read
    /// "heaven,and the earth".
    /// </summary>
    [Fact]
    public void ATrailerMissingTheSpaceAfterItsPunctuationIsCaught()
    {
        var corrupted = Correct.ToArray();
        corrupted[6] = ("heaven", ",");

        Check(corrupted).Should().NotBeNull();
    }

    /// <summary>
    /// Collapsing whitespace is what lets an indented source file through. Deleting it is not, and
    /// the difference is the whole of the trailer corruption.
    /// </summary>
    [Fact]
    public void CollapsingWhitespaceForgivesIndentationButNotAMissingSpace()
    {
        var indented = Source.Replace("and", "\n        and");

        VerseRoundTrip.Check("Genesis 1:1", Rebuild(Correct), indented, RoundTripTolerance.CollapsingWhitespace)
            .Should().BeNull();

        var corrupted = Correct.ToArray();
        corrupted[6] = ("heaven", ",");
        VerseRoundTrip.Check("Genesis 1:1", Rebuild(corrupted), indented, RoundTripTolerance.CollapsingWhitespace)
            .Should().NotBeNull();
    }

    [Fact]
    public void AWordDroppedAltogetherIsCaught()
    {
        Check(Correct.Where(w => w.Surface != "God").ToArray()).Should().NotBeNull();
    }

    [Fact]
    public void TheOffsetPointsAtTheFirstCharacterThatDiffers()
    {
        var corrupted = Correct.ToArray();
        corrupted[0] = ("It", " ");

        Check(corrupted)!.FirstDifference.Should().Be(1);
    }

    /// <summary>A verse rebuilt as a longer string still reports an offset inside both.</summary>
    [Fact]
    public void ARepeatedWordIsCaughtAtTheEnd()
    {
        var failure = Check([.. Correct, ("earth", ".")]);

        failure.Should().NotBeNull();
        failure!.FirstDifference.Should().Be(Source.Length);
    }

    [Fact]
    public void TheMessageShowsBothTextsAroundTheDifference()
    {
        var corrupted = Correct.ToArray();
        corrupted[6] = ("heave", ", ");

        var description = Check(corrupted)!.Describe();

        description.Should().Contain("source:").And.Contain("words:");
        description.Should().Contain("heaven,").And.Contain("heave,");
    }

    private static RoundTripFailure? Check((string Surface, string Trailer)[] words) =>
        VerseRoundTrip.Check("Genesis 1:1", Rebuild(words), Source);

    private static string Rebuild((string Surface, string Trailer)[] words) =>
        VerseRoundTrip.Rebuild(words, w => w.Surface, w => w.Trailer);
}
