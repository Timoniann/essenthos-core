using Essenthos.Core.Loading.Links.Evidentia;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// German and Spanish write as words of their own what a Hebrew or Greek verb carries in its form: the
/// subject of <em>er sprach</em> and <em>él dijo</em>, the tense of <em>wird sagen</em> and <em>ha
/// dicho</em>. Each goes on the word its verb was placed on, found by the parse rather than by
/// position, and nowhere when the verb names another person or the original writes the pronoun itself.
/// </summary>
public class EvidentiaInflectionWordTests
{
    private static readonly EvidentiaAddress Genesis316 = new(1, 3, 16);
    private static readonly EvidentiaAddress Matthew2540 = new(40, 25, 40);

    private static readonly LanguagePackRegistry Packs =
        new([new GermanLanguagePack(), new SpanishLanguagePack(), new OriginalLanguagePack()]);

    [Fact]
    public void AGermanSubjectPronounGoesOnTheVerbWhoseEndingNamesIt()
    {
        var da = German(1, 1, "Da", "ADV");
        var sprach = German(2, 2, "sprach", "VERB");
        var er = German(3, 3, "er", "PRON", head: 2, relation: "nsubj", Personal("3", "Sing", "Masc"));
        var said = Hebrew(11, 1, "יֹּאמֶר", "H559", "verb", ("person", "p3"), ("number", "sg"), ("gender", "m"));

        Attach([da, sprach, er], [said], (sprach, said)).Should().BeEquivalentTo([(3L, 11L)]);
    }

    [Fact]
    public void APronounIsLeftAloneWhereTheVerbNamesAnotherPersonOrTheOriginalWritesItsOwn()
    {
        var ich = German(1, 1, "ich", "PRON", head: 2, relation: "nsubj", Personal("1", "Sing"));
        var sprach = German(2, 2, "sprach", "VERB");
        var er = German(3, 3, "er", "PRON", head: 4, relation: "nsubj", Personal("3", "Sing", "Masc"));
        var herrsche = German(4, 4, "herrsche", "VERB");
        var said = Hebrew(11, 1, "יֹּאמֶר", "H559", "verb", ("person", "p3"), ("number", "sg"), ("gender", "m"));
        var he = Hebrew(12, 2, "הוּא", "H1931", "prps", ("person", "p3"), ("number", "sg"), ("gender", "m"));
        var rule = Hebrew(13, 3, "יִמְשָׁל", "H4910", "verb", ("person", "p3"), ("number", "sg"), ("gender", "m"));

        Attach([ich, sprach, er, herrsche], [said, he, rule], (sprach, said), (herrsche, rule)).Should().BeEmpty();
    }

    [Fact]
    public void ATenseAuxiliaryGoesOnTheVerbItBelongsToHoweverFarItStands()
    {
        var er = German(1, 1, "er", "PRON", head: 6, relation: "nsubj", Personal("3", "Sing", "Masc"));
        var wird = German(2, 2, "wird", "AUX", head: 6, relation: "aux", lemma: "werden");
        var dein = German(3, 3, "dein", "DET");
        var herr = German(4, 4, "Herr", "NOUN");
        var nicht = German(5, 5, "nicht", "PART");
        var sein = German(6, 6, "herrschen", "VERB", features: [("VerbForm", "Inf")]);
        var kann = German(7, 7, "kann", "AUX", head: 6, relation: "aux", lemma: "können");
        var rule = Hebrew(11, 1, "יִמְשָׁל", "H4910", "verb", ("person", "p3"), ("number", "sg"), ("gender", "m"));

        Attach([er, wird, dein, herr, nicht, sein, kann], [rule], (sein, rule))
            .Should().BeEquivalentTo([(1L, 11L), (2L, 11L)]);
    }

