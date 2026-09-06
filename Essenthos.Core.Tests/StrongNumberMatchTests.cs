using Essenthos.Core.Loading.Links;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// What a Strong number is worth as a claim about which word renders which.
///
/// It is a lemma and not a token, so the whole method is about what is left over once the counting
/// is done: a number written once on each side leaves nothing to choose, a number written three
/// times on each side leaves an order to trust, and a number written once here and three times
/// there leaves a guess. The confidence column is the only place that difference is recorded, and
/// two texts in this corpus — the King James and Luther 1912 — reach the originals by it.
/// </summary>
public class StrongNumberMatchTests
{
    private static readonly Dictionary<string, NumberRedirect> NoRedirects = new();

    private static List<StrongMatchDraft> Match(
        (long Id, string[] Numbers)[] tagged,
        (long Id, string? Strong)[] witness,
        Dictionary<string, NumberRedirect>? redirects = null) =>
        StrongNumberMatch.Verse(
            [.. tagged.Select(w => new StrongNumberMatch.TaggedWord(w.Id, w.Numbers))],
            [.. witness.Select(w => new StrongNumberMatch.WitnessWord(w.Id, w.Strong))],
            redirects ?? NoRedirects,
            out _);

    /// <summary>
    /// The ordinary case, and the only one the number settles on its own. It is still an inference
    /// — the tag says the lemma, not the occurrence — so it is high rather than certain.
    /// </summary>
    [Fact]
    public void ANumberWrittenOnceOnEachSideIsOneLinkAtNineTenths()
    {
        var links = Match([(1, ["H430"])], [(11, "H430"), (12, "H776")]);

        links.Should().ContainSingle();
        links[0].From.Should().Equal(1);
        links[0].To.Should().Equal(11);
        links[0].Confidence.Should().Be(0.9);
        links[0].Kind.Should().Be(StrongMatchKind.Unambiguous);
    }

    /// <summary>
    /// The same number as many times on one side as the other. One link naming all six words is a
    /// true claim and a useless one — it lights the whole verse when a reader touches one word —
    /// and the order both texts write them in is the one thing they agree about, so they are paired
    /// in it and the confidence says that an assumption was made.
    /// </summary>
    [Fact]
    public void ANumberWrittenTwiceOnBothSidesIsPairedInOrder()
    {
        var links = Match(
            [(1, ["G1161"]), (2, ["G1161"])],
            [(11, "G1161"), (12, "G1161")]);

        links.Should().HaveCount(2);
        links.Select(link => (link.From[0], link.To[0])).Should().Equal((1L, 11L), (2L, 12L));
        links.Should().OnlyContain(link => link.Confidence == 0.7);
        links.Should().OnlyContain(link => link.Kind == StrongMatchKind.Paired);
    }

    /// <summary>
    /// Counts that do not agree cannot be paired off, and inventing a bijection would be a claim
    /// nothing supports. The set stands, and says so with a lower confidence.
    /// </summary>
    [Fact]
    public void ANumberWrittenOnceHereAndTwiceThereIsOneLinkNamingBoth()
    {
        var links = Match([(1, ["H853"])], [(11, "H853"), (12, "H853")]);

        links.Should().ContainSingle();
        links[0].To.Should().Equal(11, 12);
        links[0].Confidence.Should().Be(0.5);
        links[0].Kind.Should().Be(StrongMatchKind.Contended);
    }

    [Fact]
    public void MoreThanOneCandidateOnBothSidesIsTheWeakestShapeOfAll()
    {
        var links = Match(
            [(1, ["H1"]), (2, ["H1"]), (3, ["H1"])],
            [(11, "H1"), (12, "H1")]);

        links.Should().ContainSingle();
        links[0].From.Should().Equal(1, 2, 3);
        links[0].To.Should().Equal(11, 12);
        links[0].Confidence.Should().Be(0.3);
    }

    /// <summary>
    /// A tag naming two numbers is one word standing over a phrase, and the source states the whole
    /// list. So the link names every word of it, and it is never paired off into two links: each
    /// word of the phrase renders both, and splitting them would turn one stated claim into two
    /// invented ones.
    /// </summary>
    [Fact]
    public void ATagNamingAPhraseIsOneLinkOverAllOfIt()
    {
        var links = Match([(1, ["G1223", "G5124"])], [(11, "G1223"), (12, "G5124")]);

        links.Should().ContainSingle();
        links[0].To.Should().Equal(11, 12);
        links[0].Confidence.Should().Be(0.9);
        links[0].Kind.Should().Be(StrongMatchKind.Unambiguous);
    }

    /// <summary>
    /// A number no witness word in the verse carries reaches nothing. It is counted and written as
    /// nothing at all: the translation may be rendering a longer text than this corpus holds, or the
    /// match may have failed, and a link either way would assert the one this cannot tell from the
    /// other.
    /// </summary>
    [Fact]
    public void ANumberTheVerseDoesNotCarryIsCountedAndNotWritten()
    {
        var links = StrongNumberMatch.Verse(
            [new StrongNumberMatch.TaggedWord(1, ["G9999"])],
            [new StrongNumberMatch.WitnessWord(11, "G1")],
            NoRedirects,
            out var tally);

        links.Should().BeEmpty();
        tally.Unmatched.Should().Be(1);
    }

    /// <summary>
    /// Where the dictionary joins the tag's number to the one the witness writes, the link is still
    /// built and every tier loses the same tenth — because what the redirect adds is the same
    /// everywhere: one more inference between the link and the two texts that state it.
    /// </summary>
    [Fact]
    public void ANumberReachedThroughTheDictionaryCostsATenth()
    {
        var links = StrongNumberMatch.Verse(
            [new StrongNumberMatch.TaggedWord(1, ["G2076"])],
            [new StrongNumberMatch.WitnessWord(11, "G1510")],
            new Dictionary<string, NumberRedirect> { ["G2076"] = new(["G1510"], 0.99) },
            out var tally);

        links.Should().ContainSingle();
        links[0].Confidence.Should().Be(0.8);
        tally.Resolved.Should().Be(1);
    }

    /// <summary>
    /// Two words of the translation carrying one number are one claim about a set, not two claims
    /// each pretending to be about a pair.
    /// </summary>
    [Fact]
    public void TwoTranslatedWordsRenderingOneWitnessWordAreOneLink()
    {
        var links = Match([(1, ["H7225"]), (2, ["H7225"])], [(11, "H7225")]);

        links.Should().ContainSingle();
        links[0].From.Should().Equal(1, 2);
        links[0].To.Should().Equal(11);
        links[0].Confidence.Should().Be(0.5);
    }

    [Fact]
    public void AnUntaggedWordIsPassedOverRatherThanRefused()
    {
        var links = Match([(1, []), (2, ["H430"])], [(11, "H430")]);

        links.Should().ContainSingle();
        links[0].From.Should().Equal(2);
    }
}
