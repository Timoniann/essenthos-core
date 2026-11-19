using Essenthos.Core.Loading.Links.Evidentia;
using Essenthos.Core.Products;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Evidentia;

/// <summary>
/// The Ukrainian and Russian attached-word rules: each places a small word on the original word its placed head's rendering
/// shows it renders or spells out, and nowhere else; and the word lists they read refuse a malformed line.
/// </summary>
public class EvidentiaSlavicAttachedWordsTests
{
    private static readonly EvidentiaAddress Verse = new(51, 1, 1);

    private static readonly IEvidentiaSlavicWords Ukrainian = new UkrainianWords(EvidentiaWordList.Checked("ukr",
    [
        Row("subject-pronoun", "ми", person: "1", number: "pl", original: "G1473"),
        Row("case-preposition", "у", grammaticalCase: "dative"),
        Row("negation", "не", original: "G3756|G3361"),
        Row("copula", "був", original: "G1510"),
        Row("relative", "хто", original: "G3956"),
    ]));

    [Theory]
    [InlineData("ukr")]
    [InlineData("rus")]
    public void TheShippedWordListsAreWellFormed(string language)
    {
        var words = EvidentiaWordList.Read(new ProductStore(TestResources.Shipped(".")), language);

        words.Rows.Should().Contain(row => row.Kind == EvidentiaWordKinds.SubjectPronoun);
        words.Rows.Should().Contain(row => row.Kind == EvidentiaWordKinds.CasePreposition);
    }

    [Theory]
    [InlineData("article", "ми", "1", "pl", null, null, "x", "kind 'article'")]
    [InlineData("subject-pronoun", "ми", "4", "pl", null, null, "x", "person '4'")]
    [InlineData("subject-pronoun", "ми", "1", null, null, null, "x", "needs its person and number")]
    [InlineData("negation", "не", null, null, null, null, "x", "needs the original words")]
    [InlineData("negation", "не", null, null, null, "G37x", "x", "is not Strong numbers")]
    [InlineData("case-preposition", "у", null, null, null, null, "x", "needs the case")]
    [InlineData("subject-pronoun", "Ми", "1", "pl", null, null, "x", "not one lower-case word")]
    [InlineData("subject-pronoun", "ми", "1", "pl", null, null, " ", "reason is empty")]
    public void AMalformedLineIsRefusedWithItsFault(
        string kind, string form, string? person, string? number, string? grammaticalCase, string? original, string reason, string fault)
    {
        var act = () => EvidentiaWordList.Checked("ukr", [new EvidentiaWordRow(kind, form, person, number, null, grammaticalCase, original, reason)]);

        act.Should().Throw<FormatException>().WithMessage($"*row 1*{fault}*");
    }

    [Fact]
    public void ARepeatedLineIsRefused()
    {
        var act = () => EvidentiaWordList.Checked("ukr", [Row("negation", "не", original: "G3756"), Row("negation", "не", original: "G3756")]);

        act.Should().Throw<FormatException>().WithMessage("*row 2*repeats*");
    }

    [Fact]
    public void ASubjectPronounGoesOnTheFiniteVerbItsHeadRendersWhereItsPersonAndNumberAgree()
    {
        // ми говоримо over λαλοῦμεν.
        var we = Word(1, "ми", head: 2, relation: "nsubj");
        var speak = Word(2, "говоримо");
        var verb = Greek(101, "λαλοῦμεν", "G2980", ("pos", "verb"), ("mood", "indicative"), ("person", "first"), ("number", "plural"));

        Place([we, speak], [we], [verb], (2, 101)).Should().ContainSingle()
            .Which.Should().Be(new EvidentiaSlavicPlacement(1, 101, EvidentiaWordKinds.SubjectPronoun, 2));
    }

    [Fact]
    public void ASubjectPronounIsNotPlacedOnAVerbOfAnotherPersonOrBesideAWrittenPronounOrTheArticleOfHoDe()
    {
        var we = Word(1, "ми", head: 2, relation: "nsubj");
        var speak = Word(2, "говоримо");
        var third = Greek(101, "λαλεῖ", "G2980", ("pos", "verb"), ("mood", "indicative"), ("person", "third"), ("number", "singular"));
        var written = Greek(100, "ἡμεῖς", "G1473", ("pos", "pron"), ("case", "nominative"));
        var first = Greek(101, "λαλοῦμεν", "G2980", ("pos", "verb"), ("mood", "indicative"), ("person", "first"), ("number", "plural"));

        var article = Greek(98, "οἱ", "G3588", ("pos", "det"), ("case", "nominative"));
        var de = Greek(99, "δὲ", "G1161", ("pos", "conj"));

        Place([we, speak], [we], [third], (2, 101)).Should().BeEmpty();
        Place([we, speak], [we], [article, de, first], (2, 101)).Should().BeEmpty();
        Place([we, speak], [we], [written, first], (2, 101)).Should().BeEmpty();
    }

