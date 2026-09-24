using Essenthos.Core.Loading.Links.Evidentia;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// A content word whose learned renderings are too spread out for the review tier is placed only where
/// the verse leaves one answer: one sense, one occurrence of it, and no other word claiming that
/// occurrence. <em>made</em> in the King James renders a dozen Hebrew verbs, none of them often.
/// </summary>
public class EvidentiaResidualRenderingTests
{
    private static readonly EvidentiaAddress Genesis17 = new(1, 1, 7);

    [Fact]
    public void AThinRenderingIsPlacedWhereItIsTheVersesOnlyAnswer()
    {
        var proposals = Residual(
            [English(1, 1, "made", "VERB"), English(2, 2, "firmament", "NOUN")],
            [Hebrew(11, 1, "יַּעַשׂ", "H6213", "verb"), Hebrew(12, 2, "רָקִיעַ", "H7549", "subs")],
            ("made", "H6213", 0.12));

        proposals.Should().Equal((1L, 11L));
    }

    [Fact]
    public void AThinRenderingAnotherFreeWordAlsoClaimsIsLeftAlone()
    {
        var proposals = Residual(
            [English(1, 1, "made", "VERB"), English(2, 2, "did", "VERB")],
            [Hebrew(11, 1, "יַּעַשׂ", "H6213", "verb")],
            ("made", "H6213", 0.12), ("did", "H6213", 0.12));

        proposals.Should().BeEmpty("either word could be the one the verb renders");
    }

    [Fact]
    public void APronounOrARenderingSeenTooRarelyIsLeftAlone()
    {
        var proposals = Residual(
            [English(1, 1, "that", "PRON"), English(2, 2, "made", "VERB")],
            [Hebrew(11, 1, "אֲשֶׁר", "H834", "conj"), Hebrew(12, 2, "יַּעַשׂ", "H6213", "verb")],
            ("that", "H834", 0.12), ("made", "H6213", 0.03));

        proposals.Should().BeEmpty();
    }

    private static IReadOnlyList<(long Source, long Target)> Residual(
        IReadOnlyList<EvidentiaToken> source,
        IReadOnlyList<EvidentiaToken> target,
        params (string Form, string Strong, double Share)[] renderings)
    {
        var packs = new LanguagePackRegistry([new EnglishLanguagePack(), new OriginalLanguagePack()]);
        var preview = new EvidentiaPipeline(packs, [new Renderings(renderings)])
            .Preview(new EvidentiaRequest(source, target, AllowSourceStrongEvidence: false));
        var resolver = new EvidentiaKnownRenderingProposalResolver();
        resolver.ResolveGlobally(preview.Candidates).Proposals.Should().BeEmpty("every share here is below the review floor");
        return resolver.ResolveResidual(preview.Candidates, []).Proposals
            .Select(proposal => (proposal.Source.Token.Id, proposal.Target.Token.Id))
            .ToList();
    }

    private static EvidentiaToken English(long id, int position, string surface, string partOfSpeech) =>
        new(id, Genesis17, position, surface, "eng", PartOfSpeech: partOfSpeech);

    private static EvidentiaToken Hebrew(long id, int position, string surface, string strong, string partOfSpeech) =>
        new(id, Genesis17, position, surface, "hbo", StrongNumber: strong, PartOfSpeech: partOfSpeech);

    private sealed class Renderings((string Form, string Strong, double Share)[] renderings) : IEvidentiaEvidenceSource
    {
        public IEnumerable<EvidentiaEvidence> Find(EvidentiaAnalysis source, EvidentiaAnalysis target) =>
            renderings
                .Where(rendering => source.IsContentWord
                    && rendering.Form.Equals(source.Token.Surface, StringComparison.OrdinalIgnoreCase)
                    && rendering.Strong == target.Token.StrongNumber)
                .Select(rendering => new EvidentiaEvidence(
                    EvidentiaEvidenceKind.KnownRendering,
                    EvidentiaDefaults.KnownRenderingBaseScore + EvidentiaDefaults.KnownRenderingShareScore * rendering.Share,
                    "test",
                    new EvidentiaEvidenceSupport(40, rendering.Share, 0.30)));
    }
}