    [Fact]
    public void ASpanishAuxiliaryAndSubjectGoOnTheGreekVerb()
    {
        var yo = Spanish(1, 1, "yo", "PRON", head: 3, relation: "nsubj", Personal("1", "Sing"));
        var he = Spanish(2, 2, "he", "AUX", head: 3, relation: "aux", lemma: "haber");
        var dicho = Spanish(3, 3, "dicho", "VERB", features: [("VerbForm", "Part")]);
        var es = Spanish(4, 4, "es", "AUX", head: 5, relation: "cop", lemma: "ser");
        var bueno = Spanish(5, 5, "bueno", "ADJ");
        var said = Greek(11, 1, "εἴρηκα", "G2046", "verb", ("person", "first"), ("number", "singular"));
        var good = Greek(12, 2, "καλόν", "G2570", "adj");

        Attach([yo, he, dicho, es, bueno], [said, good], (dicho, said), (bueno, good))
            .Should().BeEquivalentTo([(1L, 11L), (2L, 11L)]);
    }

    [Fact]
    public void AnAuxiliaryGoesOnTheGreekBeOfATenseWrittenInTwoWords()
    {
        var war = Spanish(1, 1, "había", "AUX", head: 2, relation: "aux", lemma: "haber");
        var gelegt = Spanish(2, 2, "sido", "VERB", features: [("VerbForm", "Part")]);
        var was = Greek(11, 1, "ἦν", "G1510", "verb", ("person", "third"), ("number", "singular"));
        var cast = Greek(12, 2, "βεβλημένος", "G906", "verb", ("mood", "participle"));

        Attach([war, gelegt], [was, cast], (gelegt, cast)).Should().BeEquivalentTo([(1L, 11L)]);
    }

    [Fact]
    public void ASubjectTheGreekWritesAsAnArticleOrTheParseReadOffAPrepositionIsNotAttached()
    {
        var y = Spanish(1, 1, "Y", "CCONJ");
        var ellos = Spanish(2, 2, "ellos", "PRON", head: 3, relation: "nsubj", Personal("3", "Plur", "Masc"));
        var callaron = Spanish(3, 3, "callaron", "VERB");
        var envio = Spanish(4, 4, "envió", "VERB");
        var a = Spanish(5, 5, "á", "ADP");
        var el = Spanish(6, 6, "él", "PRON", head: 4, relation: "nsubj", Personal("3", "Sing", "Masc"));
        var they = Greek(11, 1, "οἱ", "G3588", "det", ("case", "nominative"), ("number", "plural"));
        var but = Greek(12, 2, "δὲ", "G1161", "conj");
        var silent = Greek(13, 3, "ἡσύχασαν", "G2270", "verb", ("person", "third"), ("number", "plural"));
        var sent = Greek(14, 4, "ἀπέστειλεν", "G649", "verb", ("person", "third"), ("number", "singular"));

        Attach([y, ellos, callaron, envio, a, el], [they, but, silent, sent], (callaron, silent), (envio, sent))
            .Should().BeEmpty();
    }

    [Fact]
    public void AnAuxiliaryGoesOnTheOriginalsOwnAboutToAndNowhereWhenAnotherWordHoldsIt()
    {
        var habia = Spanish(1, 1, "había", "AUX", head: 3, relation: "aux", lemma: "haber");
        var de = Spanish(2, 2, "de", "ADP");
        var venir = Spanish(3, 3, "venir", "VERB", features: [("VerbForm", "Inf")]);
        var aboutTo = Greek(11, 1, "μέλλων", "G3195", "verb", ("mood", "participle"));
        var come = Greek(12, 2, "ἔρχεσθαι", "G2064", "verb", ("mood", "infinitive"));

        Attach([habia, de, venir], [aboutTo, come], (venir, come)).Should().BeEquivalentTo([(1L, 11L)]);
        Attach([habia, de, venir], [aboutTo, come], (venir, come), (de, aboutTo)).Should().BeEmpty();
    }

    [Fact]
    public void AnAuxiliaryIsLeftAloneWhereItsVerbWasPlacedOnAWordThatIsNoVerb()
    {
        var era = Spanish(1, 1, "era", "AUX", head: 2, relation: "aux", lemma: "ser");
        var sabado = Spanish(2, 2, "sábado", "VERB", features: [("VerbForm", "Part")]);
        var was = Greek(11, 1, "ἦν", "G1510", "verb", ("person", "third"), ("number", "singular"));
        var but = Greek(12, 2, "δὲ", "G1161", "conj");
        var still = Greek(13, 3, "ἔτι", "G2089", "adv");
        var sabbath = Greek(14, 4, "σάββατον", "G4521", "noun");

        Attach([era, sabado], [was, but, still, sabbath], (sabado, sabbath)).Should().BeEmpty();
    }

