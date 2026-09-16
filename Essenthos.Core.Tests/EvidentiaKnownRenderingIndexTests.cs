using Essenthos.Core.Loading.Links.Evidentia;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

public class EvidentiaKnownRenderingIndexTests
{
    [Fact]
    public void AHeldOutVerseContributesNothingToTheDistribution()
    {
        var distributions = Build(
            [
                Observed(HeldOutVerse, "word", "H1"),
                Observed(HeldOutVerse, "word", "H1"),
                Observed(101, "word", "H2"),
                Observed(102, "word", "H2"),
            ],
            heldOut: new HashSet<int> { HeldOutVerse });

        var distribution = distributions[Surface("word")];
        distribution.Observations.Should().Be(2);
        distribution.Senses.Should().ContainKey("H2").And.NotContainKey("H1",
            "the chapter being measured is the answer key, not evidence for its own prediction");
    }

    [Fact]
    public void AVerseSpanningTwoChaptersIsExcludedByItsIdRatherThanByOneOfItsAddresses()
    {
        // The verse is placed at the end of the held-out chapter and again at the start of the
        // next one. Excluding reference rows let the second placement through and the verse
        // trained on itself; the exclusion is a set of verse ids, so one of its addresses being
        // outside the chapter cannot readmit it.
        var distributions = Build(
            [
                Observed(HeldOutVerse, "word", "H1"),
                Observed(HeldOutVerse, "word", "H1"),
                Observed(HeldOutVerse, "word", "H1"),
            ],
            heldOut: new HashSet<int> { HeldOutVerse });

        distributions.Should().BeEmpty();
    }

    [Fact]
    public void TheStrongestCompetingSenseIsTheStrongestInTheIndex()
    {
        var distributions = Build(
        [
            Observed(1, "word", "H1"),
            Observed(2, "word", "H1"),
            Observed(3, "word", "H2"),
            Observed(4, "word", "H3"),
        ]);

        var distribution = distributions[Surface("word")];
        distribution.NextShare(distribution.Senses["H1"].Share).Should().Be(0.25);
        distribution.NextShare(distribution.Senses["H2"].Share).Should().Be(0.50);
    }

    [Fact]
    public void AFormTheCorpusNeverWroteReachesWhatItKnowsAboutTheSameWord()
    {
        // The passage says "divided"; the corpus only ever wrote "divideth" and "divide". Both
        // reduce to the same normalisation, which is the whole reason the index is keyed on one.
        var evidence = Source(
        [
            Observed(1, "divideth", "H914"),
            Observed(2, "divide", "H914"),
            Observed(3, "divide", "H914"),
        ], English("divided"));

        var found = Assert.Single(evidence.Find(English("divided"), Target("H914")));
        found.Support!.MatchedForm.Should().Be(EvidentiaFormKind.Normalised);
        found.Support.Observations.Should().Be(3);
    }

    [Fact]
    public void TheExactFormIsPreferredOverTheNormalisationWhenTheCorpusHasBothAndSaysWhichAnswered()
    {
        var evidence = Source(
        [
            Observed(1, "divided", "H914"),
            Observed(2, "divided", "H914"),
            Observed(3, "divideth", "H6504"),
            Observed(4, "divide", "H6504"),
        ], English("divided"));

        var found = Assert.Single(evidence.Find(English("divided"), Target("H914")));
        found.Support!.MatchedForm.Should().Be(EvidentiaFormKind.Surface);
        found.Support.Observations.Should().Be(2, "the exact form was answered on its own count");
    }

    [Fact]
    public void AnExactFormTooThinToHaveAnEntryFallsBackToTheNormalisation()
    {
        var evidence = Source(
        [
            Observed(1, "divided", "H914"),
            Observed(2, "divideth", "H914"),
            Observed(3, "divide", "H914"),
        ], English("divided"));

        var found = Assert.Single(evidence.Find(English("divided"), Target("H914")));
        found.Support!.MatchedForm.Should().Be(EvidentiaFormKind.Normalised);
        found.Support.Observations.Should().Be(3);
    }

