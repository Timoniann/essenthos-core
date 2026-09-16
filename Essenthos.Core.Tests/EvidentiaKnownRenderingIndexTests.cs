using Essenthos.Core.Loading.Links.Evidentia;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

public class EvidentiaKnownRenderingIndexTests
{
    private static readonly HashSet<string> Wanted = ["word"];
    private static readonly HashSet<string> Surfaces = ["word"];

    [Fact]
    public void AHeldOutVerseContributesNothingToTheDistribution()
    {
        var distributions = RenderingDistributions.Build(
            [
                new RenderingObservation(HeldOutVerse, "word", "H1"),
                new RenderingObservation(HeldOutVerse, "word", "H1"),
                new RenderingObservation(101, "word", "H2"),
                new RenderingObservation(102, "word", "H2"),
            ],
            new HashSet<int> { HeldOutVerse },
            Wanted, Surfaces, surface => surface, minimumObservations: 2);

        var distribution = distributions["word"];
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
        var distributions = RenderingDistributions.Build(
            [
                new RenderingObservation(HeldOutVerse, "word", "H1"),
                new RenderingObservation(HeldOutVerse, "word", "H1"),
                new RenderingObservation(HeldOutVerse, "word", "H1"),
            ],
            new HashSet<int> { HeldOutVerse },
            Wanted, Surfaces, surface => surface, minimumObservations: 2);

        distributions.Should().BeEmpty();
    }

    [Fact]
    public void TheStrongestCompetingSenseIsTheStrongestInTheIndex()
    {
        var distributions = RenderingDistributions.Build(
            [
                new RenderingObservation(1, "word", "H1"),
                new RenderingObservation(2, "word", "H1"),
                new RenderingObservation(3, "word", "H2"),
                new RenderingObservation(4, "word", "H3"),
            ],
            new HashSet<int>(), Wanted, Surfaces, surface => surface, minimumObservations: 2);

        var distribution = distributions["word"];
        distribution.NextShare(distribution.Senses["H1"].Share).Should().Be(0.25);
        distribution.NextShare(distribution.Senses["H2"].Share).Should().Be(0.50);
    }

    private const int HeldOutVerse = 7;
}
