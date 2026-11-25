using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Links;
using Essenthos.Core.Utils;
using Essenthos.Core.TextusReceptus;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Lexicon;

/// <summary>
/// Nestle is accented and the Textus Receptus is not. Comparing them as written would report every
/// word in the New Testament as a different word, which is the opposite of what two witnesses are
/// held for — so this is the comparison the whole loader rests on.
/// </summary>
public class GreekWitnessTests
{
    [Theory]
    [InlineData("Βίβλος", "βιβλος")]
    [InlineData("γενέσεως", "γενεσεως")]
    [InlineData("Ἰησοῦ", "ιησου")]
    [InlineData("Χριστοῦ", "χριστου")]
    [InlineData("υἱοῦ", "υιου")]
    [InlineData("Ἀβραάμ", "αβρααμ")]
    public void TheTwoEditionsWriteTheSameWordTheSameWay(string nestle, string receptus)
    {
        GreekLetters.Bare(nestle).Should().Be(GreekLetters.Bare(receptus));
    }

    [Fact]
    public void WordsThatDifferStillDiffer()
    {
        // Δαυείδ and Δαβίδ are the reading the two editions actually disagree on at Matthew 1:1,
        // and setting accents aside must not quietly make them the same word.
        GreekLetters.Bare("Δαυεὶδ").Should().NotBe(GreekLetters.Bare("δαβιδ"));
    }

    [Fact]
    public void NothingIsLostFromAWordThatCarriesNoAccents()
    {
        GreekLetters.Bare("αβρααμ").Should().Be("αβρααμ");
    }

    [Fact]
    public void NoAccentedLetterSurvivesTheStripping()
    {
        // The check the normalising version could not make. Every accented letter the loaded Greek
        // texts contain, run through at once: if the table has a gap, this is where it shows,
        // rather than in a link count nobody reads.
        const string everyAccentedLetter =
            "ἀἈἄἌᾄἂἆἎἁἉἅἍἃἋάᾴὰᾶᾷᾳἐἘἔἜἑἙἕἝἓἛέὲἠἨἤἬᾔἢἪἦἮᾖᾐἡἩἥἭἣἧᾗᾑήῄὴῆῇῃ" +
            "ἰἸἴἼἶἱἹἵἽἳἷίὶῖϊΐῒὀὈὄὌὂὁὉὅὍὃὋόὸῥῬὐὔὒὖὑὙὕὝὓὗὟύὺῦϋΰῢ" +
            "ὠὤὬὢὦὮᾠὡὩὥὭὧὯᾧώῴὼῶῷῳ";

        GreekLetters.Bare(everyAccentedLetter).Should().MatchRegex("^[αβγδεζηθικλμνξοπρστυφχψω]+$");
    }

    [Fact]
    public void AnOccurrenceOneEditionLacksTakesNoOtherOccurrence()
    {
        // John 3:16: Nestle has no αὐτοῦ, so by order alone Scrivener's αὐτοῦ took Nestle's only αὐτόν.
        var drafts = new List<GreekWitnessLinks.GreekDraft>();
        GreekWitnessLinks.Pair(
            [new(1, 19, "αὐτόν", "G846")],
            [new(101, 11, "αυτου", "G846"), new(102, 20, "αυτον", "G846")],
            drafts, ShippedStrongNumbers.Lists);

        drafts.Should().ContainSingle(d => d.Relation == LinkRelation.Equals)
            .Which.Should().Match<GreekWitnessLinks.GreekDraft>(d => d.From.Single() == 1 && d.To.Single() == 102);
        drafts.Should().ContainSingle(d => d.Relation == LinkRelation.Omits)
            .Which.To.Should().Equal(101);
    }

