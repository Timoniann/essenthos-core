using Essenthos.Core.Loading.Links.Evidentia;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// A learned habit that pairs an auxiliary word with the wrong kind of word is refused, whatever the
/// index says. The scores are the shape of the real King James entries: <em>they</em> lands on the
/// Greek article 60% of the time and <em>out</em> on מִן 32%, because the phrase links taught them so.
/// </summary>
public class EvidentiaAuxiliaryWordTests
{
    private static readonly EvidentiaAddress Mark120 = new(41, 1, 20);
    private static readonly EvidentiaAddress Genesis416 = new(1, 4, 16);

    [Fact]
    public void ASubjectPronounIsNotPlacedOnTheArticleOfANoun()
    {
        var proposals = Propose(
            [
                English(1, Mark120, 1, "they", "PRON", ("PronType", "Prs"), ("Case", "Nom"), ("Number", "Plur")),
                English(2, Mark120, 2, "left", "VERB"),
                English(3, Mark120, 3, "father", "NOUN"),
            ],
            [
                Greek(11, Mark120, 1, "ἀφέντες", "G863", "verb", "nominative"),
                Greek(12, Mark120, 2, "τὸν", "G3588", "det", "accusative"),
                Greek(13, Mark120, 3, "πατέρα", "G3962", "noun", "accusative"),
            ],
            ("they", "G3588", 0.47), ("left", "G863", 0.60), ("father", "G3962", 0.65));

        proposals.Should().NotContain((1L, 12L), "the Greek subject is the verb's ending and τὸν belongs to πατέρα");
        proposals.Should().Contain([(2L, 11L), (3L, 13L)]);
    }

    [Fact]
    public void OnlyAnArticleIsPlacedOnTheArticleOfANoun()
    {
        var proposals = Propose(
            [
                English(1, Mark120, 1, "those", "PRON", ("PronType", "Dem"), ("Number", "Plur")),
                English(2, Mark120, 2, "things", "NOUN", ("Number", "Plur")),
            ],
            [
                Greek(11, Mark120, 1, "τοῦ", "G3588", "det", "genitive"),
                Greek(12, Mark120, 2, "ὅσα", "G3745", "pron", "accusative"),
                Greek(13, Mark120, 3, "τὰς", "G3588", "det", "accusative"),
                Greek(14, Mark120, 4, "ἀκάνθας", "G173", "noun", "accusative"),
            ],
            ("those", "G3588", 0.40), ("things", "G3588", 0.35));

        proposals.Should().BeEmpty("τοῦ and τὰς belong to the nouns after them, whatever the phrase links taught the index");
    }

    [Fact]
    public void APersonalPronounTakesTheGreekOccurrenceOfItsOwnNumberAndCase()
    {
        var proposals = Propose(
            [
                English(1, Mark120, 1, "him", "PRON", ("PronType", "Prs"), ("Case", "Acc"), ("Number", "Sing"), ("Person", "3")),
                English(2, Mark120, 2, "their", "PRON", ("PronType", "Prs"), ("Poss", "Yes"), ("Number", "Plur"), ("Person", "3")),
            ],
            [
                GreekPronoun(11, Mark120, 1, "αὐτῶν", "genitive", "plural"),
                GreekPronoun(12, Mark120, 2, "αὐτοὺς", "accusative", "plural"),
                GreekPronoun(13, Mark120, 3, "αὐτοῦ", "genitive", "singular"),
            ],
            ("him", "G846", 0.55), ("their", "G846", 0.55));

        proposals.Should().BeEquivalentTo([(1L, 13L), (2L, 11L)],
            "him is one man and their is a genitive plural, whichever occurrence stands nearer");
    }

    [Fact]
    public void AReverentialHimTheParserTookForANameIsStillOneMan()
    {
        var proposals = Propose(
            [English(1, Mark120, 1, "Him", "PROPN")],
            [
                GreekPronoun(11, Mark120, 1, "αὐτοὺς", "accusative", "plural"),
                GreekPronoun(12, Mark120, 2, "αὐτὸν", "accusative", "singular"),
            ],
            ("him", "G846", 0.55));

        proposals.Should().Equal((1L, 12L));
    }

