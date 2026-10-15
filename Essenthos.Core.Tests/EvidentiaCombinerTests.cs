using System.Text;
using System.Text.Json;
using Essenthos.Core.Loading.Links.Evidentia;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// The combiner weighs every edge of the words the lexical tiers own by a model fitted outside the Forge on the
/// Forge's own export. The weights only mean what they were fitted to mean if this code computes the probability
/// the fit computed for the same features, and if it decides by the bands the weights file states.
/// </summary>
public class EvidentiaCombinerTests
{
    private static readonly EvidentiaAddress Verse = new(8, 1, 6);

    private static readonly LanguagePackRegistry Packs = new([new EnglishLanguagePack(), new OriginalLanguagePack()]);

    private static readonly string[] Pivots = ["KJV", "ULT", "YLT", "RLT2018"];

    [Fact]
    public void TheForgeGivesEveryExportedEdgeTheProbabilityTheFitGaveIt()
    {
        var combiner = EvidentiaCombiner.Default;

        combiner.Checks.Should().NotBeEmpty("the weights file carries edges of one exported chapter with the fit's probabilities");
        foreach (var check in combiner.Checks)
        {
            var (raw, calibrated) = combiner.Probability(check.Group, check.Features, check.Current);
            raw.Should().BeApproximately(check.Raw, 1e-9, check.Where);
            calibrated.Should().BeApproximately(check.Calibrated, 1e-12, check.Where);
        }
    }

    [Fact]
    public void TheWeightsFileNamesTheFeaturesThisBuildComputes()
    {
        var combiner = EvidentiaCombiner.Default;

        combiner.Version.Should().NotBe("unfitted");
        combiner.Pivots.Should().Equal(Pivots);
        combiner.ReviewBand.Should().BeLessThan(combiner.SafeBand);
        combiner.WithdrawBelow.Should().BeLessThan(combiner.ReviewBand);
    }

    [Fact]
    public void AnEdgeReadsItsEvidenceThePivotAndTheTiersOwnPlacement()
    {
        var word = English(1, 1, "livestock");
        var beast = Hebrew(11, 1, "בְּהֵמָה", "H929");
        var herd = Hebrew(12, 2, "מִקְנֶה", "H4735");
        var placed = new EvidentiaProposal(word, herd, EvidentiaProposalKind.GlobalReviewKnownRendering, 0.5);
        var pivot = EvidentiaPivot.Match([word],
            [new EvidentiaPivotText("KJV", [English(101, 1, "livestock")], new Dictionary<long, HashSet<long>> { [101] = [11] })]);

        var edges = EvidentiaCombinerFeatures.Edges(
            [word], [beast, herd], [Candidate(word, beast, 0.6, 0.7), Candidate(word, herd, 0.4, 0.2)], [placed],
            new HashSet<(long, long)>(), pivot, Pivots);

        var names = EvidentiaCombinerFeatures.Names(Pivots);
        double Feature(EvidentiaCombinerEdge edge, string name) => edge.Features[names.ToList().IndexOf(name)];
        var onBeast = edges.Single(edge => edge.Target == beast);
        var onHerd = edges.Single(edge => edge.Target == herd);
        Feature(onBeast, "score").Should().BeApproximately(0.6, 1e-9);
        Feature(onBeast, "share").Should().BeApproximately(0.7, 1e-9);
        Feature(onBeast, "top").Should().Be(1);
        Feature(onBeast, "lead").Should().BeApproximately(0.2, 1e-9);
        Feature(onBeast, "pivot_KJV").Should().Be(1);
        Feature(onBeast, "current").Should().Be(0);
        Feature(onHerd, "current").Should().Be(1);
        Feature(onHerd, $"current_{nameof(EvidentiaProposalKind.GlobalReviewKnownRendering)}").Should().Be(1);
        Feature(onHerd, "pivot_names_another").Should().Be(1);
        Feature(onHerd, "diagonal").Should().Be(1);
        onHerd.IsCurrent.Should().BeTrue();
        Feature(onBeast, "pos_counterparts").Should().Be(1, "a noun renders a noun");
        Feature(onBeast, "pos_not_counterparts").Should().Be(0);
        Feature(onBeast, "number_agrees").Should().Be(0, "neither word states a number here");
        Feature(onBeast, "number_disagrees").Should().Be(0);
    }

