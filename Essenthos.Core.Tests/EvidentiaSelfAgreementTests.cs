using Essenthos.Core.Loading.Links;
using Essenthos.Core.Loading.Links.Evidentia;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// Words placed where evidence of another kind than a learned rendering leaves one answer: a gloss in
/// context, a counterpart the verse holds once, and a British or possessive spelling read as the
/// modern one.
/// </summary>
public class EvidentiaSelfAgreementTests
{
    private static readonly EvidentiaAddress Genesis124 = new(1, 1, 24);

    private static readonly LanguagePackRegistry Packs = new([new EnglishLanguagePack(), new OriginalLanguagePack()]);

    [Fact]
    public void ANounGoesOnTheOneFreeWordWhoseGlossInContextNamesIt()
    {
        var livestock = Analysis(English(1, "livestock", "NOUN"));
        var created = Analysis(English(2, "created", "VERB"));
        var beast = Analysis(Hebrew(11, 1, "בְּהֵמָה", "H929", "subs"));
        var make = Analysis(Hebrew(12, 2, "בָּרָא", "H1254", "verb"));
        var glosses = new Dictionary<long, IReadOnlySet<string>>
        {
            [11] = new HashSet<string> { EnglishStemmer.Stem("livestock") },
            [12] = new HashSet<string> { EnglishStemmer.Stem("created") },
        };

        EvidentiaContextGloss.Resolve([livestock, created], [beast, make], glosses, [])
            .Select(proposal => (proposal.Source.Token.Id, proposal.Target.Token.Id))
            .Should().Equal((1L, 11L));
    }

    [Fact]
    public void AGlossNamingALexemeTheVerseWritesTwiceSaysNothingOfWhichOccurrence()
    {
        var sheep = Analysis(English(1, "sheep", "NOUN"));
        var one = Analysis(Hebrew(11, 1, "שֶׂה", "H7716", "subs"));
        var two = Analysis(Hebrew(12, 2, "שֶׂה", "H7716", "subs"));
        var glosses = new Dictionary<long, IReadOnlySet<string>>
        {
            [11] = new HashSet<string> { "sheep" },
            [12] = new HashSet<string> { "sheep" },
        };

        EvidentiaContextGloss.Resolve([sheep], [one, two], glosses, []).Should().BeEmpty();
    }

    [Fact]
    public void YouGoesOnTheOneFreePronounOfItsOwnKindAndNotWhereTwoWordsClaimIt()
    {
        var you = Analysis(English(1, "you", "PRON"));
        var dust = Analysis(English(2, "dust", "NOUN"));
        var you2 = Analysis(English(3, "you", "PRON"));
        var soil = Analysis(Hebrew(11, 1, "עָפָר", "H6083", "subs"));
        var atta = Analysis(Hebrew(12, 2, "אַתָּה", "H859", "prps"));
        var placed = new EvidentiaProposal(dust, soil, EvidentiaProposalKind.GlobalReviewKnownRendering, 0.8);

        EvidentiaCounterparts.Resolve([you, dust], [soil, atta], [placed])
            .Select(proposal => (proposal.Source.Token.Id, proposal.Target.Token.Id))
            .Should().Equal((1L, 12L));
        EvidentiaCounterparts.Resolve([you, dust, you2], [soil, atta], [placed]).Should().BeEmpty();
    }

    [Theory]
    [InlineData("neighbour's", "neighbor")]
    [InlineData("God’s", "god")]
    [InlineData("honourable", "honorable")]
    [InlineData("four", "four")]
    [InlineData("its", "its")]
    [InlineData("didn’t", "not")]
    [InlineData("won't", "not")]
    public void ASpellingTwoEditionsWriteTwoWaysBecomesOne(string surface, string expected) =>
        EnglishSpelling.Common(surface).Should().Be(expected);

    [Theory]
    [InlineData("‘Behold", "Behold")]
    [InlineData("“Lord,", "Lord")]
    [InlineData("them—do", "them")]
    [InlineData("sixty-six", "sixty-six")]
    public void AWordIsLookedUpWithoutTheMarksPrintedAgainstIt(string surface, string expected) =>
        Analysis(English(1, surface, "NOUN")).Token.Surface.Should().Be(expected);

    private static EvidentiaAnalysis Analysis(EvidentiaToken token)
    {
        Packs.TryAnalyse(token, out var analysis).Should().BeTrue();
        return analysis;
    }

    private static EvidentiaToken English(long id, string surface, string partOfSpeech) =>
        new(id, Genesis124, (int)id, surface, "eng", PartOfSpeech: partOfSpeech);

    private static EvidentiaToken Hebrew(long id, int position, string surface, string strong, string partOfSpeech) =>
        new(id, Genesis124, position, surface, "hbo", StrongNumber: strong, PartOfSpeech: partOfSpeech,
            Morphology: new Dictionary<string, string> { ["pos"] = partOfSpeech });
}