    [Fact]
    public void ASubjectPronounMayStandOnAnArticleThatStandsForAPerson()
    {
        var proposals = Propose(
            [
                English(1, Mark120, 1, "they", "PRON", ("PronType", "Prs"), ("Case", "Nom")),
                English(2, Mark120, 2, "said", "VERB"),
            ],
            [
                Greek(11, Mark120, 1, "οἱ", "G3588", "det", "nominative"),
                Greek(12, Mark120, 2, "δὲ", "G1161", "conj", null),
                Greek(13, Mark120, 3, "εἶπαν", "G3004", "verb", null),
            ],
            ("they", "G3588", 0.47), ("said", "G3004", 0.60));

        proposals.Should().Contain((1L, 11L), "in οἱ δὲ εἶπαν the article is the subject");
    }

    [Fact]
    public void AnArticleFollowedByAParticleStillBelongsToTheNounAfterIt()
    {
        var target = EvidentiaAuxiliaryWords.Mark(
        [
            Analysis(Greek(11, Mark120, 1, "ἡ", "G3588", "det", "nominative")),
            Analysis(Greek(12, Mark120, 2, "δὲ", "G1161", "conj", null)),
            Analysis(Greek(13, Mark120, 3, "πενθερὰ", "G3994", "noun", "nominative")),
        ]);

        target[0].Role.Should().Be(EvidentiaAuxiliaryRole.NominalArticle);
    }

    [Fact]
    public void AVerbParticleIsNotPlacedOnAPreposition()
    {
        var proposals = Propose(
            [
                English(1, Genesis416, 1, "Cain", "PROPN"),
                English(2, Genesis416, 2, "went", "VERB"),
                English(3, Genesis416, 3, "out", "ADV"),
                English(4, Genesis416, 4, "from", "ADP"),
            ],
            [
                Hebrew(11, Genesis416, 1, "יֵּצֵא", "H3318", "verb"),
                Hebrew(12, Genesis416, 2, "קַיִן", "H7014", "nmpr"),
                Hebrew(13, Genesis416, 3, "מִ", "H4480", "prep"),
            ],
            ("cain", "H7014", 0.65), ("went", "H3318", 0.60), ("out", "H4480", 0.34));

        proposals.Should().NotContain((3L, 13L), "the particle belongs to יצא and מִן is BSB's 'from'");
    }

    [Fact]
    public void AWordLeftUnplacedByTheRuleIsCountedAsThatAndNotAsAPolicyRefusal()
    {
        var source = new[]
        {
            English(1, Genesis416, 1, "went", "VERB"),
            English(2, Genesis416, 2, "out", "ADV"),
        };
        var packs = new LanguagePackRegistry([new EnglishLanguagePack(), new OriginalLanguagePack()]);
        var preview = new EvidentiaPipeline(packs, [new Renderings([("out", "H4480", 0.34)])])
            .Preview(new EvidentiaRequest(source, [Hebrew(13, Genesis416, 1, "מִן", "H4480", "prep")]));

        var words = EvidentiaSourceWordAccount.Classify(
            source, token => packs.TryAnalyse(token, out var analysis) ? analysis : null,
            preview.Candidates, [], new HashSet<long>(), new HashSet<(long, long)>(), new HashSet<long>(), 1, 4);

        words.Single(word => word.SourceWordId == 2).Outcome.Should().Be(EvidentiaWordOutcome.AuxiliaryWordOffItsKind);
        EvidentiaSourceWordAccount.Of(words).AuxiliaryWordOffItsKind.Should().Be(1);
    }

    [Fact]
    public void AVerbParticleGoesWithTheVerbWhenTheVerbIsFree()
    {
        var proposals = Propose(
            [
                English(1, Genesis416, 1, "Come", "VERB"),
                English(2, Genesis416, 2, "out", "ADP"),
                English(3, Genesis416, 3, "of", "ADP"),
                English(4, Genesis416, 4, "ark", "NOUN"),
            ],
            [
                Hebrew(11, Genesis416, 1, "צֵא", "H3318", "verb"),
                Hebrew(12, Genesis416, 2, "מִן", "H4480", "prep"),
                Hebrew(13, Genesis416, 3, "תֵּבָה", "H8392", "subs"),
            ],
            ("out", "H4480", 0.34), ("out", "H3318", 0.32), ("ark", "H8392", 0.65));

        proposals.Should().Contain((2L, 11L));
        proposals.Should().NotContain((2L, 12L));
    }