    [Fact]
    public void ASubjectPronounIsNotPlacedFarFromItsVerbNorWhereTheVerseWritesItsPronounOrAnotherUnrenderedVerbOfItsPerson()
    {
        var we = Word(1, "ми", head: 2, relation: "nsubj");
        var speak = Word(2, "говоримо");
        var weFar = Word(1, "ми", head: 5, relation: "nsubj");
        var speakFar = Word(5, "говоримо");
        var first = Greek(101, "λαλοῦμεν", "G2980", ("pos", "verb"), ("mood", "indicative"), ("person", "first"), ("number", "plural"));
        var writtenFar = Greek(109, "ἡμεῖς", "G1473", ("pos", "pron"), ("case", "nominative"));
        var unrendered = Greek(104, "ἐσμέν", "G1510", ("pos", "verb"), ("mood", "indicative"), ("person", "first"), ("number", "plural"));

        Place([weFar, speakFar], [weFar], [first], (5, 101)).Should().BeEmpty();
        Place([we, speak], [we], [first, writtenFar], (2, 101)).Should().BeEmpty();
        Place([we, speak], [we], [first, unrendered], (2, 101)).Should().BeEmpty();
    }

    [Fact]
    public void APrepositionGoesOnTheFreePrepositionBeforeItsNounsRenderingButNeverOnTheObjectMarker()
    {
        // у світлі over ἐν τῷ φωτί.
        var at = Word(1, "у", head: 2, relation: "case");
        var light = Word(2, "світлі");
        var en = Greek(101, "ἐν", "G1722", ("pos", "prep"));
        var article = Greek(102, "τῷ", "G3588", ("pos", "det"), ("case", "dative"));
        var noun = Greek(103, "φωτί", "G5457", ("pos", "noun"), ("case", "dative"));
        var marker = Greek(101, "אֶת", "H853", ("pos", "prep"));

        Place([at, light], [at], [en, article, noun], (2, 103)).Should().ContainSingle()
            .Which.Should().Be(new EvidentiaSlavicPlacement(1, 101, "preposition-before-noun", 2));
        Place([at, light], [at], [en, article, noun], (2, 103), (9, 101)).Should().NotContain(placement => placement.Original == 101);
        Place([at, light], [at], [marker, noun], (2, 103)).Should().NotContain(placement => placement.Original == 101);
    }

    [Fact]
    public void ANegationGoesOnTheFreeNegationBeforeItsHeadsRendering()
    {
        var not = Word(1, "не", head: 2, relation: "advmod");
        var see = Word(2, "бачив");
        var ou = Greek(101, "οὐχ", "G3756", ("pos", "adv"));
        var verb = Greek(102, "ἑώρακεν", "G3708", ("pos", "verb"), ("mood", "indicative"), ("person", "third"), ("number", "singular"));

        Place([not, see], [not], [ou, verb], (2, 102)).Should().ContainSingle()
            .Which.Should().Be(new EvidentiaSlavicPlacement(1, 101, EvidentiaWordKinds.Negation, 2));
    }

    [Fact]
    public void ACopulaGoesOnBeOnlyWhereTheParseMakesItTheCopula()
    {
        var was = Word(1, "був", head: 2, relation: "cop");
        var auxiliary = Word(1, "був", head: 2, relation: "aux");
        var light = Word(2, "світлом");
        var be = Greek(101, "ἦν", "G1510", ("pos", "verb"), ("mood", "indicative"), ("person", "third"), ("number", "singular"));
        var noun = Greek(102, "φῶς", "G5457", ("pos", "noun"), ("case", "nominative"));

        Place([was, light], [was], [be, noun], (2, 102)).Should().ContainSingle().Which.Original.Should().Be(101);
        Place([auxiliary, light], [auxiliary], [be, noun], (2, 102)).Should().BeEmpty();
    }

    [Fact]
    public void ARelativeGoesOnAnArticularParticipleUnlessAWordListedForItStandsBeforeTheArticle()
    {
        // хто любить over ὁ ἀγαπῶν; кожен хто вірує over πᾶς ὁ πιστεύων.
        var who = Word(1, "хто", head: 2, relation: "nsubj");
        var loves = Word(2, "любить");
        var article = Greek(101, "ὁ", "G3588", ("pos", "det"), ("case", "nominative"));
        var participle = Greek(102, "ἀγαπῶν", "G25", ("pos", "verb"), ("mood", "participle"), ("case", "nominative"));
        var every = Greek(100, "πᾶς", "G3956", ("pos", "adj"), ("case", "nominative"));

        Place([who, loves], [who], [article, participle], (2, 102)).Should().ContainSingle()
            .Which.Should().Be(new EvidentiaSlavicPlacement(1, 102, EvidentiaWordKinds.Relative, 2));
        Place([who, loves], [who], [every, article, participle], (2, 102)).Should().BeEmpty();
    }

    [Fact]
    public void TheShippedRuleListIsWellFormedAndNamesNoHebrewRule()
    {
        var store = new ProductStore(TestResources.Shipped("."));

        EvidentiaSlavicRuleRow.Read(store, "ukr").Keys.Should().OnlyContain(key => key.Original == "grc");
        EvidentiaSlavicRuleRow.Read(store, "rus").Keys.Should().OnlyContain(key => key.Original == "grc");
    }

