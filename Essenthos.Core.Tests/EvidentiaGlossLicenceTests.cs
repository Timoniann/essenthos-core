using Essenthos.Core.Loading.Links.Evidentia;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// Nestle 1904's word gloss is the Berean Interlinear's, so the placements it licenses are scored apart:
/// against the Berean key they read the key's own rendering.
/// </summary>
public class EvidentiaGlossLicenceTests
{
    private static readonly EvidentiaAddress Mark11 = new(41, 1, 1);

    private static readonly LanguagePackRegistry Packs = new([new EnglishLanguagePack(), new OriginalLanguagePack()]);

    [Theory]
    [InlineData(nameof(EvidentiaProposalKind.UniqueTargetGlossReview), true)]
    [InlineData(nameof(EvidentiaProposalKind.DictionaryAndGlossReview), true)]
    [InlineData(nameof(EvidentiaProposalKind.GlobalStableKnownRendering), false)]
    [InlineData(nameof(EvidentiaProposalKind.GlobalReviewKnownRendering), false)]
    [InlineData(nameof(EvidentiaProposalKind.UniqueContextGlossReview), false)]
    [InlineData(nameof(EvidentiaProposalKind.UniqueSharedStrong), false)]
    public void TheGlossTiersAreTheOnesTheWitnessGlossLicenses(string kind, bool licensed)
    {
        EvidentiaGlossLicence.ReadsTheWitnessGloss(Proposal(Enum.Parse<EvidentiaProposalKind>(kind), EvidentiaEvidenceKind.TargetGloss)).Should().Be(licensed);
    }

    [Fact]
    public void AnAnchoredGapIsTheGlossOnlyWhereTheGlossNamedTheWord()
    {
        EvidentiaGlossLicence.ReadsTheWitnessGloss(
            Proposal(EvidentiaProposalKind.AnchoredGapReview, EvidentiaEvidenceKind.TargetGloss)).Should().BeTrue();
        EvidentiaGlossLicence.ReadsTheWitnessGloss(
            Proposal(EvidentiaProposalKind.AnchoredGapReview, EvidentiaEvidenceKind.KnownRendering)).Should().BeFalse();
        EvidentiaGlossLicence.ReadsTheWitnessGloss(
            Proposal(EvidentiaProposalKind.AnchoredGapReview, null)).Should().BeFalse();
    }

    private static EvidentiaProposal Proposal(EvidentiaProposalKind kind, EvidentiaEvidenceKind? evidence)
    {
        Packs.TryAnalyse(new EvidentiaToken(1, Mark11, 1, "beginning", "eng"), out var source).Should().BeTrue();
        Packs.TryAnalyse(new EvidentiaToken(11, Mark11, 1, "Ἀρχὴ", "grc", StrongNumber: "G746"), out var target).Should().BeTrue();
        var trace = evidence is { } kindOfEvidence
            ? new EvidentiaDecisionTrace("review", "test", [new EvidentiaEvidence(kindOfEvidence, 0.5, "test")])
            : null;
        return new EvidentiaProposal(source, target, kind, 0.7, trace);
    }
}
