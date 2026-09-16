using Essenthos.Core.Loading.Links.Evidentia;
using Essenthos.Core.Loading.Links;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

public class EvidentiaStrongProposalResolverTests
{
    [Fact]
    public void StableKnownRenderingSelectsTheClearHighestLexicalRendering()
    {
        var resolution = new EvidentiaKnownRenderingProposalResolver().Resolve(
        [
            KnownCandidate(1, 11, "H430", 0.65),
            KnownCandidate(1, 12, "H3068", 0.25),
            KnownCandidate(2, 12, "H3068", 0.65),
        ]);

        resolution.Proposals.Should().HaveCount(2);
        resolution.Proposals.Select(proposal => (proposal.Source.Token.Id, proposal.Target.Token.Id))
            .Should().BeEquivalentTo([(1L, 11L), (2L, 12L)]);
        resolution.Proposals.Should().OnlyContain(proposal =>
            proposal.Kind == EvidentiaProposalKind.StableKnownRendering);
    }

    [Fact]
    public void StableKnownRenderingLeavesNearEqualSensesForLaterEvidence()
    {
        var resolution = new EvidentiaKnownRenderingProposalResolver().Resolve(
        [KnownCandidate(1, 11, "H1", 0.50), KnownCandidate(1, 12, "H2", 0.45)]);

        resolution.Proposals.Should().BeEmpty();
    }

    [Fact]
    public void ReviewPolicyExposesAWeakerCandidateWithoutCallingItSafe()
    {
        var resolver = new EvidentiaKnownRenderingProposalResolver();
        var candidates = new[] { KnownCandidate(1, 11, "H1", 0.35) };

        resolver.Resolve(candidates).Proposals.Should().BeEmpty();
        var review = resolver.Resolve(candidates, EvidentiaKnownRenderingProposalResolver.Review);
        review.Proposals.Should().ContainSingle(proposal =>
            proposal.Kind == EvidentiaProposalKind.ReviewKnownRendering);
    }

    [Fact]
    public void GlobalReviewUsesAnAlternativeOccurrenceToKeepTwoClearRenderings()
    {
        var candidates = new[]
        {
            KnownCandidate(1, 11, "H1", 0.65),
            KnownCandidate(1, 12, "H1", 0.65),
            KnownCandidate(2, 11, "H1", 0.65),
        };

        var resolution = new EvidentiaKnownRenderingProposalResolver().ResolveGlobally(candidates);

        resolution.Proposals.Select(proposal => (proposal.Source.Token.Id, proposal.Target.Token.Id))
            .Should().BeEquivalentTo([(1L, 12L), (2L, 11L)]);
        resolution.Proposals.Should().OnlyContain(proposal =>
            proposal.Kind == EvidentiaProposalKind.GlobalReviewKnownRendering);

        var safeResolution = new EvidentiaKnownRenderingProposalResolver().ResolveGlobally(
            candidates, EvidentiaKnownRenderingProposalResolver.Safe);
        safeResolution.Proposals.Should().OnlyContain(proposal =>
            proposal.Kind == EvidentiaProposalKind.GlobalStableKnownRendering);
    }

    [Fact]
    public void RepeatedSharedStrongNumbersArePairedInWrittenOrderWhenCountsAgree()
    {
        var resolution = new EvidentiaStrongProposalResolver().Resolve(
            [Candidate(1, 11), Candidate(2, 12)]);

        resolution.Proposals.Select(proposal => (proposal.Source.Token.Id, proposal.Target.Token.Id))
            .Should().Equal((1, 11), (2, 12));
        resolution.Proposals.Should().OnlyContain(proposal =>
            proposal.Kind == EvidentiaProposalKind.SharedStrongInOrder && proposal.Confidence == 0.70);
        resolution.UnresolvedSourceWords.Should().Be(0);
    }

    [Fact]
    public void AnUnequalRepeatedStrongNumberRemainsUnresolved()
    {
        var resolution = new EvidentiaStrongProposalResolver().Resolve(
            [Candidate(1, 11), Candidate(2, 11)]);

        resolution.Proposals.Should().BeEmpty();
        resolution.UnresolvedSourceWords.Should().Be(2);
    }

    [Fact]
    public void UniqueTargetGlossAddsOnlyAnUnreservedExactCandidate()
    {
        var source = new EvidentiaAnalysis(
            new EvidentiaToken(1, new EvidentiaAddress(1, 1, 1), 1, "light", "eng"),
            "light", "light", null, false, LanguagePackCapability.Normalisation);
        var target = new EvidentiaAnalysis(
            new EvidentiaToken(11, new EvidentiaAddress(1, 1, 1), 1, "אוֹר", "hbo"),
            "אוֹר", null, null, false, LanguagePackCapability.Normalisation);
        var candidate = new EvidentiaCandidate(source, target,
        [
            new EvidentiaEvidence(EvidentiaEvidenceKind.ExactCanonicalAddress, 0.30, "test"),
            new EvidentiaEvidence(EvidentiaEvidenceKind.TargetGloss, 0.20, "test"),
        ]);

        var resolution = new EvidentiaTargetGlossProposalResolver().ResolveAdditional([candidate], []);

        resolution.Proposals.Should().ContainSingle(proposal =>
            proposal.Kind == EvidentiaProposalKind.UniqueTargetGlossReview);
    }

