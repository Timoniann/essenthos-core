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
        var links = Match([(1, ["H1121"])], [(11, "H1121"), (12, "H1121")]);

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

    private static StrongNumberMatch.WitnessWord Marker(long id, bool suffixed = false) =>
        new(id, ObjectMarker.Number, suffixed);

    /// <summary>
    /// Genesis 1:1 as the Synodal's numbering tags it: <em>сотворил</em> <c>H1254 H853</c>, the
    /// King James convention of hanging the object marker on the verb, and <em>и</em> before
    /// <em>землю</em> <c>H853</c> alone. Neither אֵת has a suffix, so neither is a word a translation
    /// renders: the verb reaches its verb and nothing else, settled, and the conjunction reaches
    /// nothing and is counted.
    /// </summary>
    [Fact]
    public void GenesisOneOneSendsTheVerbToTheVerbAndTheConjunctionNowhere()
    {
        var links = StrongNumberMatch.Verse(
            [
                new StrongNumberMatch.TaggedWord(1, []),
                new StrongNumberMatch.TaggedWord(2, ["H7225"]),
                new StrongNumberMatch.TaggedWord(3, ["H1254", "H853"]),
                new StrongNumberMatch.TaggedWord(4, ["H430"]),
                new StrongNumberMatch.TaggedWord(5, ["H8064"]),
                new StrongNumberMatch.TaggedWord(6, ["H853"]),
                new StrongNumberMatch.TaggedWord(7, ["H776"]),
            ],
            [
                new StrongNumberMatch.WitnessWord(11, "H9003"),
                new StrongNumberMatch.WitnessWord(12, "H7225"),
                new StrongNumberMatch.WitnessWord(13, "H1254"),
                new StrongNumberMatch.WitnessWord(14, "H430"),
                Marker(15),
                new StrongNumberMatch.WitnessWord(16, "H9009"),
                new StrongNumberMatch.WitnessWord(17, "H8064"),
                new StrongNumberMatch.WitnessWord(18, "H9000"),
                Marker(19),
                new StrongNumberMatch.WitnessWord(20, "H9009"),
                new StrongNumberMatch.WitnessWord(21, "H776"),
            ],
            NoRedirects,
            out var tally);

        var created = links.Single(link => link.From.Contains(3));
        created.From.Should().Equal(3);
        created.To.Should().Equal(13);
        created.Confidence.Should().Be(0.9);
        created.Kind.Should().Be(StrongMatchKind.Unambiguous);

        links.Should().NotContain(link => link.From.Contains(6));
        links.Should().NotContain(link => link.To.Contains(15) || link.To.Contains(19));
        links.Should().OnlyContain(link => link.Confidence == 0.9);
        tally.Unmatched.Should().Be(1);
        tally.Phrases.Should().Be(0);
    }

    /// <summary>
    /// With a suffix the marker is a pronoun — אֹתוֹ is <em>его</em> — and a tag naming it alone is
    /// about that word, not about the bare markers beside it.
    /// </summary>
    [Fact]
    public void ASuffixedMarkerIsAPronounAndIsReached()
    {
        var links = StrongNumberMatch.Verse(
            [new StrongNumberMatch.TaggedWord(1, ["H853"])],
            [Marker(11), Marker(12, suffixed: true)],
            NoRedirects,
            out _);

        links.Should().ContainSingle();
        links[0].To.Should().Equal(12);
        links[0].Confidence.Should().Be(0.9);
    }

    /// <summary>
    /// Beside another number the marker is dropped even where it has a suffix: the tag is the verb's,
    /// and the pronoun is somebody else's word.
    /// </summary>
    [Fact]
    public void AMarkerBesideAnotherNumberIsDroppedEvenWithASuffix()
    {
        var links = Match([(1, ["H5414", "H853"])], [(11, "H5414")]);
        var suffixed = StrongNumberMatch.Verse(
            [new StrongNumberMatch.TaggedWord(1, ["H5414", "H853"])],
            [new StrongNumberMatch.WitnessWord(11, "H5414"), Marker(12, suffixed: true)],
            NoRedirects,
            out _);

        links.Should().ContainSingle().Which.To.Should().Equal(11);
        suffixed.Should().ContainSingle();
        suffixed[0].To.Should().Equal(11);
        suffixed[0].Confidence.Should().Be(0.9);
    }

    /// <summary>
    /// A verb that no longer carries the marker is one more occurrence of its own number, grouped with
    /// the others — here two verbs against two, paired in order, where the marker had kept them apart.
    /// </summary>
    [Fact]
    public void AVerbWithoutItsMarkerJoinsTheOtherOccurrencesOfItsNumber()
    {
        var links = StrongNumberMatch.Verse(
            [new StrongNumberMatch.TaggedWord(1, ["H1254", "H853"]), new StrongNumberMatch.TaggedWord(2, ["H1254"])],
            [new StrongNumberMatch.WitnessWord(11, "H1254"), Marker(12), new StrongNumberMatch.WitnessWord(13, "H1254")],
            NoRedirects,
            out _);

        links.Select(link => (link.From.Single(), link.To.Single())).Should().Equal((1L, 11L), (2L, 13L));
        links.Should().OnlyContain(link => link.Kind == StrongMatchKind.Paired);
    }

    /// <summary>
    /// Two markers and nothing else is still the marker alone, and reaches what a tag naming it once
    /// would: the pronoun, and never the bare one.
    /// </summary>
    [Fact]
    public void ATagNamingOnlyTheMarkerTwiceIsStillTheMarkerAlone()
    {
        var links = StrongNumberMatch.Verse(
            [new StrongNumberMatch.TaggedWord(1, ["H853", "H853"])],
            [Marker(11), Marker(12, suffixed: true)],
            NoRedirects,
            out _);

        links.Should().ContainSingle().Which.To.Should().Equal(12);
    }
}
