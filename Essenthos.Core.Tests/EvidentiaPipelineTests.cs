using Essenthos.Core.Loading.Links.Evidentia;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

public class EvidentiaPipelineTests
{
    [Fact]
    public void Reciprocal_dictionary_sense_is_review_only_with_a_trace()
    {
        var source = new EvidentiaAnalysis(Token(1, "faith", "eng", verse: 1), "faith", "faith", null, EvidentiaWordClass.Content, LanguagePackCapability.Lemma);
        var target = new EvidentiaAnalysis(new EvidentiaToken(11, new EvidentiaAddress(40, 11, 1), 1, "אֱמוּנָה", "hbo", StrongNumber: "H530"), "אֱמוּנָה", null, null, EvidentiaWordClass.Content, LanguagePackCapability.Normalisation);
        var candidate = new EvidentiaCandidate(source, target,
        [
            new EvidentiaEvidence(EvidentiaEvidenceKind.ExactCanonicalAddress, 0.30, "canonical-frame"),
            new EvidentiaEvidence(EvidentiaEvidenceKind.DictionarySense, 0.34, "strong-entry:eng"),
        ]);

        var proposal = Assert.Single(new EvidentiaDictionaryProposalResolver().ResolveAdditional([candidate], []).Proposals);
        proposal.Kind.Should().Be(EvidentiaProposalKind.UniqueDictionarySenseReview);
        proposal.Trace!.Tier.Should().Be("review");
    }

    [Fact]
    public void Every_proposal_kind_has_a_stored_spelling()
    {
        foreach (var kind in Enum.GetValues<EvidentiaProposalKind>())
        {
            EvidentiaDecisionRecorder.Spelling(kind).Should().NotBeNullOrWhiteSpace(
                "a stored run records every proposal the measurement makes, attached words included");
        }
    }

    [Fact]
    public void A_pronoun_found_in_a_definition_neither_is_proposed_nor_keeps_a_noun_from_its_sense()
    {
        var target = new EvidentiaAnalysis(new EvidentiaToken(11, new EvidentiaAddress(40, 11, 1), 1, "בְּהֵמָה", "hbo", StrongNumber: "H929"), "בְּהֵמָה", null, "subs", EvidentiaWordClass.Content, LanguagePackCapability.Normalisation);
        EvidentiaCandidate Sense(long id, string text, string partOfSpeech) => new(
            new EvidentiaAnalysis(Token(id, text, "eng", verse: 1), text, text, partOfSpeech, EvidentiaWordClass.Content, LanguagePackCapability.PartOfSpeech),
            target,
            [
                new EvidentiaEvidence(EvidentiaEvidenceKind.ExactCanonicalAddress, 0.30, "canonical-frame"),
                new EvidentiaEvidence(EvidentiaEvidenceKind.DictionarySense, 0.34, "strong-entry:eng"),
            ]);

        var proposal = Assert.Single(new EvidentiaDictionaryProposalResolver()
            .ResolveAdditional([Sense(1, "livestock", "NOUN"), Sense(2, "that", "PRON")], []).Proposals);

        proposal.Source.Token.Id.Should().Be(1, "'that' occurs in the definition, it is not what the definition defines");
    }

    [Fact]
    public void Consecutive_unambiguous_word_edges_form_a_diagnostic_phrase()
    {
        var preview = Pipeline().Preview(new EvidentiaRequest(
            [new EvidentiaToken(1, new EvidentiaAddress(40, 11, 1), 1, "living", "eng"), new EvidentiaToken(2, new EvidentiaAddress(40, 11, 1), 2, "water", "eng")],
            [new EvidentiaToken(11, new EvidentiaAddress(40, 11, 1), 1, "living", "eng"), new EvidentiaToken(12, new EvidentiaAddress(40, 11, 1), 2, "water", "eng")]));

        var phrase = Assert.Single(preview.Phrases);
        phrase.Source.Select(token => token.Id).Should().Equal(1, 2);
        phrase.Target.Select(token => token.Id).Should().Equal(11, 12);
    }