    [Fact]
    public void AParticleAfterAPrepositionIsNotAVerbParticle()
    {
        var source = EvidentiaAuxiliaryWords.Mark(
        [
            Analysis(English(1, Genesis416, 1, "destroy", "VERB")),
            Analysis(English(2, Genesis416, 2, "from", "ADP")),
            Analysis(English(3, Genesis416, 3, "off", "ADP")),
        ]);

        source[2].Role.Should().Be(EvidentiaAuxiliaryRole.None, "in 'from off the face' the particle is the preposition's");
    }

    [Fact]
    public void AnAuxiliaryVerbIsNotPlacedOnAPrepositionAndAPossessiveIsNotAnAuxiliary()
    {
        var proposals = Propose(
            [
                English(1, Genesis416, 1, "had", "AUX"),
                English(2, Genesis416, 2, "brother's", "AUX"),
            ],
            [
                Hebrew(11, Genesis416, 1, "עַל", "H5921", "prep"),
                Hebrew(12, Genesis416, 2, "אָחִיו", "H251", "subs"),
            ],
            ("had", "H5921", 0.47), ("brother's", "H251", 0.65));

        proposals.Should().NotContain((1L, 11L));
        proposals.Should().Contain((2L, 12L), "the parser reads 's as 'is', which does not make a noun an auxiliary");
    }

    private static IReadOnlyList<(long Source, long Target)> Propose(
        IReadOnlyList<EvidentiaToken> source,
        IReadOnlyList<EvidentiaToken> target,
        params (string Form, string Strong, double Score)[] renderings)
    {
        var packs = new LanguagePackRegistry([new EnglishLanguagePack(), new OriginalLanguagePack()]);
        var preview = new EvidentiaPipeline(packs, [new Renderings(renderings)])
            .Preview(new EvidentiaRequest(source, target, AllowSourceStrongEvidence: false));
        return new EvidentiaKnownRenderingProposalResolver().ResolveGlobally(preview.Candidates).Proposals
            .Select(proposal => (proposal.Source.Token.Id, proposal.Target.Token.Id))
            .ToList();
    }

    private static EvidentiaAnalysis Analysis(EvidentiaToken token)
    {
        var packs = new LanguagePackRegistry([new EnglishLanguagePack(), new OriginalLanguagePack()]);
        packs.TryAnalyse(token, out var analysis).Should().BeTrue();
        return analysis;
    }

    private static EvidentiaToken English(
        long id, EvidentiaAddress address, int position, string surface, string partOfSpeech,
        params (string Name, string Value)[] features) =>
        new(id, address, position, surface, "eng", PartOfSpeech: partOfSpeech,
            Morphology: features.ToDictionary(feature => feature.Name, feature => feature.Value));

    private static EvidentiaToken Greek(
        long id, EvidentiaAddress address, int position, string surface, string strong, string partOfSpeech, string? grammaticalCase) =>
        new(id, address, position, surface, "grc", StrongNumber: strong, PartOfSpeech: partOfSpeech,
            Morphology: grammaticalCase is null ? null : new Dictionary<string, string> { ["case"] = grammaticalCase });

    private static EvidentiaToken GreekPronoun(
        long id, EvidentiaAddress address, int position, string surface, string grammaticalCase, string number) =>
        new(id, address, position, surface, "grc", StrongNumber: "G846", PartOfSpeech: "pron",
            Morphology: new Dictionary<string, string> { ["case"] = grammaticalCase, ["number"] = number });

    private static EvidentiaToken Hebrew(
        long id, EvidentiaAddress address, int position, string surface, string strong, string partOfSpeech) =>
        new(id, address, position, surface, "hbo", StrongNumber: strong, PartOfSpeech: partOfSpeech);

    private sealed class Renderings((string Form, string Strong, double Score)[] renderings) : IEvidentiaEvidenceSource
    {
        public IEnumerable<EvidentiaEvidence> Find(EvidentiaAnalysis source, EvidentiaAnalysis target) =>
            renderings
                .Where(rendering => source.IsContentWord
                    && rendering.Form.Equals(source.Token.Surface, StringComparison.OrdinalIgnoreCase)
                    && rendering.Strong == target.Token.StrongNumber)
                .Select(rendering => new EvidentiaEvidence(
                    EvidentiaEvidenceKind.KnownRendering, rendering.Score, "test",
                    new EvidentiaEvidenceSupport(20, (rendering.Score - 0.20) / 0.45, 0)));
    }
}
