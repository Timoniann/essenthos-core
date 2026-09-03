using Essenthos.Core.Glaux;
using Essenthos.Core.TextusReceptus;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// GLAUx cites Greek the way a classicist does and Nestle 1904 the way a New Testament editor does.
/// Joining the two lemma lists as written loses 6,849 Septuagint tokens — 1.19% of Brenton — on
/// words that are not in dispute at all, γίγνομαι against γίνομαι being the largest.
///
/// The risk is the mirror of the stemmer's: a bridge that rewrites too freely merges two lexemes,
/// and a Strong number attached to the wrong one is worse than none. So the unchanged lemma is
/// always offered first, and both halves are tested.
/// </summary>
public class GreekLemmaBridgeTests
{
    [Fact]
    public void OffersTheLemmaItselfBeforeAnyRewriting()
    {
        GreekLemmaBridge.Candidates("γίγνομαι").First().Should().Be("γιγνομαι");
    }

    [Theory]
    [InlineData("γίγνομαι", "γινομαι")]
    [InlineData("γιγνώσκω", "γινωσκω")]
    [InlineData("πορεύομαι", "πορευω")]
    [InlineData("ἐντέλλομαι", "εντελλω")]
    [InlineData("ἀποκρίνομαι", "αποκρινω")]
    [InlineData("ὀμνύω", "ομνυομαι")]
    [InlineData("οὕτως", "ουτω")]
    public void ReachesTheFormTheOtherConventionWrites(string glaux, string nestle)
    {
        GreekLemmaBridge.Candidates(glaux).Should().Contain(nestle);
    }

    /// <summary>
    /// Nothing may be offered that is not the same word. λόγος is a noun and has no middle voice;
    /// the verb rules must not fire on it.
    /// </summary>
    [Theory]
    [InlineData("λόγος")]
    [InlineData("κριτής")]
    [InlineData("Ἀβραάμ")]
    public void LeavesAWordWithNoOtherConventionAlone(string lemma)
    {
        GreekLemmaBridge.Candidates(lemma).Should().ContainSingle().Which.Should().Be(
            GreekLetters.Bare(lemma));
    }

    /// <summary>
    /// GLAUx tokenises punctuation as its own element and <see cref="GlauxReader"/> drops it, but a
    /// lemma that folds away to nothing must not become an empty key that every other empty key
    /// then collides with.
    /// </summary>
    [Fact]
    public void OffersNothingForALemmaThatFoldsAwayToNothing()
    {
        GreekLemmaBridge.Candidates("́").Should().BeEmpty();
    }
}