    [Fact]
    public void AnAuxiliaryTheParseHangsOnAFiniteVerbIsNotItsTense()
    {
        var he = Spanish(1, 1, "he", "AUX", head: 3, relation: "aux", lemma: "haber");
        var aqui = Spanish(2, 2, "aquí", "ADV");
        var descendera = Spanish(3, 3, "descenderá", "VERB", features: [("VerbForm", "Fin")]);
        var descend = Greek(11, 1, "καταβήσεται", "G2597", "verb", ("person", "third"), ("number", "singular"));

        Attach([he, aqui, descendera], [descend], (descendera, descend)).Should().BeEmpty();
    }

    [Fact]
    public void HabenBeforeAnInfinitiveIsAVerbOfItsOwn()
    {
        var ich = German(1, 1, "Ich", "PRON", head: 5, relation: "nsubj", Personal("1", "Sing"));
        var hatte = German(2, 2, "hatte", "AUX", head: 5, relation: "aux", lemma: "haben");
        var viel = German(3, 3, "viel", "ADV");
        var zu = German(4, 4, "zu", "PART");
        var schreiben = German(5, 5, "schreiben", "VERB", features: [("VerbForm", "Inf")]);
        var write = Greek(11, 1, "γράφειν", "G1125", "verb", ("mood", "infinitive"));

        Attach([ich, hatte, viel, zu, schreiben], [write], (schreiben, write)).Should().BeEmpty();
    }

    [Fact]
    public void APronounTheOriginalWritesAnywhereInTheVerseKeepsTheSubjectUnlessAnotherWordRendersIt()
    {
        var du = German(1, 1, "Du", "PRON", head: 6, relation: "nsubj", Personal("2", "Sing"));
        var aber = German(2, 2, "aber", "ADV");
        var nach = German(3, 3, "nach", "ADP");
        var deiner = German(4, 4, "deiner", "DET");
        var gnade = German(5, 5, "Barmherzigkeit", "NOUN");
        var verliessest = German(6, 6, "verließest", "VERB");
        var you = Hebrew(11, 1, "אַתָּה", "H859", "prps", ("person", "p2"), ("number", "sg"), ("gender", "m"));
        var mercy = Hebrew(12, 2, "רַחֲמֶיךָ", "H7356", "subs");
        var many = Hebrew(13, 3, "רַבִּים", "H7227", "adjv");
        var not = Hebrew(14, 4, "לֹא", "H3808", "nega");
        var them = Hebrew(15, 5, "אֹתָם", "H853", "prep");
        var left = Hebrew(16, 6, "עֲזַבְתָּם", "H5800", "verb", ("person", "p2"), ("number", "sg"), ("gender", "m"));
        IReadOnlyList<EvidentiaToken> german = [du, aber, nach, deiner, gnade, verliessest];
        IReadOnlyList<EvidentiaToken> hebrew = [you, mercy, many, not, them, left];

        Attach(german, hebrew, (verliessest, left)).Should().BeEmpty();
        Attach(german, hebrew, (verliessest, left), (aber, you)).Should().BeEquivalentTo([(1L, 16L)]);
    }

    [Fact]
    public void TheEmptyEsAndAPronounThatIsNoSubjectAreNotAttached()
    {
        var es = German(1, 1, "es", "PRON", head: 2, relation: "nsubj", Personal("3", "Sing", "Neut"));
        var geschah = German(2, 2, "geschah", "VERB");
        var ihn = German(3, 3, "ihn", "PRON", head: 2, relation: "obj", Personal("3", "Sing", "Masc"));
        var happened = Hebrew(11, 1, "יְהִי", "H1961", "verb", ("person", "p3"), ("number", "sg"), ("gender", "m"));

        Attach([es, geschah, ihn], [happened], (geschah, happened)).Should().BeEmpty();
    }

