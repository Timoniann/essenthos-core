using Essenthos.Core.ClearBible;
using Essenthos.Core.Loading.Links;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// How Clear Bible's Greek words become Nestle 1904's. Their alignment of the Berean is over the
/// Berean Greek New Testament, which is not Nestle 1904: joined by counting, every word after a word
/// one edition has and the other has not lands one place late, and the rows read
/// <em>preaching → βάπτισμα</em>, which is exactly the shape of a correct row.
/// </summary>
public class ClearBibleEditionJoinTests
{
    private static WitnessForm[] Forms(params (string Letters, string Strong)[] words) =>
        [.. words.Select(word => new WitnessForm(word.Letters, word.Strong))];

    /// <summary>
    /// Mark 1:4. BGNT reads ἐρήμῳ καὶ κηρύσσων where Nestle has no καί; the καί has no counterpart
    /// and every word after it is placed on its own word, not on the next.
    /// </summary>
    [Fact]
    public void PlacesEveryWordAfterAWordNestleLacksOnItsOwnWord()
    {
        var bgnt = Forms(
            ("εγενετο", "G1096"), ("ιωαννης", "G2491"), ("ο", "G3588"), ("βαπτιζων", "G907"),
            ("εν", "G1722"), ("τη", "G3588"), ("ερημω", "G2048"), ("και", "G2532"),
            ("κηρυσσων", "G2784"), ("βαπτισμα", "G908"), ("μετανοιας", "G3341"), ("εις", "G1519"),
            ("αφεσιν", "G859"), ("αμαρτιων", "G266"));
        var nestle = Forms(
            ("εγενετο", "G1096"), ("ιωανης", "G2491"), ("ο", "G3588"), ("βαπτιζων", "G907"),
            ("εν", "G1722"), ("τη", "G3588"), ("ερημω", "G2048"), ("κηρυσσων", "G2784"),
            ("βαπτισμα", "G908"), ("μετανοιας", "G3341"), ("εις", "G1519"), ("αφεσιν", "G859"),
            ("αμαρτιων", "G266"));

        ClearBibleLinkLoader.Counterparts(bgnt, nestle)
            .Should().Equal(0, 1, 2, 3, 4, 5, 6, -1, 7, 8, 9, 10, 11, 12);
    }

    /// <summary>
    /// A different word standing in the same place is a variant, not a spelling, even where the
    /// letters are one apart. What Clear Bible's annotators said about their word says nothing about
    /// ours.
    /// </summary>
    [Fact]
    public void RefusesADifferentWordStandingInTheSamePlace() =>
        ClearBibleLinkLoader.Counterparts(
                Forms(("ηλθεν", "G2064"), ("και", "G2532"), ("ειπεν", "G2036")),
                Forms(("ηλθεν", "G2064"), ("κατ", "G2596"), ("ειπεν", "G2036")))
            .Should().Equal(0, -1, 2);

    /// <summary>The Berean Greek respells Nestle's Δαυείδ; it is still the same word.</summary>
    [Theory]
    [InlineData("G1138", "G1138")]
    [InlineData("", "G1138")]
    public void PlacesAWordTheOtherEditionSpellsDifferently(string theirs, string ours) =>
        ClearBibleLinkLoader.Counterparts(
                Forms(("υιου", "G5207"), ("δαυιδ", theirs)),
                Forms(("υιου", "G5207"), ("δαυειδ", ours)))
            .Should().Equal(0, 1);

    [Fact]
    public void JoinsTheBereanSetOnItsOwnGreekEdition()
    {
        var berean = ClearBibleSet.All().Single(set => set.From == "BSB");

        berean.Join.Should().Be(ClearBibleJoin.Edition);
        berean.Source.Should().EndWith("BGNT.tsv");
    }
}
