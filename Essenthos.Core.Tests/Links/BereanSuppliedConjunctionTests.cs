using Essenthos.Core.Berean;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Links;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Links;

/// <summary>
/// A Berean row of a conjunction and bracketed words on a word without a conjunction of its own: the bracketed words render
/// the word and the conjunction renders the ו beside it, which BHSA writes as a word of its own.
/// </summary>
public class BereanSuppliedConjunctionTests
{
    private const long Waw = 10;

    /// <summary>Joshua 3:6: וַיֹּאמֶר «said», יְהוֹשֻׁעַ «And [he]».</summary>
    [Fact]
    public void TheConjunctionRendersTheWawOfTheVerbBeforeAndTheBracketedWordRendersTheName()
    {
        var rows = new[]
        {
            Row(1, 2, " said ", "Conjunctive waw | Verb - Qal - Consecutive imperfect - third person masculine singular"),
            Row(2, 1, " And [he] ", "Noun - proper - masculine singular"),
            Row(3, 3, " to ", "Preposition"),
        };
        var silences = new BereanLinks.Silences();

        var drafts = Pair(rows, [[Waw, 11], [12], [13]], "And he said to", silences);

        drafts.Select(Shape).Should().BeEquivalentTo("100>10", "101>12 bracketed", "102>10,11", "103>13");
        (silences.ConjunctionsMoved, silences.ConjunctionsUnlinked).Should().Be((1, 0));
    }

    /// <summary>
    /// The row's word has its own ו, is a preposition, or is the conjunction: the conjunction renders it, and the bracketed
    /// word is left to the rule for supplied words. Exodus 10:10 gives וַיֹּאמֶר «Then [Pharaoh]».
    /// </summary>
    [Theory]
    [InlineData("Conjunctive waw | Verb - Qal - Consecutive imperfect - third person masculine singular")]
    [InlineData("Preposition | third person masculine singular")]
    [InlineData("Conjunction")]
    public void ARowWhoseWordHasItsOwnConjunctionOrIsAPrepositionIsUnchanged(string parsing)
    {
        var rows = new[]
        {
            Row(1, 1, " Then [Pharaoh] ", parsing),
            Row(2, 2, " told them ", "Preposition | third person masculine plural"),
        };
        var silences = new BereanLinks.Silences();

        var drafts = Pair(rows, [[Waw, 11], [12]], "Then Pharaoh told them", silences);

        drafts.Select(Shape).Should().BeEquivalentTo("100,101>10,11", "102,103>12");
        (silences.ConjunctionsMoved, silences.ConjunctionsUnlinked).Should().Be((0, 0));
    }

    /// <summary>
    /// No ו beside the word can be told, so the conjunction is left unlinked. Numbers 5:19: אֹתָהּ before הַכֹּהֵן «And [he]» has
    /// no ו, and the וְאָמַר after it has its own «and». «For» renders a preposition or כִּי, never a ו (2 Samuel 13:2).
    /// </summary>
    [Theory]
    [InlineData(" And [he] ", " and say ")]
    [InlineData(" for [him] ", " say ")]
    public void AConjunctionWithNoWawBesideItThatCanBeToldIsLeftUnlinked(string row, string after)
    {
        var rows = new[]
        {
            Row(1, 1, " - ", "Direct object marker | third person feminine singular"),
            Row(2, 2, row, "Article | Noun - masculine singular"),
            Row(3, 3, after, "Conjunctive waw | Verb - Qal - Conjunctive perfect - third person masculine singular"),
        };
        var silences = new BereanLinks.Silences();
        var english = string.Join(' ', BereanWords.Rendering(row).Concat(BereanWords.Rendering(after)));

        var drafts = Pair(rows, [[11], [12, 13], [Waw, 14]], english, silences);

        drafts.Should().NotContain(draft => draft.From.Contains(100));
        drafts.Should().ContainSingle(draft => draft.Bracketed).Which.Should().Match<BereanLinks.Draft>(
            draft => draft.From.SequenceEqual(new long[] { 101 }) && draft.To.SequenceEqual(new long[] { 12, 13 }));
        (silences.ConjunctionsMoved, silences.ConjunctionsUnlinked).Should().Be((0, 1));
    }

    private static string Shape(BereanLinks.Draft draft) =>
        $"{string.Join(',', draft.From)}>{string.Join(',', draft.To)}{(draft.Bracketed ? " bracketed" : string.Empty)}"
        + (draft.Relation == LinkRelation.Renders ? string.Empty : " omits");

    private static List<BereanLinks.Draft> Pair(BereanRow[] rows, List<List<long>> witness, string english, BereanLinks.Silences silences)
    {
        var ours = english.Split(' ').Select((word, at) => new BereanLinks.Word(100 + at, 1, word, null)).ToList();
        var drafts = new List<BereanLinks.Draft>();

        BereanLinks.Pair(rows, ours, witness, drafts, silences, conjunctions: new HashSet<long> { Waw }).Should().BeTrue();
        return drafts;
    }

    private static BereanRow Row(double order, int english, string rendering, string parsing) =>
        new(order, english, 1, "Hebrew", "א", "א", string.Empty, "Joshua 3:6", rendering, parsing);
}