    [Theory]
    [InlineData("pol", "grc", "subject-pronoun", "attached_pronoun", "x", "language 'pol'")]
    [InlineData("ukr", "lat", "subject-pronoun", "attached_pronoun", "x", "original 'lat'")]
    [InlineData("ukr", "grc", "possessive", "attached_pronoun", "x", "rule 'possessive'")]
    [InlineData("ukr", "grc", "subject-pronoun", "attached-pronoun", "x", "tier 'attached-pronoun'")]
    [InlineData("ukr", "grc", "subject-pronoun", "safe", "x", "tiers already use")]
    [InlineData("ukr", "grc", "subject-pronoun", "attached_pronoun", " ", "reason is empty")]
    public void AMalformedRuleLineIsRefusedWithItsFault(string language, string original, string rule, string tier, string reason, string fault)
    {
        var act = () => EvidentiaSlavicRuleRow.Checked([new EvidentiaSlavicRuleRow(language, original, rule, tier, reason)], "ukr");

        act.Should().Throw<FormatException>().WithMessage($"*row 1*{fault}*");
    }

    [Fact]
    public void ARunPlacesOnlyTheRulesListedForTheOriginalsLanguageUnderTheirTierBesideItsOwnHead()
    {
        // ми говоримо over λαλοῦμεν, the verb placed by the run; не is listed in the word file but not in the rules.
        var we = Word(1, "ми", head: 2, relation: "nsubj");
        var speak = Word(2, "говоримо");
        var not = Word(3, "не", head: 2, relation: "advmod");
        var negation = Greek(100, "οὐ", "G3756", ("pos", "adv"));
        var verb = Greek(101, "λαλοῦμεν", "G2980", ("pos", "verb"), ("mood", "indicative"), ("person", "first"), ("number", "plural"));
        var head = new EvidentiaProposal(Analysis(speak), Analysis(verb), EvidentiaProposalKind.GlobalStableKnownRendering, 0.9,
            new EvidentiaDecisionTrace("safe", "learned", []));
        var run = new EvidentiaSlavicRun(Ukrainian, new Dictionary<(string Original, string Rule), string>
        {
            [("grc", EvidentiaWordKinds.SubjectPronoun)] = "attached_pronoun",
            [("hbo", EvidentiaWordKinds.Negation)] = "attached_negation",
        });

        var proposals = EvidentiaSlavicAttachedWords.Proposals(run, [Analysis(we), Analysis(speak), Analysis(not)],
            [Analysis(negation), Analysis(verb)], [head]);

        var pronoun = proposals.Should().ContainSingle().Which;
        pronoun.Source.Token.Id.Should().Be(1);
        pronoun.Target.Token.Id.Should().Be(101);
        pronoun.Kind.Should().Be(EvidentiaProposalKind.AttachedWord);
        pronoun.Trace!.Tier.Should().Be("attached_pronoun");
        pronoun.Head.Should().Be(head);
        EvidentiaAttachedWords.Attachment(pronoun).Should().Be(EvidentiaAttachment.SubjectPronoun);
        EvidentiaAttachedWords.Safe([head], proposals).Should().NotContain((1, 101));
    }

    private static EvidentiaAnalysis Analysis(EvidentiaToken token) =>
        new(token, token.Surface, token.Lemma, token.PartOfSpeech, EvidentiaWordClass.Content, LanguagePackCapability.None);

    private static List<EvidentiaSlavicPlacement> Place(
        IReadOnlyList<EvidentiaToken> verse,
        IReadOnlyList<EvidentiaToken> unplaced,
        IReadOnlyList<EvidentiaToken> original,
        params (long Word, long Original)[] placed) =>
        [.. EvidentiaSlavicAttachedWords.Place(Ukrainian, verse, unplaced, original,
            placed.GroupBy(pair => pair.Word).ToDictionary(group => group.Key, group => (IReadOnlyList<long>)[.. group.Select(pair => pair.Original)]))];

    private static EvidentiaToken Word(long id, string surface, long? head = null, string? relation = null) =>
        new(id, Verse, (int)id, surface, "ukr", SyntacticHead: head, Relation: relation);

    private static EvidentiaToken Greek(long id, string surface, string strong, params (string Key, string Value)[] morphology)
    {
        var features = morphology.ToDictionary(feature => feature.Key, feature => feature.Value);
        return new EvidentiaToken(id, Verse, (int)(id - 100), surface, strong.StartsWith('H') ? "hbo" : "grc",
            StrongNumber: strong, PartOfSpeech: features.GetValueOrDefault("pos"), Morphology: features);
    }

    private static EvidentiaWordRow Row(
        string kind, string form, string? person = null, string? number = null, string? grammaticalCase = null, string? original = null) =>
        new(kind, form, person, number, null, grammaticalCase, original, "test");
}