    [Fact]
    public void AVerbOnANounAndASingularOnAPluralAreSaidSo()
    {
        var verb = new EvidentiaToken(11, Verse, 1, "יְדֻשָּׁן", "hbo", StrongNumber: "H1878", PartOfSpeech: "verb",
            Morphology: new Dictionary<string, string> { ["pos"] = "verb", ["number"] = "pl" });
        Packs.TryAnalyse(verb, out var target).Should().BeTrue();
        var singular = new EvidentiaToken(1, Verse, 1, "fat", "eng", PartOfSpeech: "NOUN",
            Morphology: new Dictionary<string, string> { ["Number"] = "Sing" });
        Packs.TryAnalyse(singular, out var source).Should().BeTrue();

        var edges = EvidentiaCombinerFeatures.Edges(
            [source], [target], [Candidate(source, target, 0.6, 0.7)], [], new HashSet<(long, long)>(), EvidentiaPivot.None, Pivots);

        var names = EvidentiaCombinerFeatures.Names(Pivots).ToList();
        var edge = edges.Should().ContainSingle().Subject;
        edge.Features[names.IndexOf("pos_counterparts")].Should().Be(0);
        edge.Features[names.IndexOf("pos_not_counterparts")].Should().Be(1);
        edge.Features[names.IndexOf("number_disagrees")].Should().Be(1);
    }

    [Fact]
    public void AnEdgeBetweenTheRenderingsOfItsPlacedNeighboursAndBesideItsHeadsRenderingIsSaidToBe()
    {
        var before = English(1, 1, "flocks");
        Packs.TryAnalyse(new EvidentiaToken(2, Verse, 2, "livestock", "eng", PartOfSpeech: "NOUN", SyntacticHead: 1), out var word)
            .Should().BeTrue();
        var after = English(3, 3, "herds");
        var sheep = Hebrew(11, 1, "צֹאן", "H6629");
        var beast = Hebrew(12, 2, "בְּהֵמָה", "H929");
        var herd = Hebrew(13, 3, "מִקְנֶה", "H4735");
        List<EvidentiaProposal> placed =
        [
            new(before, sheep, EvidentiaProposalKind.GlobalReviewKnownRendering, 0.5),
            new(after, herd, EvidentiaProposalKind.GlobalReviewKnownRendering, 0.5),
        ];

        var edges = EvidentiaCombinerFeatures.Edges(
            [before, word, after], [sheep, beast, herd], [Candidate(word, beast, 0.6, 0.7)], placed,
            new HashSet<(long, long)>(), EvidentiaPivot.None, Pivots);

        var names = EvidentiaCombinerFeatures.Names(Pivots).ToList();
        var edge = edges.Single(edge => edge.Source.Token.Id == 2);
        edge.Features[names.IndexOf("between_anchors")].Should().Be(1);
        edge.Features[names.IndexOf("anchor_distance")].Should().BeApproximately(1 / 6.0, 1e-12);
        edge.Features[names.IndexOf("no_anchor")].Should().Be(0);
        edge.Features[names.IndexOf("head_near")].Should().Be(1, "its head, flocks, was placed on the word just before");
        edge.Features[names.IndexOf("head_far")].Should().Be(0);
    }

    [Fact]
    public void TheTiersOwnPlacementReadsTheCalibrationOfTheKeptPlacements()
    {
        var combiner = Model(kept: true);
        var names = EvidentiaCombinerFeatures.Names(Pivots);
        var features = names.Select(name => name == "score" ? 0.99 : 0.0).ToArray();

        combiner.Probability("all", features).Calibrated.Should().Be(0.99);
        combiner.Probability("all", features, current: true).Calibrated.Should().Be(0.90);
    }

    [Fact]
    public void AWordAnotherWordHoldsOutsideTheCombinersReachIsNotOffered()
    {
        var word = English(1, 1, "livestock");
        var article = English(2, 2, "the");
        var beast = Hebrew(11, 1, "בְּהֵמָה", "H929");
        var held = new EvidentiaProposal(article, beast, EvidentiaProposalKind.GlobalReviewKnownRendering, 0.5);

        var edges = EvidentiaCombinerFeatures.Edges(
            [word, article], [beast], [Candidate(word, beast, 0.6, 0.7)], [held], new HashSet<(long, long)>(), EvidentiaPivot.None, Pivots);

        edges.Should().BeEmpty("a function word's placement is not the combiner's to take, and its target is not free");
    }