    [Fact]
    public void One_word_to_contiguous_words_is_a_review_phrase()
    {
        var preview = Pipeline().Preview(new EvidentiaRequest(
            [new EvidentiaToken(1, new EvidentiaAddress(40, 11, 1), 1, "name", "eng")],
            [new EvidentiaToken(11, new EvidentiaAddress(40, 11, 1), 1, "name", "eng"), new EvidentiaToken(12, new EvidentiaAddress(40, 11, 1), 2, "name", "eng")]));

        preview.Phrases.Should().Contain(phrase => phrase.Reason == "one-to-many-review-edges");
    }
    [Fact]
    public void Morphology_ranks_an_existing_lexical_candidate_but_never_creates_one()
    {
        var preview = Pipeline().Preview(new EvidentiaRequest(
            [new EvidentiaToken(1, new EvidentiaAddress(40, 11, 1), 1, "faith", "eng", PartOfSpeech: "NOUN", Morphology: new Dictionary<string, string> { ["Number"] = "Sing" })],
            [new EvidentiaToken(2, new EvidentiaAddress(40, 11, 1), 1, "faith", "eng", PartOfSpeech: "noun", Morphology: new Dictionary<string, string> { ["number"] = "Sing" })]));

        var candidate = Assert.Single(preview.Candidates);
        Assert.Contains(candidate.Evidence, evidence => evidence.Kind == EvidentiaEvidenceKind.Morphology && evidence.Score > 0.06);
    }
    [Fact]
    public void AHebrewNounAgreesWithAnEnglishOneAlthoughTheTwoWitnessesSpellTheLabelDifferently()
    {
        var gloss = TargetGlossEvidenceSource.For(
        [
            new EvidentiaToken(2, new EvidentiaAddress(1, 1, 1), 1, "אֶרֶץ", "hbo", Gloss: "earth",
                PartOfSpeech: "subs", Morphology: new Dictionary<string, string> { ["pos"] = "subs", ["number"] = "sg" }),
        ]);
        var preview = Pipeline().Preview(new EvidentiaRequest(
            [new EvidentiaToken(1, new EvidentiaAddress(1, 1, 1), 1, "earth", "eng", PartOfSpeech: "NOUN",
                Morphology: new Dictionary<string, string> { ["Number"] = "Sing" })],
            [new EvidentiaToken(2, new EvidentiaAddress(1, 1, 1), 1, "אֶרֶץ", "hbo", Gloss: "earth",
                PartOfSpeech: "subs", Morphology: new Dictionary<string, string> { ["pos"] = "subs", ["number"] = "sg" })]),
            [gloss!]);

        var candidate = Assert.Single(preview.Candidates);
        Assert.Contains(candidate.Evidence, evidence =>
            evidence.Kind == EvidentiaEvidenceKind.Morphology
            && evidence.Source.Contains("universal-pos:noun")
            && evidence.Source.EndsWith("matching-features:1"));
    }

    [Fact]
    public void TheCurrentVerseOutranksAnIdenticalWordInANeighbour()
    {
        var preview = Pipeline().Preview(new EvidentiaRequest(
            [Token(1, "faith", "eng", verse: 2)],
            [Token(11, "faith", "eng", verse: 1), Token(12, "faith", "eng", verse: 2)]));

        preview.Status.Should().Be(EvidentiaPreviewStatus.ReadyForRules);
        preview.Candidates.Where(candidate => candidate.Source.Token.Id == 1)
            .Select(candidate => candidate.Target.Token.Id)
            .Should().StartWith(12, "the exact canonical address is stronger than a neighbour");
        preview.NeedsStatisticalFallback.Should().BeFalse();
    }