    [Fact]
    public void TheEntryIsChosenBeforeTheTargetIsSeen()
    {
        // "divided" has an entry of its own and it says H6504. A lookup that kept trying keys
        // until one agreed would answer H914 off the normalisation and report a share the exact
        // form contradicts.
        var evidence = Source(
        [
            Observed(1, "divided", "H6504"),
            Observed(2, "divided", "H6504"),
            Observed(3, "divideth", "H914"),
            Observed(4, "divide", "H914"),
        ], English("divided"));

        evidence.Find(English("divided"), Target("H914")).Should().BeEmpty();
        Assert.Single(evidence.Find(English("divided"), Target("H6504")));
    }

    [Fact]
    public void AWitnessStatedLemmaIsAKeyOfItsOwn()
    {
        var evidence = Source(
        [
            new RenderingObservation(1, "בָּרָא", "ברא", "H1254"),
            new RenderingObservation(2, "בֹּרַאֲךָ", "ברא", "H1254"),
        ], Hebrew("בֹּורְאֶיךָ", "ברא"));

        var found = Assert.Single(evidence.Find(Hebrew("בֹּורְאֶיךָ", "ברא"), Target("H1254")));
        found.Support!.MatchedForm.Should().Be(EvidentiaFormKind.Lemma,
            "an original-language surface is its own normalisation, so only the lemma joins the forms");
    }

    [Fact]
    public void APackWithNoLemmatiserFilesNoLemmaKey()
    {
        RenderingKeys.Of(English("divided")).Select(key => key.Kind)
            .Should().Equal(EvidentiaFormKind.Surface, EvidentiaFormKind.Normalised);
    }

    [Fact]
    public void AFunctionWordHasNoKeys() => RenderingKeys.Of(English("the")).Should().BeEmpty();

    [Fact]
    public void AFunctionWordIsNeverLookedUp()
    {
        var evidence = Source([Observed(1, "the", "H853"), Observed(2, "the", "H853")], English("the"));

        evidence.Find(English("the"), Target("H853")).Should().BeEmpty();
    }

    private static RenderingObservation Observed(int verse, string surface, string strongNumber) =>
        new(verse, surface, null, strongNumber);

    private static RenderingKey Surface(string form) => new(EvidentiaFormKind.Surface, form);

    private static EvidentiaAnalysis English(string surface) =>
        new EnglishLanguagePack().Analyse(new EvidentiaToken(0, default, 0, surface, "eng"));

    private static EvidentiaAnalysis Hebrew(string surface, string lemma) =>
        new OriginalLanguagePack().Analyse(
            new EvidentiaToken(0, default, 0, surface, "hbo", Lemma: lemma, PartOfSpeech: "verb"));

    private static EvidentiaAnalysis Target(string strongNumber) =>
        new OriginalLanguagePack().Analyse(
            new EvidentiaToken(1, default, 1, "x", "hbo", StrongNumber: strongNumber, PartOfSpeech: "verb"));

    private static IReadOnlyDictionary<RenderingKey, RenderingDistribution> Build(
        IReadOnlyList<RenderingObservation> observations,
        IReadOnlySet<int>? heldOut = null,
        EvidentiaAnalysis? asked = null)
    {
        var language = asked?.Token.Language ?? "eng";
        var wanted = observations
            .SelectMany(observation => Keys(observation, language))
            .Concat(asked is null ? [] : RenderingKeys.Of(asked))
            .ToHashSet();
        return RenderingDistributions.Build(
            observations,
            heldOut ?? new HashSet<int>(),
            wanted,
            observation => Keys(observation, language),
            minimumObservations: 2);
    }

    private static EvidentiaKnownRenderingEvidenceSource Source(
        IReadOnlyList<RenderingObservation> observations, EvidentiaAnalysis asked) =>
        new(Build(observations, asked: asked), "known-rendering:test",
            new HashSet<string>(StringComparer.Ordinal), new HashSet<string>(StringComparer.Ordinal));

    private static IReadOnlyList<RenderingKey> Keys(RenderingObservation observation, string language) =>
        RenderingKeys.Of(Pack(language).Analyse(new EvidentiaToken(
            0, default, 0, observation.SourceSurface, language, Lemma: observation.SourceLemma)));

    private static ILanguagePack Pack(string language) => language == "eng"
        ? new EnglishLanguagePack()
        : new OriginalLanguagePack();

    private const int HeldOutVerse = 7;
}
