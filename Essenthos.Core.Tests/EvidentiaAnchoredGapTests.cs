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

    [Fact]
    public void ANounRendersANounOrANameAndAVerbOnlyAVerb()
    {
        var made = Analysis(English(1, 1, "made", "VERB"));
        var firmament = Analysis(English(2, 2, "firmament", "NOUN"));
        var make = Analysis(Hebrew(11, 1, "יַּעַשׂ", "H6213", "verb"));
        var expanse = Analysis(Hebrew(12, 2, "רָקִיעַ", "H7549", "subs"));
        var eden = Analysis(Hebrew(13, 3, "עֵדֶן", "H5731", "nmpr"));

        EvidentiaMorphologyLabels.AreCounterparts(made, make).Should().BeTrue();
        EvidentiaMorphologyLabels.AreCounterparts(firmament, expanse).Should().BeTrue();
        EvidentiaMorphologyLabels.AreCounterparts(firmament, eden).Should().BeTrue();
        EvidentiaMorphologyLabels.AreCounterparts(made, expanse).Should().BeFalse();
        EvidentiaMorphologyLabels.AreCounterparts(firmament, make).Should().BeFalse();
    }

    [Fact]
    public void AWordTheVerseRepeatsGoesOnAsManyOccurrencesOfItsLexemeInOrder()
    {
        var firmament = English(1, 1, "firmament", "NOUN");
        var under = English(2, 2, "under", "ADP");
        var firmament2 = English(3, 3, "firmament", "NOUN");
        var expanse = Hebrew(11, 1, "רָקִיעַ", "H7549", "subs");
        var below = Hebrew(12, 2, "תַּחַת", "H8478", "prep");
        var expanse2 = Hebrew(13, 3, "רָקִיעַ", "H7549", "subs");

        Repeated([firmament, under, firmament2], [expanse, below, expanse2], [], ("firmament", "H7549"))
            .Should().Equal((1L, 11L), (3L, 13L));
        Repeated([firmament, under, firmament2], [expanse, below, expanse2], [(firmament, expanse)], ("firmament", "H7549"))
            .Should().BeEmpty("two words are left for one free occurrence");
    }

    [Fact]
    public void TwoDifferentWordsOnOneLexemeAreNotPairedByOrder()
    {
        var stalk = English(1, 1, "stalk", "NOUN");
        var head = English(2, 2, "head", "NOUN");
        var ear = Hebrew(11, 1, "שִׁבֹּלֶת", "H7641", "subs");
        var ear2 = Hebrew(12, 2, "שִׁבֹּלֶת", "H7641", "subs");

        Repeated([stalk, head], [ear, ear2], [], ("stalk", "H7641"), ("head", "H7641")).Should().BeEmpty();
    }

    private static IReadOnlyList<(long Source, long Target)> Repeated(
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
        return EvidentiaRepeatedRendering.Resolve([.. target.Select(Analysis)], preview.Candidates, reserved)
            .Proposals
            .Select(proposal => (proposal.Source.Token.Id, proposal.Target.Token.Id))
            .ToList();
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