    [Fact]
    public void TheNeighbourWindowCrossesAChapterBoundaryWhereTheChapterLengthsAreKnown()
    {
        var lengths = new EvidentiaChapterLengths([(1, 31), (2, 25), (3, 24)]);
        var opening = lengths.Place(new EvidentiaToken(1, new EvidentiaAddress(40, 3, 1), 1, "faith", "eng"));
        var closing = lengths.Place(new EvidentiaToken(11, new EvidentiaAddress(40, 2, 25), 1, "faith", "eng"));
        var further = lengths.Place(new EvidentiaToken(12, new EvidentiaAddress(40, 2, 22), 1, "faith", "eng"));

        var preview = Pipeline().Preview(new EvidentiaRequest([opening], [closing, further]));

        opening.Address.DistanceTo(closing.Address).Should().Be(1);
        opening.Address.Should().Be(new EvidentiaAddress(40, 3, 1), "where a verse stands in its book is not part of which verse it is");
        preview.Candidates.Where(candidate => candidate.Evidence.Any(evidence => evidence.Kind == EvidentiaEvidenceKind.NeighbouringCanonicalAddress))
            .Should().ContainSingle().Which.Target.Token.Id.Should().Be(11, "3:1 is one verse from 2:25 and four from 2:22");
        lengths.Edges(2, EvidentiaDefaults.NeighbourVerseDistance).Should().Equal((1, 30), (1, 31), (3, 1), (3, 2));
        lengths.Edges(3, EvidentiaDefaults.NeighbourVerseDistance).Should().Equal((2, 24), (2, 25));
        new EvidentiaAddress(40, 3, 1).DistanceTo(new EvidentiaAddress(40, 2, 25)).Should().Be(int.MaxValue,
            "without the chapter lengths there is no telling how far apart two chapters' verses are");
    }

    [Fact]
    public void AnUnsupportedLanguageRequestsAFallbackWithoutInventingCandidates()
    {
        var preview = Pipeline().Preview(new EvidentiaRequest(
            [Token(1, "Glaube", "deu", verse: 2)],
            [Token(11, "faith", "eng", verse: 2)]));

        preview.Status.Should().Be(EvidentiaPreviewStatus.UnsupportedLanguage);
        preview.Candidates.Should().BeEmpty();
        preview.NeedsStatisticalFallback.Should().BeTrue();
        preview.Todos.Should().Contain(EvidentiaTodo.LanguagePack);
    }

    [Fact]
    public void AnUnsupportedLanguageDoesNotMatchItselfBySpelling()
    {
        var preview = Pipeline().Preview(new EvidentiaRequest(
            [Token(1, "und", "deu", verse: 2)],
            [Token(11, "und", "deu", verse: 2)]));

        preview.Candidates.Should().BeEmpty(
            "nobody has written a German pack, so nothing knows whether this is a particle");
        preview.Status.Should().Be(EvidentiaPreviewStatus.UnsupportedLanguage);
        preview.NeedsStatisticalFallback.Should().BeTrue();
    }

    [Fact]
    public void AHebrewArticleIsAFunctionWordOnTheTargetSideToo()
    {
        var registry = new LanguagePackRegistry([new OriginalLanguagePack()]);
        var article = new EvidentiaToken(1, new EvidentiaAddress(1, 1, 1), 1, "הָ", "hbo",
            Morphology: new Dictionary<string, string> { ["pos"] = "art" });
        var noun = article with { Id = 2, Surface = "אָרֶץ", PartOfSpeech = "subs" };

        registry.TryAnalyse(article with { PartOfSpeech = "art" }, out var analysed).Should().BeTrue();
        analysed.IsFunctionWord.Should().BeTrue();
        analysed.Capabilities.Should().HaveFlag(LanguagePackCapability.FunctionWords);

        registry.TryAnalyse(noun, out var content).Should().BeTrue();
        content.IsContentWord.Should().BeTrue();
    }