    [Fact]
    public void TargetGlossDoesNotChooseBetweenSeveralSourcesForOneTarget()
    {
        var sourceOne = new EvidentiaAnalysis(
            new EvidentiaToken(1, new EvidentiaAddress(1, 1, 1), 1, "serve", "eng"),
            "serve", "serve", null, false, LanguagePackCapability.Normalisation);
        var sourceTwo = sourceOne with
        {
            Token = sourceOne.Token with { Id = 2, Position = 2, Surface = "minister" },
            Normalised = "minister",
            Lemma = "minister",
        };
        var target = new EvidentiaAnalysis(
            new EvidentiaToken(11, new EvidentiaAddress(1, 1, 1), 1, "עבד", "hbo"),
            "עבד", null, null, false, LanguagePackCapability.Normalisation);
        var evidence = new[]
        {
            new EvidentiaEvidence(EvidentiaEvidenceKind.ExactCanonicalAddress, 0.30, "test"),
            new EvidentiaEvidence(EvidentiaEvidenceKind.TargetGloss, 0.20, "test"),
        };

        var resolution = new EvidentiaTargetGlossProposalResolver().ResolveAdditional(
            [new EvidentiaCandidate(sourceOne, target, evidence), new EvidentiaCandidate(sourceTwo, target, evidence)], []);

        resolution.Proposals.Should().BeEmpty();
    }

    [Fact]
    public void SyntaxGateKeepsTargetGlossInsideAnAnchoredClause()
    {
        var anchorSource = new EvidentiaAnalysis(
            new EvidentiaToken(1, new EvidentiaAddress(1, 1, 1), 1, "God", "eng"),
            "god", "god", null, false, LanguagePackCapability.Normalisation);
        var anchorTarget = new EvidentiaAnalysis(
            new EvidentiaToken(11, new EvidentiaAddress(1, 1, 1), 1, "אלהים", "hbo"),
            "אלהים", null, null, false, LanguagePackCapability.Normalisation);
        var glossSource = new EvidentiaAnalysis(
            new EvidentiaToken(2, new EvidentiaAddress(1, 1, 1), 2, "light", "eng"),
            "light", "light", null, false, LanguagePackCapability.Normalisation);
        var glossTarget = new EvidentiaAnalysis(
            new EvidentiaToken(12, new EvidentiaAddress(1, 1, 1), 2, "אור", "hbo"),
            "אור", null, null, false, LanguagePackCapability.Normalisation);
        var glossCandidate = new EvidentiaCandidate(glossSource, glossTarget,
        [
            new EvidentiaEvidence(EvidentiaEvidenceKind.ExactCanonicalAddress, 0.30, "test"),
            new EvidentiaEvidence(EvidentiaEvidenceKind.TargetGloss, 0.20, "test"),
        ]);

        var accepted = new EvidentiaSyntaxReviewGate().ClauseCohesiveTargetGlossCandidates(
            [glossCandidate],
            [new EvidentiaProposal(anchorSource, anchorTarget, EvidentiaProposalKind.GlobalStableKnownRendering, 0.80)],
            SyntaxPrior.Of((11, 1, 1, 1), (12, 2, 1, 1)));

        accepted.Should().Contain((2L, 12L));
    }

    private static EvidentiaCandidate Candidate(long sourceId, long targetId)
    {
        var source = new EvidentiaAnalysis(
            new EvidentiaToken(sourceId, new EvidentiaAddress(40, 17, 20), (int)sourceId, "faith", "eng", StrongNumber: "G4102"),
            "faith", null, null, false, LanguagePackCapability.Normalisation);
        var target = new EvidentiaAnalysis(
            new EvidentiaToken(targetId, new EvidentiaAddress(40, 17, 20), (int)(targetId - 10), "πίστις", "grc", StrongNumber: "G4102"),
            "πίστις", null, null, false, LanguagePackCapability.Normalisation);
        return new EvidentiaCandidate(
            source,
            target,
            [
                new EvidentiaEvidence(EvidentiaEvidenceKind.ExactCanonicalAddress, 0.30, "test"),
                new EvidentiaEvidence(EvidentiaEvidenceKind.SharedStrongNumber, 0.90, "test"),
            ]);
    }

    private static EvidentiaCandidate KnownCandidate(long sourceId, long targetId, string strong, double score)
    {
        var source = new EvidentiaAnalysis(
            new EvidentiaToken(sourceId, new EvidentiaAddress(1, 1, 1), (int)sourceId, "word", "eng"),
            "word", "word", null, false, LanguagePackCapability.Normalisation);
        var target = new EvidentiaAnalysis(
            new EvidentiaToken(targetId, new EvidentiaAddress(1, 1, 1), (int)(targetId - 10), "דבר", "hbo", StrongNumber: strong),
            "דבר", null, null, false, LanguagePackCapability.None);
        return new EvidentiaCandidate(source, target,
        [
            new EvidentiaEvidence(EvidentiaEvidenceKind.ExactCanonicalAddress, 0.30, "test"),
            new EvidentiaEvidence(EvidentiaEvidenceKind.KnownRendering, score, "test"),
        ]);
    }
}
