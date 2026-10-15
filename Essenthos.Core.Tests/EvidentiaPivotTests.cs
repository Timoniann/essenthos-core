using Essenthos.Core.Loading.Links.Evidentia;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// Another text of the same language, whose own links are stated, is read word by word: a word that shares
/// a lemma with one of its words in the same verse is told which original word that one renders. It is a
/// second opinion on a placement and never a placement of its own.
/// </summary>
public class EvidentiaPivotTests
{
    private static readonly EvidentiaAddress Genesis1 = new(1, 1, 24);

    private static readonly LanguagePackRegistry Packs = new([new EnglishLanguagePack(), new OriginalLanguagePack()]);

    private static readonly EvidentiaAnalysis Livestock = Analysis(English(1, 1, "livestock"));
    private static readonly EvidentiaAnalysis Beast = Analysis(Hebrew(11, 1, "בְּהֵמָה", "H929"));
    private static readonly EvidentiaAnalysis Herd = Analysis(Hebrew(12, 2, "מִקְנֶה", "H4735"));

    [Fact]
    public void AWordTakesTheStatedTargetOfThePivotWordWithItsLemma()
    {
        var pivot = Pivot(("KJV", [Analysis(English(101, 3, "livestock"))], [(101L, 11L)]));

        pivot.Of(1).Should().ContainKey("KJV").WhoseValue.Should().Equal(11L);
        pivot.Agrees(1, 11).Should().BeTrue();
        pivot.Contradicts(1, 12).Should().BeTrue();
        pivot.Contradicts(2, 12).Should().BeFalse("the pivot says nothing of a word it does not match");
    }

    [Fact]
    public void ALemmaTheVersesWriteADifferentNumberOfTimesIsNotMatched()
    {
        var second = Analysis(English(2, 4, "livestock"));
        var pivot = EvidentiaPivot.Match(
            [Livestock, second],
            [new EvidentiaPivotText("KJV", [Analysis(English(101, 3, "livestock"))], Linked((101L, 11L)))]);

        pivot.IsEmpty.Should().BeTrue();

        var inOrder = EvidentiaPivot.Match(
            [Livestock, second],
            [new EvidentiaPivotText("KJV", [Analysis(English(101, 3, "livestock")), Analysis(English(102, 7, "livestock"))],
                Linked((101L, 11L), (102L, 12L)))]);
        inOrder.Named(1).Should().Equal(11L);
        inOrder.Named(2).Should().Equal(12L);
    }

    [Fact]
    public void APlacementEveryPivotContradictsIsWithheldOnlyWhenThePolicySaysSo()
    {
        var pivot = Pivot(("KJV", [Analysis(English(101, 3, "livestock"))], [(101L, 11L)]));
        var placement = new EvidentiaProposal(Livestock, Herd, EvidentiaProposalKind.GlobalReviewKnownRendering, 0.5);

        EvidentiaPivotCheck.Contradicted([placement], pivot, EvidentiaPivotPolicy.Withhold).Should().Equal((1L, 12L));
        EvidentiaPivotCheck.Contradicted([placement], pivot, EvidentiaPivotPolicy.Safe).Should().BeEmpty();
        EvidentiaPivotCheck.Contradicted([placement with { Target = Beast }], pivot, EvidentiaPivotPolicy.Withhold).Should().BeEmpty();
    }

    private static EvidentiaPivot Pivot(params (string Slug, EvidentiaAnalysis[] Words, (long, long)[] Links)[] texts) =>
        EvidentiaPivot.Match([Livestock], texts.Select(text => new EvidentiaPivotText(text.Slug, text.Words, Linked(text.Links))));

    private static Dictionary<long, HashSet<long>> Linked(params (long From, long To)[] links) =>
        links.GroupBy(link => link.From).ToDictionary(group => group.Key, group => group.Select(link => link.To).ToHashSet());

    private static EvidentiaAnalysis Analysis(EvidentiaToken token)
    {
        Packs.TryAnalyse(token, out var analysis).Should().BeTrue();
        return analysis;
    }

    private static EvidentiaToken English(long id, int position, string surface) =>
        new(id, Genesis1, position, surface, "eng", PartOfSpeech: "NOUN");

    private static EvidentiaToken Hebrew(long id, int position, string surface, string strongNumber) =>
        new(id, Genesis1, position, surface, "hbo", StrongNumber: strongNumber, PartOfSpeech: "subs");
}
