using Essenthos.Core.Loading.Links.Evidentia;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// The statistical aligner's pair is a second opinion on a placement and never a placement of its own:
/// it fills a word only where a dictionary sense or gloss names the same pair, and it withholds a
/// placement only where EVIDENTIA has lexical evidence for the word it names instead.
/// </summary>
public class EvidentiaAlignerCheckTests
{
    private static readonly EvidentiaAddress Genesis1 = new(1, 1, 24);

    private static readonly LanguagePackRegistry Packs = new([new EnglishLanguagePack(), new OriginalLanguagePack()]);

    private static readonly EvidentiaAnalysis Livestock = Analysis(English(1, 1, "livestock"));
    private static readonly EvidentiaAnalysis Beast = Analysis(Hebrew(11, 1, "בְּהֵמָה", "H929"));
    private static readonly EvidentiaAnalysis Herd = Analysis(Hebrew(12, 2, "מִקְנֶה", "H4735"));

    [Fact]
    public void AnUnplacedWordGoesOnTheAlignersWordWhereADictionarySenseNamesThePair()
    {
        var candidates = new[] { Candidate(Livestock, Beast, EvidentiaEvidenceKind.DictionarySense) };

        Filled(candidates, EvidentiaAlignerPairs.Of([(1L, 11L)])).Should().Equal((1L, 11L));
        Filled(candidates, EvidentiaAlignerPairs.Of([(1L, 12L)]))
            .Should().BeEmpty("the aligner's pair has no evidence of EVIDENTIA's own");
        Filled([], EvidentiaAlignerPairs.Of([(1L, 11L)])).Should().BeEmpty("nor is the aligner's pair taken on its own word");
    }

    [Fact]
    public void ALearnedRenderingTheTiersRefusedIsNotTheEvidenceTheFillAsksFor()
    {
        var candidates = new[]
        {
            Candidate(Livestock, Beast, EvidentiaEvidenceKind.DictionarySense, EvidentiaEvidenceKind.KnownRendering),
        };

        Filled(candidates, EvidentiaAlignerPairs.Of([(1L, 11L)])).Should().BeEmpty();
    }

    [Fact]
    public void AWordAlreadyPlacedAndAWordAlreadyTakenAreLeftAlone()
    {
        var candidates = new[] { Candidate(Livestock, Beast, EvidentiaEvidenceKind.TargetGloss) };
        var aligner = EvidentiaAlignerPairs.Of([(1L, 11L)]);
        var cattle = Analysis(English(2, 2, "cattle"));

        Filled(candidates, aligner, Placement(Livestock, Herd)).Should().BeEmpty();
        Filled(candidates, aligner, Placement(cattle, Beast)).Should().BeEmpty();
    }

    [Fact]
    public void APlacementIsContestedWhereTheAlignerNamesAnotherFreeWordTheEvidenceNamesToo()
    {
        var placement = Placement(Livestock, Herd);
        var both = new[]
        {
            Candidate(Livestock, Herd, EvidentiaEvidenceKind.KnownRendering),
            Candidate(Livestock, Beast, EvidentiaEvidenceKind.KnownRendering),
        };
        var elsewhere = EvidentiaAlignerPairs.Of([(1L, 11L)]);

        EvidentiaAlignerCheck.Contested([placement], both, elsewhere).Should().Equal((1L, 12L));
        EvidentiaAlignerCheck.Contested([placement], both, EvidentiaAlignerPairs.Of([(1L, 12L)]))
            .Should().BeEmpty("the aligner names the same pair");
        EvidentiaAlignerCheck.Contested([placement], both[..1], elsewhere)
            .Should().BeEmpty("EVIDENTIA has no evidence for the word the aligner names");
        EvidentiaAlignerCheck.Contested([placement, Placement(Analysis(English(2, 2, "cattle")), Beast)], both, elsewhere)
            .Should().BeEmpty("the word the aligner names is another word's");
    }

    [Fact]
    public void AnUnreadablePairsFileNamesWhatWritesOne()
    {
        var path = Path.Combine(Path.GetTempPath(), $"evidentia-aligner-{Guid.NewGuid():N}.tsv");
        try
        {
            File.WriteAllText(path, "1\t11\t0.98\t0.4\n1\t12\t0.3\n");
            var pairs = EvidentiaAlignerPairs.Read([path]);
            pairs.Agrees(1, 11).Should().BeTrue();
            pairs.Contradicts(1, 13).Should().BeTrue();
            pairs.Contradicts(2, 13).Should().BeFalse("the aligner says nothing of that word");

            File.WriteAllText(path, "one eleven");
            var unreadable = () => EvidentiaAlignerPairs.Read([path]);
            unreadable.Should().Throw<FormatException>().WithMessage("*score --pairs*");
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static List<(long, long)> Filled(
        EvidentiaCandidate[] candidates, EvidentiaAlignerPairs aligner, params EvidentiaProposal[] reserved) =>
        [
            .. EvidentiaAlignerCheck.Resolve(
                    candidates, reserved, aligner, EvidentiaVerseFrame.Of([Livestock], [Beast, Herd], reserved))
                .Select(proposal => (proposal.Source.Token.Id, proposal.Target.Token.Id)),
        ];

    private static EvidentiaCandidate Candidate(
        EvidentiaAnalysis source, EvidentiaAnalysis target, params EvidentiaEvidenceKind[] kinds) =>
        new(source, target,
        [
            new EvidentiaEvidence(EvidentiaEvidenceKind.ExactCanonicalAddress, EvidentiaDefaults.ExactAddressScore, "canonical-frame"),
            .. kinds.Select(kind => new EvidentiaEvidence(kind, 0.3, "test")),
        ]);

    private static EvidentiaProposal Placement(EvidentiaAnalysis source, EvidentiaAnalysis target) =>
        new(source, target, EvidentiaProposalKind.GlobalReviewKnownRendering, 0.5);

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