    [Fact]
    public void ANewPlacementNeedsTheReviewBandAndTheTiersOwnStandsDownOnlyUnderTheWithdrawalLine()
    {
        var combiner = Model();
        var word = English(1, 1, "livestock");
        var other = English(2, 2, "flocks");
        var beast = Hebrew(11, 1, "בְּהֵמָה", "H929");
        var herd = Hebrew(12, 2, "מִקְנֶה", "H4735");
        var sheep = Hebrew(13, 3, "צֹאן", "H6629");
        var placedWord = new EvidentiaProposal(word, herd, EvidentiaProposalKind.GlobalReviewKnownRendering, 0.5);
        var placedOther = new EvidentiaProposal(other, sheep, EvidentiaProposalKind.GlobalReviewKnownRendering, 0.5);
        List<EvidentiaCandidate> candidates =
        [
            Candidate(word, beast, 0.99, 0), Candidate(word, herd, 0.95, 0),
            Candidate(other, herd, 0.70, 0), Candidate(other, sheep, 0.50, 0),
        ];
        var edges = EvidentiaCombinerFeatures.Edges(
            [word, other], [beast, herd, sheep], candidates, [placedWord, placedOther], new HashSet<(long, long)>(),
            EvidentiaPivot.None, Pivots);

        var decision = combiner.Decide(edges, [placedWord, placedOther]);

        var placements = decision.Lexical.ToDictionary(proposal => proposal.Source.Token.Id, proposal => proposal.Target.Token.Id);
        placements.Should().Equal(new Dictionary<long, long> { [1] = 11 }, "flocks' own placement scores under the withdrawal line");
        decision.Added.Should().ContainSingle().Which.Kind.Should().Be(EvidentiaProposalKind.CombinedEvidence);
        decision.Safe.Should().Equal((1L, 11L));
        decision.Withdrawn.Select(proposal => proposal.Source.Token.Id).Should().BeEquivalentTo([1L, 2L]);
        decision.Probabilities[1].Should().Be(0.99);
    }

    /// <summary>A model that reads the score alone, calibrated to the score itself, so the bands can be set by hand.</summary>
    private static EvidentiaCombiner Model(bool kept = false)
    {
        var names = EvidentiaCombinerFeatures.Names(Pivots);
        var weights = names.Select(name => name == "score" ? 50.0 : 0).ToArray();
        var mean = names.Select(name => name == "score" ? 0.75 : 0).ToArray();
        double[][] calibration =
        [
            [Logistic(50 * (0.55 - 0.75)), 0.50],
            [Logistic(50 * (0.65 - 0.75)), 0.59],
            [Logistic(50 * (0.80 - 0.75)), 0.70],
            [Logistic(50 * (0.97 - 0.75)), 0.95],
            [1.0, 0.99],
        ];
        var document = new
        {
            version = "test",
            fittedOn = (object?)null,
            commit = "",
            pivots = Pivots,
            features = names,
            bands = new { safe = 0.98, review = 0.90, withdraw = 0.60 },
            models = new Dictionary<string, object>
            {
                ["all"] = new
                {
                    intercept = 0.0, weights, mean, scale = names.Select(_ => 1.0).ToArray(), calibration,
                    calibrationKept = kept ? new[] { new[] { 1.0, 0.90 } } : null,
                },
            },
        };
        return EvidentiaCombiner.Read(new MemoryStream(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(document))));
    }

    private static double Logistic(double value) => 1 / (1 + Math.Exp(-value));

    private static EvidentiaCandidate Candidate(EvidentiaAnalysis source, EvidentiaAnalysis target, double score, double share) =>
        new(source, target,
        [
            new EvidentiaEvidence(EvidentiaEvidenceKind.ExactCanonicalAddress, 0, "canonical-frame"),
            new EvidentiaEvidence(EvidentiaEvidenceKind.KnownRendering, score, "test", new EvidentiaEvidenceSupport(10, share, 0)),
        ]);

    private static EvidentiaAnalysis English(long id, int position, string surface)
    {
        Packs.TryAnalyse(new EvidentiaToken(id, Verse, position, surface, "eng", PartOfSpeech: surface == "the" ? "DET" : "NOUN"),
            out var analysis).Should().BeTrue();
        return analysis;
    }

    private static EvidentiaAnalysis Hebrew(long id, int position, string surface, string strongNumber)
    {
        Packs.TryAnalyse(new EvidentiaToken(id, Verse, position, surface, "hbo", StrongNumber: strongNumber, PartOfSpeech: "subs"),
            out var analysis).Should().BeTrue();
        return analysis;
    }
}