    [Fact]
    public void AFunctionWordDoesNotBecomeALexicalMatchOnlyBecauseItLooksTheSame()
    {
        var preview = Pipeline().Preview(new EvidentiaRequest(
            [Token(1, "the", "eng", verse: 2)],
            [Token(11, "the", "eng", verse: 2)]));

        preview.Candidates.Should().BeEmpty("the common address is a search boundary, not word evidence");
        preview.ContentCoverage.Should().Be(0);
        preview.NeedsStatisticalFallback.Should().BeTrue();
    }

    [Fact]
    public void ASharedStrongNumberIsAnAnchorEvenWhenTheScriptsDiffer()
    {
        var preview = Pipeline(new StrongNumberEvidenceSource()).Preview(new EvidentiaRequest(
            [Token(1, "неверию", "rus", verse: 21, strong: "g0570")],
            [Token(11, "ἀπιστίαν", "grc", verse: 21, strong: "G570")]));

        preview.Status.Should().Be(EvidentiaPreviewStatus.ReadyForRules,
            "the original-language pack carries source-provided lemma and morphology without pretending to translate Greek");
        preview.Candidates.Should().ContainSingle();
        preview.Candidates.Single().Evidence.Should().Contain(evidence =>
            evidence.Kind == EvidentiaEvidenceKind.SharedStrongNumber);
    }

    [Fact]
    public void AReaderLanguageStrongSenseOnlyMakesADictionaryCandidate()
    {
        var dictionary = new EvidentiaDictionarySenseEvidenceSource(
            new Dictionary<RenderingKey, HashSet<string>> { [new(EvidentiaFormKind.Surface, "віра")] = ["G4102"] }, "ukr");
        var preview = Pipeline().Preview(new EvidentiaRequest(
            [Token(1, "віра", "ukr", verse: 20)],
            [Token(11, "πίστις", "grc", verse: 20, strong: "G4102")]), [dictionary]);

        preview.Candidates.Should().ContainSingle();
        preview.Candidates.Single().Evidence.Should().Contain(evidence =>
            evidence.Kind == EvidentiaEvidenceKind.DictionarySense);
        preview.NeedsStatisticalFallback.Should().BeFalse("the exact dictionary candidate covers the only content source word");
    }

    [Fact]
    public void NoSourceStrongModeDoesNotUseAWordTagAsEvidence()
    {
        var preview = Pipeline(new StrongNumberEvidenceSource()).Preview(new EvidentiaRequest(
            [Token(1, "faith", "eng", verse: 20, strong: "G4102")],
            [Token(11, "πίστις", "grc", verse: 20, strong: "G4102")],
            AllowSourceStrongEvidence: false));

        preview.Candidates.Should().BeEmpty();
        preview.NeedsStatisticalFallback.Should().BeTrue();
    }

    [Fact]
    public void TargetGlossCreatesAReviewableCandidateWithoutClaimingASourceLink()
    {
        var target = new EvidentiaToken(
            11, new EvidentiaAddress(1, 1, 1), 1, "אֱמוּנָה", "hbo", Gloss: "faith");
        var gloss = TargetGlossEvidenceSource.For([target]);

        var preview = Pipeline().Preview(new EvidentiaRequest(
            [new EvidentiaToken(1, new EvidentiaAddress(1, 1, 1), 1, "faith", "eng")],
            [target]), [gloss!]);

        preview.Candidates.Should().ContainSingle();
        preview.Candidates.Single().Evidence.Should().Contain(evidence =>
            evidence.Kind == EvidentiaEvidenceKind.TargetGloss);
    }

    private static EvidentiaPipeline Pipeline(params IEvidentiaEvidenceSource[] sources) => new(
        new LanguagePackRegistry([new EnglishLanguagePack(), new UkrainianLanguagePack(), new RussianLanguagePack(), new OriginalLanguagePack()]),
        sources);

    private static EvidentiaToken Token(long id, string text, string language, int verse, string? strong = null) =>
        new(id, new EvidentiaAddress(40, 11, verse), 1, text, language, StrongNumber: strong);
}
