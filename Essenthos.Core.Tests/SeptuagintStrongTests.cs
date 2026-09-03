using Essenthos.Core.Glaux;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// Strong numbers for a book Strong never numbered.
///
/// Every one of these is our reasoning — a GLAUx lemma looked up in a lexicon built for the New
/// Testament — so what a test can hold is that the reasoning says how good it is, and that a rule
/// about two citation conventions never displaces a match the two lists already agree on.
/// </summary>
public class SeptuagintStrongTests
{
    private static Dictionary<string, List<string>> Lexicon(params (string Lemma, string[] Numbers)[] rows) =>
        rows.ToDictionary(row => row.Lemma, row => row.Numbers.ToList(), StringComparer.Ordinal);

    [Fact]
    public void TakesTheNumberWhoseLemmaTheDictionaryWritesTheSameWay()
    {
        var (numbers, bridged) = SeptuagintStrongLoader.Numbers(
            Lexicon(("θεοσ", ["G2316"])), "θεοσ");

        numbers.Should().Equal("G2316");
        bridged.Should().BeFalse();
    }

    /// <summary>
    /// GLAUx cites *become* as γίγνομαι, Attic practice; the New Testament dictionary writes
    /// γίνομαι. One word, one number, and joining the lists as written drops the whole class.
    /// </summary>
    [Fact]
    public void ReachesAKoineEntryFromAnAtticCitationForm()
    {
        var (numbers, bridged) = SeptuagintStrongLoader.Numbers(
            Lexicon(("γινομαι", ["G1096"])), "γιγνομαι");

        numbers.Should().Equal("G1096");
        bridged.Should().BeTrue();
    }

    /// <summary>
    /// The bridge is consulted only where the lexicon has nothing, so a rewriting rule can never
    /// take a word away from an entry that already claims it as written.
    /// </summary>
    [Fact]
    public void PrefersTheUnchangedLemmaOverAnythingTheBridgeWouldRewriteItTo()
    {
        var (numbers, bridged) = SeptuagintStrongLoader.Numbers(
            Lexicon(("γιγνομαι", ["G9999"]), ("γινομαι", ["G1096"])), "γιγνομαι");

        numbers.Should().Equal("G9999");
        bridged.Should().BeFalse();
    }

    [Fact]
    public void ProposesEveryCandidateWhereTheLexiconClaimsALemmaTwice()
    {
        var (numbers, _) = SeptuagintStrongLoader.Numbers(
            Lexicon(("αυτου", ["G846", "G847"])), "αυτου");

        numbers.Should().HaveCount(2);
    }

    /// <summary>
    /// A lemma several entries claim is divided between them, so no candidate of an ambiguous word
    /// can ever be believed as much as a word the lexicon answers once.
    /// </summary>
    [Fact]
    public void DividesTheConfidenceAmongTheCandidates()
    {
        SeptuagintStrongLoader.Shared(1).Should().Be(SeptuagintStrongLoader.Single);
        SeptuagintStrongLoader.Shared(2).Should().BeLessThan(SeptuagintStrongLoader.Single);
        SeptuagintStrongLoader.Shared(3).Should().BeLessThan(SeptuagintStrongLoader.Shared(2));
    }

    /// <summary>
    /// A word whose lemma nothing answers reaches no number, and that is the correct answer far more
    /// often than it looks: Strong catalogued New Testament vocabulary, and the Septuagint has a
    /// great deal of its own.
    /// </summary>
    [Fact]
    public void ProposesNothingForALemmaNoEntryAnswers()
    {
        var (numbers, _) = SeptuagintStrongLoader.Numbers(Lexicon(("θεοσ", ["G2316"])), "φαραω");
        numbers.Should().BeEmpty();
    }
}
