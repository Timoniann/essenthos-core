using Essenthos.Core.Loading.Links.Evidentia;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// A word of the translation and a word of the original annotated as one entity in one verse are paired,
/// in the order the verse names it, only where both sides name it as often.
/// </summary>
public class EvidentiaEntityAnchorTests
{
    private const int Abram = 1;
    private const int Lot = 2;
    private const int Egypt = 3;

    private static readonly EvidentiaAddress Genesis13 = new(1, 13, 1);
    private static readonly EvidentiaAddress Genesis14 = new(1, 13, 2);

    private static readonly LanguagePackRegistry Packs = new([new EnglishLanguagePack(), new OriginalLanguagePack()]);

    [Fact]
    public void EachNameGoesToTheOriginalWordNamingTheSameEntity()
    {
        var source = new[] { English(1, 1, "Abram"), English(2, 2, "went"), English(3, 3, "Egypt"), English(4, 4, "Lot") };
        var target = new[] { Hebrew(11, 1, "אַבְרָם"), Hebrew(12, 2, "מִצְרַיִם"), Hebrew(13, 3, "לוֹט") };

        Anchors(source, target, [(1, Abram), (3, Egypt), (4, Lot)], [(11, Abram), (12, Egypt), (13, Lot)])
            .Should().BeEquivalentTo([(1L, 11L), (3L, 12L), (4L, 13L)]);
    }

    [Fact]
    public void ANameRepeatedIsPairedInOrderOnlyWhereBothSidesRepeatItAsOften()
    {
        var source = new[] { English(1, 1, "Abram"), English(2, 2, "and"), English(3, 3, "Abram") };
        var twice = new[] { Hebrew(11, 1, "אַבְרָם"), Hebrew(12, 2, "אַבְרָם") };
        var once = new[] { Hebrew(11, 1, "אַבְרָם"), Hebrew(12, 2, "הוּא") };

        Anchors(source, twice, [(1, Abram), (3, Abram)], [(11, Abram), (12, Abram)])
            .Should().Equal((1L, 11L), (3L, 12L));
        Anchors(source, once, [(1, Abram), (3, Abram)], [(11, Abram)])
            .Should().BeEmpty("which of the two renders the one name is not something the annotations say");
    }

    [Fact]
    public void TheNeighbouringVerseIsReadOnlyWhenTheVerseItselfDoesNotNameTheEntity()
    {
        var source = new[] { English(1, 1, "Lot") };
        var target = new[] { Hebrew(11, 1, "לוֹט") with { Address = Genesis14 } };

        Anchors(source, target, [(1, Lot)], [(11, Lot)], neighbourVerses: 1).Should().Equal((1L, 11L));
        Anchors(source, target, [(1, Lot)], [(11, Lot)], neighbourVerses: 0).Should().BeEmpty();
    }

    [Fact]
    public void AWordTwoEntitiesWouldPlaceOnDifferentWordsIsLeftAlone()
    {
        var source = new[] { English(1, 1, "Abram") };
        var target = new[] { Hebrew(11, 1, "אַבְרָם"), Hebrew(12, 2, "לוֹט") };

        Anchors(source, target, [(1, Abram), (1, Lot)], [(11, Abram), (12, Lot)]).Should().BeEmpty();
    }

    private static List<(long, long)> Anchors(
        EvidentiaToken[] source,
        EvidentiaToken[] target,
        (long Word, int Entity)[] sourceNames,
        (long Word, int Entity)[] targetNames,
        int neighbourVerses = 1) =>
        [
            .. EvidentiaEntityAnchors.Resolve(
                    [.. source.Select(Analysis)],
                    [.. target.Select(Analysis)],
                    new EvidentiaEntityNames(Names(sourceNames), Names(targetNames)),
                    neighbourVerses)
                .Select(proposal => (proposal.Source.Token.Id, proposal.Target.Token.Id)),
        ];

    private static Dictionary<long, IReadOnlySet<int>> Names((long Word, int Entity)[] names) =>
        names.GroupBy(name => name.Word)
            .ToDictionary(word => word.Key, word => (IReadOnlySet<int>)word.Select(name => name.Entity).ToHashSet());

    private static EvidentiaAnalysis Analysis(EvidentiaToken token)
    {
        Packs.TryAnalyse(token, out var analysis).Should().BeTrue();
        return analysis;
    }

    private static EvidentiaToken English(long id, int position, string surface) =>
        new(id, Genesis13, position, surface, "eng", PartOfSpeech: "PROPN");

    private static EvidentiaToken Hebrew(long id, int position, string surface) =>
        new(id, Genesis13, position, surface, "hbo", PartOfSpeech: "nmpr");
}
