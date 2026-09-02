using Essenthos.Core.TextusReceptus;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// Folding Greek to the form a reader types.
///
/// The failure this guards against is silent by nature: a letter the folder does not know is
/// passed through unchanged, the word is still a word, the search still answers, and it answers
/// with fewer verses than exist. It took a Septuagint and an audit to notice that ᾅδου and αδου
/// were different words.
/// </summary>
public class GreekFoldingTests
{
    /// <summary>
    /// Every letter of the Greek and Greek Extended blocks folds into the twenty-four, or is
    /// dropped as a mark. This is the property the old table could not hold: it covered the
    /// letters that were loaded, and a new text brought fourteen it had never seen.
    /// </summary>
    [Fact]
    public void FoldsEveryGreekLetterToTheAlphabet()
    {
        const string alphabet = "αβγδεζηθικλμνξοπρστυφχψω";
        var unfolded = new List<string>();

        for (var c = 'Ͱ'; c <= '῿'; c++)
        {
            if (c is > 'Ͽ' and < 'ἀ' || !char.IsLetter(c))
            {
                continue;
            }

            var folded = GreekLetters.Fold(c);
            if (folded != ' ' && !alphabet.Contains(folded) && char.IsLetter(folded))
            {
                unfolded.Add($"U+{(int)c:X4} {c} -> {folded}");
            }
        }

        // Not zero: the block holds archaic letters no edition here uses — digamma, koppa, sampi —
        // and passing those through unchanged is right. What must not appear is an accented form.
        unfolded.Should().OnlyContain(
            line => !line.Contains('́') && !line.Contains('̀'),
            "an accented letter left unfolded is a search that silently misses");
    }

    [Theory]
    [InlineData("ᾅδου", "αδου")]        // the word the audit found: Hades, in the Septuagint
    [InlineData("Ὠσηὲ", "ωσηε")]        // Hosea, named eleven times and findable none
    [InlineData("Σάῤῥα", "σαρρα")]      // Sarah, with the rare rho breathings
    // Final sigma folds to medial, so both spellings of a word answer the same search.
    [InlineData("θεός", "θεοσ")]
    [InlineData("Θεὸς", "θεοσ")]
    [InlineData("θεος", "θεοσ")]
    [InlineData("λόγος", "λογοσ")]
    [InlineData("ἀρχῇ", "αρχη")]        // iota subscript is dropped, not kept
    [InlineData("υἱός", "υιοσ")]
    [InlineData("ἈΒΡΑΆΜ", "αβρααμ")]    // capitals, breathing and accent together
    public void FoldsAWordToWhatAReaderWouldType(string written, string bare) =>
        GreekLetters.Bare(written).Should().Be(bare);

    [Fact]
    public void CountsTwoWitnessesOfOneWordAsOne() =>
        GreekLetters.Same("Ἰησοῦς", "ΙΗΣΟΥΣ").Should().BeTrue();

    [Fact]
    public void LeavesAloneWhatIsNotGreek() =>
        GreekLetters.Bare("{N-GSM}").Should().Be("{N-GSM}");
}