    [Fact]
    public void TheClaimCreditsTheModelWhoseParseItRestsOn()
    {
        var sprach = German(1, 1, "sprach", "VERB");
        var er = German(2, 2, "er", "PRON", head: 1, relation: "nsubj", Personal("3", "Sing", "Masc"));
        var said = Hebrew(11, 1, "יֹּאמֶר", "H559", "verb", ("person", "p3"), ("number", "sg"), ("gender", "m"));

        var attached = EvidentiaAttachedWords.Resolve(
            [Analysis(sprach), Analysis(er)],
            [Analysis(said)],
            [new EvidentiaProposal(Analysis(sprach), Analysis(said), EvidentiaProposalKind.GlobalReviewKnownRendering, 0.8)]);

        attached.Should().ContainSingle().Which.Trace!.Rationale.Should()
            .StartWith("SubjectPronoun of 'sprach'").And.Contain("German-HDT").And.Contain("CC BY-NC-SA 4.0");
    }

    private static List<(long From, long To)> Attach(
        IReadOnlyList<EvidentiaToken> source,
        IReadOnlyList<EvidentiaToken> target,
        params (EvidentiaToken Source, EvidentiaToken Target)[] placed)
    {
        var proposals = placed
            .Select(pair => new EvidentiaProposal(
                Analysis(pair.Source), Analysis(pair.Target), EvidentiaProposalKind.GlobalReviewKnownRendering, 0.8))
            .ToList();
        return EvidentiaAttachedWords.Resolve(
                EvidentiaAuxiliaryWords.Mark([.. source.Select(Analysis)]),
                [.. target.Select(Analysis)],
                proposals)
            .Select(proposal => (proposal.Source.Token.Id, proposal.Target.Token.Id))
            .ToList();
    }

    private static EvidentiaAnalysis Analysis(EvidentiaToken token)
    {
        Packs.TryAnalyse(token, out var analysis).Should().BeTrue();
        return analysis;
    }

    private static (string, string)[] Personal(string person, string number, string? gender = null) =>
        gender is null
            ? [("PronType", "Prs"), ("Person", person), ("Number", number)]
            : [("PronType", "Prs"), ("Person", person), ("Number", number), ("Gender", gender)];

    private static EvidentiaToken German(long id, int position, string surface, string partOfSpeech,
        long? head = null, string? relation = null, (string Name, string Value)[]? features = null, string? lemma = null) =>
        Translation(id, Genesis316, position, surface, "deu", partOfSpeech, head, relation, features, lemma);

    private static EvidentiaToken Spanish(long id, int position, string surface, string partOfSpeech,
        long? head = null, string? relation = null, (string Name, string Value)[]? features = null, string? lemma = null) =>
        Translation(id, Matthew2540, position, surface, "spa", partOfSpeech, head, relation, features, lemma);

    private static EvidentiaToken Translation(long id, EvidentiaAddress address, int position, string surface,
        string language, string partOfSpeech, long? head, string? relation, (string Name, string Value)[]? features,
        string? lemma) =>
        new(id, address, position, surface, language, Trailer: " ", Lemma: lemma, PartOfSpeech: partOfSpeech,
            Morphology: features?.ToDictionary(feature => feature.Name, feature => feature.Value),
            SyntacticHead: head, Relation: relation);

    private static EvidentiaToken Hebrew(
        long id, int position, string surface, string strong, string partOfSpeech,
        params (string Name, string Value)[] morphology) =>
        new(id, Genesis316, position, surface, "hbo", StrongNumber: strong, PartOfSpeech: partOfSpeech,
            Morphology: morphology.Append(("pos", partOfSpeech)).ToDictionary(feature => feature.Item1, feature => feature.Item2));

    private static EvidentiaToken Greek(
        long id, int position, string surface, string strong, string partOfSpeech,
        params (string Name, string Value)[] morphology) =>
        new(id, Matthew2540, position, surface, "grc", StrongNumber: strong, PartOfSpeech: partOfSpeech,
            Morphology: morphology.Append(("pos", partOfSpeech)).ToDictionary(feature => feature.Item1, feature => feature.Item2));
}
