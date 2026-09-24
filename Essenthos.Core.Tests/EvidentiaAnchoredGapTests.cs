using Essenthos.Core.Loading.Links.Evidentia;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// A word left between two placed neighbours is placed on the one free word of its class the original
/// leaves between their renderings, and only where it has some lexical evidence for that word.
/// </summary>
public class EvidentiaAnchoredGapTests
{
    private static readonly EvidentiaAddress Genesis17 = new(1, 1, 7);

    private static readonly LanguagePackRegistry Packs = new([new EnglishLanguagePack(), new OriginalLanguagePack()]);

    [Fact]
    public void TheOneFreeWordOfItsClassBetweenTheNeighboursRenderingsIsPlaced()
    {
        var god = English(1, 1, "God", "PROPN");
        var made = English(2, 2, "made", "VERB");
        var firmament = English(3, 3, "firmament", "NOUN");
        var elohim = Hebrew(11, 1, "אֱלֹהִים", "H430", "subs");
        var make = Hebrew(12, 2, "יַּעַשׂ", "H6213", "verb");
        var expanse = Hebrew(13, 3, "רָקִיעַ", "H7549", "subs");

        Gap([god, made, firmament], [elohim, make, expanse], [(god, elohim), (firmament, expanse)], ("made", "H6213"))
            .Should().Equal((2L, 12L));
    }

    [Fact]
    public void TwoFreeWordsAWordOfAnotherClassOrNoEvidenceLeaveTheGapAlone()
    {
        var god = English(1, 1, "God", "PROPN");
        var made = English(2, 2, "made", "VERB");
        var firmament = English(3, 3, "firmament", "NOUN");
        var elohim = Hebrew(11, 1, "אֱלֹהִים", "H430", "subs");
        var make = Hebrew(12, 2, "יַּעַשׂ", "H6213", "verb");
        var also = Hebrew(13, 3, "גַּם", "H1571", "advb");
        var expanse = Hebrew(14, 4, "רָקִיעַ", "H7549", "subs");
        var water = Hebrew(15, 3, "מַיִם", "H4325", "subs");

        Gap([god, made, firmament], [elohim, make, also, expanse], [(god, elohim), (firmament, expanse)], ("made", "H6213"))
            .Should().BeEmpty("two words of the original stand in the gap");
        Gap([god, made, firmament], [elohim, water, expanse with { Position = 4 }], [(god, elohim), (firmament, expanse)], ("made", "H4325"))
            .Should().BeEmpty("a verb is not placed on a noun");
        Gap([god, made, firmament], [elohim, make, expanse], [(god, elohim), (firmament, expanse)])
            .Should().BeEmpty("nothing says made renders the verb");
    }

    private static IReadOnlyList<(long Source, long Target)> Gap(
        IReadOnlyList<EvidentiaToken> source,
        IReadOnlyList<EvidentiaToken> target,
        IReadOnlyList<(EvidentiaToken Source, EvidentiaToken Target)> placed,
        params (string Form, string Strong)[] renderings)
    {
        var preview = new EvidentiaPipeline(Packs, [new Renderings(renderings)])
            .Preview(new EvidentiaRequest(source, target, AllowSourceStrongEvidence: false));
        var reserved = placed
            .Select(pair => new EvidentiaProposal(Analysis(pair.Source), Analysis(pair.Target), EvidentiaProposalKind.GlobalReviewKnownRendering, 0.8))
            .ToList();
        return EvidentiaAnchoredGap.Resolve([.. source.Select(Analysis)], [.. target.Select(Analysis)], preview.Candidates, reserved)
            .Proposals
            .Select(proposal => (proposal.Source.Token.Id, proposal.Target.Token.Id))
            .ToList();
    }

    private static EvidentiaAnalysis Analysis(EvidentiaToken token)
    {
        Packs.TryAnalyse(token, out var analysis).Should().BeTrue();
        return analysis;
    }

    private static EvidentiaToken English(long id, int position, string surface, string partOfSpeech) =>
        new(id, Genesis17, position, surface, "eng", PartOfSpeech: partOfSpeech);

    private static EvidentiaToken Hebrew(long id, int position, string surface, string strong, string partOfSpeech) =>
        new(id, Genesis17, position, surface, "hbo", StrongNumber: strong, PartOfSpeech: partOfSpeech);

    private sealed class Renderings((string Form, string Strong)[] renderings) : IEvidentiaEvidenceSource
    {
        public IEnumerable<EvidentiaEvidence> Find(EvidentiaAnalysis source, EvidentiaAnalysis target) =>
            renderings
                .Where(rendering => rendering.Form.Equals(source.Token.Surface, StringComparison.OrdinalIgnoreCase)
                    && rendering.Strong == target.Token.StrongNumber)
                .Select(rendering => new EvidentiaEvidence(
                    EvidentiaEvidenceKind.KnownRendering,
                    EvidentiaDefaults.KnownRenderingBaseScore,
                    "test",
                    new EvidentiaEvidenceSupport(10, 0.02, 0.2)));
    }
}
