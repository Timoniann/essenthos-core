using Essenthos.Core.Loading.Links.Evidentia;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// A word placed by the one free candidate its verse leaves is refused when it stands at the other end of
/// the verse from where its placed neighbours put it.
/// </summary>
public class EvidentiaVerseFrameTests
{
    private static readonly EvidentiaAddress Psalm103 = new(19, 10, 3);

    private static readonly LanguagePackRegistry Packs = new([new EnglishLanguagePack(), new OriginalLanguagePack()]);

    [Fact]
    public void APlacementFarFromWhereItsNeighboursPutItIsRefused()
    {
        var source = Enumerable.Range(1, 10).Select(position => English(position)).ToList();
        var target = Enumerable.Range(1, 10).Select(position => Hebrew(position)).ToList();
        var anchors = new[] { (1, 1), (2, 2), (4, 4), (8, 8), (9, 9) }
            .Select(pair => Proposal(source[pair.Item1 - 1], target[pair.Item2 - 1]))
            .ToList();
        var frame = EvidentiaVerseFrame.Of(source, target, anchors);
        var near = Proposal(source[4], target[4]);
        var far = Proposal(source[2], target[9]);

        frame.Near([near, far]).Should().Equal(near);
    }

    private static EvidentiaProposal Proposal(EvidentiaAnalysis source, EvidentiaAnalysis target) =>
        new(source, target, EvidentiaProposalKind.UniqueDictionarySenseReview, 0.4);

    private static EvidentiaAnalysis English(int position) =>
        Analysis(new EvidentiaToken(position, Psalm103, position, $"word{position}", "eng", PartOfSpeech: "NOUN"));

    private static EvidentiaAnalysis Hebrew(int position) =>
        Analysis(new EvidentiaToken(100 + position, Psalm103, position, $"מִלָּה{position}", "hbo", StrongNumber: $"H{position}", PartOfSpeech: "subs"));

    private static EvidentiaAnalysis Analysis(EvidentiaToken token)
    {
        Packs.TryAnalyse(token, out var analysis).Should().BeTrue();
        return analysis;
    }
}