    [Fact]
    public void OccurrencesWrittenDifferentlyPairInOrderBetweenThoseWrittenAlike()
    {
        // Rev 11:11-like: one occurrence differs in case, the other is written alike, and the alike one comes second.
        var drafts = new List<GreekWitnessLinks.GreekDraft>();
        GreekWitnessLinks.Pair(
            [new(1, 3, "αὐτοῖς", "G846"), new(2, 7, "τοῦ", "G3588"), new(3, 9, "αὐτοῦ", "G846")],
            [new(101, 3, "αυτους", "G846"), new(102, 5, "του", "G3588"), new(103, 6, "του", "G3588"), new(104, 9, "αυτου", "G846")],
            drafts, ShippedStrongNumbers.Lists);

        drafts.Where(d => d.From.Count == 1 && d.To.Count == 1).Select(d => (d.From[0], d.To[0])).Should()
            .BeEquivalentTo([(1L, 101L), (3L, 104L), (2L, 102L)]);
        drafts.Should().ContainSingle(d => d.Relation == LinkRelation.Omits).Which.To.Should().Equal(103);
    }

    [Fact]
    public void ARepeatedWordIsTheOccurrenceAmongTheSameNeighbours()
    {
        // Luke 13:4: Scrivener prints δέκα καὶ ὀκτώ where Westcott-Hort prints δεκαοκτώ, so by order Westcott-Hort's only
        // καί took the καί of the numeral.
        var drafts = new List<GreekWitnessLinks.GreekDraft>();
        GreekWitnessLinks.Pair(
            Words(0, "ἢ G2228", "ἐκεῖνοι G1565", "οἱ G3588", "δεκαοκτὼ G1176", "ἐφ G1909", "οὓς G3739", "ἔπεσεν G4098",
                "ὁ G3588", "πύργος G4444", "ἐν G1722", "τῷ G3588", "Σιλωὰμ G4611", "καὶ G2532", "ἀπέκτεινεν G615",
                "αὐτούς G846"),
            Words(100, "η G2228", "εκεινοι G1565", "οι G3588", "δεκα G1176", "και G2532", "οκτω G3638", "εφ G1909",
                "ους G3739", "επεσεν G4098", "ο G3588", "πυργος G4444", "εν G1722", "τω G3588", "σιλωαμ G4611", "και G2532",
                "απεκτεινεν G615", "αυτους G846"),
            drafts, ShippedStrongNumbers.Lists);

        drafts.Should().ContainSingle(d => d.From.Contains(13)).Which.To.Should().Equal(115);
        drafts.Should().ContainSingle(d => d.To.Contains(105)).Which.Relation.Should().Be(LinkRelation.Omits);
    }

    [Fact]
    public void LeftoversOfDifferentPartsOfSpeechAreNotOneReading()
    {
        // 2 Pet 2:17: Westcott-Hort's καὶ ὁμίχλαι where Scrivener has νεφέλαι; δέ against γάρ is one reading.
        var drafts = new List<GreekWitnessLinks.GreekDraft>();
        GreekWitnessLinks.Pair(
            [new(1, 1, "δὲ", "G1161", """{"robinson": "CONJ"}"""), new(2, 5, "καὶ", "G2532", """{"robinson": "CONJ"}""")],
            [new(101, 1, "γαρ", "G1063", """{"robinson": "CONJ"}"""), new(102, 5, "νεφελαι", "G3507", """{"robinson": "N-NPF"}""")],
            drafts, ShippedStrongNumbers.Lists);

        drafts.Should().ContainSingle(d => d.Method == LinkMethod.Lexical)
            .Which.Should().Match<GreekWitnessLinks.GreekDraft>(d => d.From.Single() == 1 && d.To.Single() == 101);
        drafts.Should().Contain(d => d.Relation == LinkRelation.Expands && d.From.Single() == 2);
        drafts.Should().Contain(d => d.Relation == LinkRelation.Omits && d.To.Single() == 102);
    }

    private static List<GreekWitnessLinks.GreekWord> Words(long first, params string[] words) =>
        [.. words.Select((word, at) => word.Split(' ') is [var surface, var strong]
            ? new GreekWitnessLinks.GreekWord(first + at + 1, at + 1, surface, strong)
            : throw new ArgumentException(word))];
}
