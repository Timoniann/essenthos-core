using Essenthos.Core.Loading.Links.Evidentia;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// A grammatical word follows the word it belongs to, onto the place the witness's convention gives
/// it: a Hebrew article with its noun, <em>and</em> on the ו before the word it joins, an auxiliary on
/// its verb.
/// </summary>
public class EvidentiaAttachedWordTests
{
    private static readonly EvidentiaAddress Genesis122 = new(1, 1, 22);
    private static readonly EvidentiaAddress Mark114 = new(41, 1, 14);

    private static readonly LanguagePackRegistry Packs = new([new EnglishLanguagePack(), new OriginalLanguagePack()]);

    [Fact]
    public void AnEnglishArticleGoesOnTheArticleOfTheHebrewNounItsNounWasPlacedOn()
    {
        var the = English(1, Genesis122, 1, "the", "DET");
        var waters = English(2, Genesis122, 2, "waters", "NOUN");
        var article = Hebrew(11, Genesis122, 1, "הַ", "H9009", "art");
        var water = Hebrew(12, Genesis122, 2, "מַּיִם", "H4325", "subs");

        var attached = Attach([the, waters], [article, water], (waters, water));

        attached.Should().Equal((1L, 11L));
    }

    [Fact]
    public void AndGoesOnTheVavWrittenBeforeTheWordItJoinsPastAPrepositionAndAnArticle()
    {
        var and = English(1, Genesis122, 1, "and", "CCONJ");
        var birds = English(2, Genesis122, 2, "birds", "NOUN");
        var vav = Hebrew(11, Genesis122, 1, "וּ", "H9000", "conj");
        var preposition = Hebrew(12, Genesis122, 2, "בְ", "H9003", "prep");
        var article = Hebrew(13, Genesis122, 3, "הָ", "H9009", "art");
        var bird = Hebrew(14, Genesis122, 4, "עֹוף", "H5775", "subs");

        var attached = Attach([and, birds], [vav, preposition, article, bird], (birds, bird));

        attached.Should().Equal((1L, 11L));
    }

    [Fact]
    public void AndIsNotPlacedWhereNoConjunctionStandsBeforeTheWordItJoins()
    {
        var and = English(1, Genesis122, 1, "and", "CCONJ");
        var said = English(2, Genesis122, 2, "said", "VERB");
        var god = Hebrew(11, Genesis122, 1, "אֱלֹהִים", "H430", "subs");
        var say = Hebrew(12, Genesis122, 2, "לֵאמֹר", "H559", "verb");

        Attach([and, said], [god, say], (said, say)).Should().BeEmpty();
    }

    [Fact]
    public void AnAuxiliaryGoesOnItsVerbButNotAcrossANegation()
    {
        var will = English(1, Genesis122, 1, "will", "AUX");
        var rule = English(2, Genesis122, 2, "rule", "VERB");
        var must = English(3, Genesis122, 3, "must", "AUX");
        var not = English(4, Genesis122, 4, "not", "PART");
        var eat = English(5, Genesis122, 5, "eat", "VERB");
        var ruleVerb = Hebrew(11, Genesis122, 1, "יִמְשָׁל", "H4910", "verb");
        var negation = Hebrew(12, Genesis122, 2, "לֹא", "H3808", "nega");
        var eatVerb = Hebrew(13, Genesis122, 3, "תֹאכַל", "H398", "verb");

        var attached = Attach([will, rule, must, not, eat], [ruleVerb, negation, eatVerb], (rule, ruleVerb), (eat, eatVerb));

        attached.Should().Equal((1L, 11L));
    }

    [Fact]
    public void AnArticleWhoseNounWasNotPlacedIsNotPlacedEither()
    {
        var the = English(1, Genesis122, 1, "the", "DET");
        var waters = English(2, Genesis122, 2, "waters", "NOUN");
        var water = Hebrew(12, Genesis122, 2, "מַּיִם", "H4325", "subs");

        Attach([the, waters], [water]).Should().BeEmpty();
    }

    [Fact]
    public void AgainstGreekAndGoesOnKaiAndTheOnTheArticleBeforeTheNoun()
    {
        var and = English(1, Mark114, 1, "and", "CCONJ");
        var the = English(2, Mark114, 2, "the", "DET");
        var gospel = English(3, Mark114, 3, "gospel", "NOUN");
        var kai = Greek(11, Mark114, 1, "καὶ", "G2532", "conj", "καί");
        var article = Greek(12, Mark114, 2, "τὸ", "G3588", "det");
        var gospelNoun = Greek(13, Mark114, 3, "εὐαγγέλιον", "G2098", "noun");

        Attach([and, the, gospel], [kai, article, gospelNoun], (gospel, gospelNoun))
            .Should().BeEquivalentTo([(1L, 11L), (2L, 12L)]);
    }

    [Fact]
    public void AgainstGreekTheOfAnAdjectiveAndItsNounGoesOnTheNounsArticle()
    {
        var the = English(1, Mark114, 1, "the", "DET");
        var unclean = English(2, Mark114, 2, "unclean", "ADJ");
        var spirit = English(3, Mark114, 3, "spirit", "NOUN");
        var article = Greek(11, Mark114, 1, "τὸ", "G3588", "det");
        var spiritNoun = Greek(12, Mark114, 2, "πνεῦμα", "G4151", "noun");
        var repeated = Greek(13, Mark114, 3, "τὸ", "G3588", "det");
        var uncleanAdjective = Greek(14, Mark114, 4, "ἀκάθαρτον", "G169", "adj");

        Attach([the, unclean, spirit], [article, spiritNoun, repeated, uncleanAdjective],
                (unclean, uncleanAdjective), (spirit, spiritNoun))
            .Should().Equal((1L, 11L));
    }

    [Fact]
    public void AndIsNotPlacedOnAGreekConjunctionThatSubordinates()
    {
        var and = English(1, Mark114, 1, "And", "CCONJ");
        var satan = English(2, Mark114, 2, "Satan", "PROPN");
        var ei = Greek(11, Mark114, 1, "εἰ", "G1487", "conj", "εἰ");
        var satanNoun = Greek(12, Mark114, 2, "Σατανᾶς", "G4567", "noun");

        Attach([and, satan], [ei, satanNoun], (satan, satanNoun)).Should().BeEmpty();
    }

    [Fact]
    public void ASubjectPronounGoesOnTheVerbWhoseEndingNamesItAndNotOnAParticiple()
    {
        var he = English(1, Genesis122, 1, "he", "PRON");
        var said = English(2, Genesis122, 2, "said", "VERB");
        var she = English(3, Genesis122, 3, "she", "PRON");
        var going = English(4, Genesis122, 4, "went", "VERB");
        var say = Hebrew(11, Genesis122, 1, "יֹּאמֶר", "H559", "verb", ("person", "p3"), ("number", "sg"), ("gender", "m"));
        var walking = Hebrew(12, Genesis122, 2, "הֹלֶכֶת", "H1980", "verb", ("tense", "ptca"), ("number", "sg"), ("gender", "f"));

        Attach([he, said, she, going], [say, walking], (said, say), (going, walking)).Should().Equal((1L, 11L));
    }

    [Fact]
    public void ASubjectPronounIsNotPlacedOnAVerbOfAnotherPersonOrBesideAPronounTheOriginalWritesApart()
    {
        var she = English(1, Genesis122, 1, "she", "PRON");
        var said = English(2, Genesis122, 2, "said", "VERB");
        var he = English(3, Genesis122, 3, "he", "PRON");
        var rules = English(4, Genesis122, 4, "rules", "VERB");
        var say = Hebrew(11, Genesis122, 1, "יֹּאמֶר", "H559", "verb", ("person", "p3"), ("number", "sg"), ("gender", "m"));
        var pronoun = Hebrew(12, Genesis122, 2, "הוּא", "H1931", "prps", ("person", "p3"), ("number", "sg"), ("gender", "m"));
        var rule = Hebrew(13, Genesis122, 3, "יִמְשָׁל", "H4910", "verb", ("person", "p3"), ("number", "sg"), ("gender", "m"));

        Attach([she, said, he, rules], [say, pronoun, rule], (said, say), (rules, rule)).Should().BeEmpty();
    }

    [Fact]
    public void APossessiveGoesOnTheNounWhoseSuffixNamesIt()
    {
        var his = English(1, Genesis122, 1, "his", "PRON");
        var sons = English(2, Genesis122, 2, "sons", "NOUN");
        var my = English(3, Genesis122, 3, "my", "PRON");
        var hand = English(4, Genesis122, 4, "hand", "NOUN");
        var hisSons = Hebrew(11, Genesis122, 1, "בָנָיו", "H1121", "subs", ("suffixPerson", "p3"), ("suffixNumber", "sg"), ("suffixGender", "m"));
        var hisHand = Hebrew(12, Genesis122, 2, "יָדוֹ", "H3027", "subs", ("suffixPerson", "p3"), ("suffixNumber", "sg"), ("suffixGender", "m"));

        Attach([his, sons, my, hand], [hisSons, hisHand], (sons, hisSons), (hand, hisHand)).Should().Equal((1L, 11L));
    }

    [Fact]
    public void AnObjectAfterAnUnplacedPrepositionGoesOnThePrepositionWrittenAfterTheVerb()
    {
        var said = English(1, Genesis122, 1, "said", "VERB");
        var to = English(2, Genesis122, 2, "to", "ADP");
        var him = English(3, Genesis122, 3, "him", "PRON");
        var say = Hebrew(11, Genesis122, 1, "יֹּאמֶר", "H559", "verb", ("person", "p3"), ("number", "sg"), ("gender", "m"));
        var toHim = Hebrew(12, Genesis122, 2, "לוֹ", "H9005", "prep", ("suffixPerson", "p3"), ("suffixNumber", "sg"), ("suffixGender", "m"));

        Attach([said, to, him], [say, toHim], (said, say)).Should().BeEquivalentTo([(3L, 12L), (2L, 12L)]);
    }

    [Fact]
    public void AgainstGreekASubjectGoesOnTheVerbOfItsPersonAndNumber()
    {
        var they = English(1, Mark114, 1, "they", "PRON");
        var went = English(2, Mark114, 2, "went", "VERB");
        var he = English(3, Mark114, 3, "he", "PRON");
        var saw = English(4, Mark114, 4, "saw", "VERB");
        var go = Greek(11, Mark114, 1, "ἀπῆλθον", "G565", "verb", morphology: [("person", "third"), ("number", "plural")]);
        var see = Greek(12, Mark114, 2, "εἶδον", "G3708", "verb", morphology: [("person", "third"), ("number", "plural")]);

        Attach([they, went, he, saw], [go, see], (went, go), (saw, see)).Should().Equal((1L, 11L));
    }

    [Fact]
    public void APrepositionGoesOnThePrepositionBeforeItsNounButNeverOnTheObjectMarkerAndOfStaysUnplaced()
    {
        var from = English(1, Genesis122, 1, "from", "ADP");
        var the = English(2, Genesis122, 2, "the", "DET");
        var ground = English(3, Genesis122, 3, "ground", "NOUN");
        var with = English(4, Genesis122, 4, "with", "ADP");
        var wife = English(5, Genesis122, 5, "wife", "NOUN");
        var of = English(6, Genesis122, 6, "of", "ADP");
        var man = English(7, Genesis122, 7, "man", "NOUN");
        var min = Hebrew(11, Genesis122, 1, "מִן", "H4480", "prep");
        var article = Hebrew(12, Genesis122, 2, "הָ", "H9009", "art");
        var soil = Hebrew(13, Genesis122, 3, "אֲדָמָה", "H127", "subs");
        var marker = Hebrew(14, Genesis122, 4, "אֶת", "H853", "prep");
        var woman = Hebrew(15, Genesis122, 5, "אִשְׁתּוֹ", "H802", "subs");
        var person = Hebrew(16, Genesis122, 6, "אָדָם", "H120", "subs");

        Attach([from, the, ground, with, wife, of, man], [min, article, soil, marker, woman, person],
                (ground, soil), (wife, woman), (man, person))
            .Should().BeEquivalentTo([(1L, 11L), (2L, 12L)]);
    }

    [Fact]
    public void OfGoesOnTheNounAConstructDependsOnAndNowhereElse()
    {
        var face = English(1, Genesis122, 1, "face", "NOUN");
        var of = English(2, Genesis122, 2, "of", "ADP");
        var the = English(3, Genesis122, 3, "the", "DET");
        var deep = English(4, Genesis122, 4, "deep", "NOUN");
        var all = English(5, Genesis122, 5, "all", "DET");
        var of2 = English(6, Genesis122, 6, "of", "ADP");
        var days = English(7, Genesis122, 7, "days", "NOUN");
        var faceNoun = Hebrew(11, Genesis122, 1, "פְּנֵי", "H6440", "subs", ("state", "c"));
        var article = Hebrew(12, Genesis122, 2, "תְ", "H9009", "art");
        var deepNoun = Hebrew(13, Genesis122, 3, "הֹום", "H8415", "subs", ("state", "a"));
        var whole = Hebrew(14, Genesis122, 4, "כָּל", "H3605", "subs", ("state", "c"));
        var day = Hebrew(15, Genesis122, 5, "יְמֵי", "H3117", "subs", ("state", "c"));

        Attach([face, of, the, deep, all, of2, days], [faceNoun, article, deepNoun, whole, day],
                (face, faceNoun), (deep, deepNoun), (days, day))
            .Should().BeEquivalentTo([(2L, 13L), (3L, 12L)]);
    }

    [Fact]
    public void AWordFollowsAHeadThatIsItselfAttached()
    {
        var and = English(1, Genesis122, 1, "and", "CCONJ");
        var he = English(2, Genesis122, 2, "he", "PRON");
        var said = English(3, Genesis122, 3, "said", "VERB");
        var to = English(4, Genesis122, 4, "to", "ADP");
        var him = English(5, Genesis122, 5, "him", "PRON");
        var vav = Hebrew(11, Genesis122, 1, "וַ", "H9000", "conj");
        var say = Hebrew(12, Genesis122, 2, "יֹּאמֶר", "H559", "verb", ("person", "p3"), ("number", "sg"), ("gender", "m"));
        var toHim = Hebrew(13, Genesis122, 3, "לוֹ", "H9005", "prep", ("suffixPerson", "p3"), ("suffixNumber", "sg"), ("suffixGender", "m"));

        Attach([and, he, said, to, him], [vav, say, toHim], (said, say))
            .Should().BeEquivalentTo([(1L, 11L), (2L, 12L), (4L, 13L), (5L, 13L)]);
    }

    [Fact]
    public void ToGoesOnTheLamedBeforeAnInfinitiveAndNowhereWhenItsVerbIsNotOne()
    {
        var to = English(1, Genesis122, 1, "to", "PART");
        var separate = English(2, Genesis122, 2, "separate", "VERB");
        var to2 = English(3, Genesis122, 3, "to", "PART");
        var eat = English(4, Genesis122, 4, "eat", "VERB");
        var lamed = Hebrew(11, Genesis122, 1, "לְ", "H9005", "prep");
        var divide = Hebrew(12, Genesis122, 2, "הַבְדִּיל", "H914", "verb", ("tense", "infc"));
        var eats = Hebrew(13, Genesis122, 3, "תֹאכַל", "H398", "verb", ("tense", "impf"));

        Attach([to, separate, to2, eat], [lamed, divide, eats], (separate, divide), (eat, eats)).Should().Equal((1L, 11L));
    }

    [Fact]
    public void ThereWasGoesOnTheVerbBeWrittenBesideItsSubjectAndAndFollowsIt()
    {
        var and = English(1, Genesis122, 1, "and", "CCONJ");
        var there = English(2, Genesis122, 2, "there", "PRON") with { SyntacticHead = 3, Relation = "expl" };
        var was = English(3, Genesis122, 3, "was", "VERB") with { Lemma = "be", Relation = "root" };
        var evening = English(4, Genesis122, 4, "evening", "NOUN") with { SyntacticHead = 3, Relation = "nsubj" };
        var vav = Hebrew(11, Genesis122, 1, "וַ", "H9000", "conj");
        var be = Hebrew(12, Genesis122, 2, "יְהִי", "H1961", "verb");
        var dusk = Hebrew(13, Genesis122, 3, "עֶרֶב", "H6153", "subs");

        Attach([and, there, was, evening], [vav, be, dusk], (evening, dusk))
            .Should().BeEquivalentTo([(3L, 12L), (2L, 12L), (1L, 11L)]);
    }

    [Fact]
    public void ACopulaIsNotPlacedWhereTheOriginalWritesNoBeBesideThePredicate()
    {
        var was = English(1, Genesis122, 1, "was", "AUX") with { Lemma = "be", SyntacticHead = 2, Relation = "cop" };
        var good = English(2, Genesis122, 2, "good", "ADJ");
        var be = Hebrew(11, Genesis122, 1, "הָיָה", "H1961", "verb");
        var said = Hebrew(12, Genesis122, 2, "אָמַר", "H559", "verb");
        var fine = Hebrew(13, Genesis122, 3, "טוֹב", "H2896", "adjv");

        Attach([was, good], [be, said, fine], (good, fine)).Should().BeEmpty();
    }

    [Fact]
    public void ASubordinatorGoesOnTheConjunctionDirectlyBeforeItsVerbButNotOnTheOriginalsAnd()
    {
        var because = English(1, Genesis122, 1, "because", "SCONJ") with { SyntacticHead = 2, Relation = "mark" };
        var rested = English(2, Genesis122, 2, "rested", "VERB");
        var when = English(3, Genesis122, 3, "when", "SCONJ") with { SyntacticHead = 4, Relation = "mark" };
        var slept = English(4, Genesis122, 4, "slept", "VERB");
        var ki = Hebrew(11, Genesis122, 1, "כִּי", "H3588", "conj");
        var rest = Hebrew(12, Genesis122, 2, "שָׁבַת", "H7673", "verb");
        var vav = Hebrew(13, Genesis122, 3, "וַ", "H9000", "conj");
        var sleep = Hebrew(14, Genesis122, 4, "יִּישָׁן", "H3462", "verb");

        Attach([because, rested, when, slept], [ki, rest, vav, sleep], (rested, rest), (slept, sleep)).Should().Equal((1L, 11L));
    }

    [Fact]
    public void AnArticleAndAPossessiveShareTheSafeTierOfTheirNounAndAConjunctionDoesNot()
    {
        var and = English(1, Genesis122, 1, "and", "CCONJ");
        var the = English(2, Genesis122, 2, "the", "DET");
        var waters = English(3, Genesis122, 3, "waters", "NOUN");
        var his = English(4, Genesis122, 4, "his", "PRON");
        var sons = English(5, Genesis122, 5, "sons", "NOUN");
        var vav = Hebrew(11, Genesis122, 1, "וְ", "H9000", "conj");
        var article = Hebrew(12, Genesis122, 2, "הַ", "H9009", "art");
        var water = Hebrew(13, Genesis122, 3, "מַּיִם", "H4325", "subs");
        var hisSons = Hebrew(14, Genesis122, 4, "בָנָיו", "H1121", "subs", ("suffixPerson", "p3"), ("suffixNumber", "sg"), ("suffixGender", "m"));
        var safe = new EvidentiaProposal(Analysis(waters), Analysis(water), EvidentiaProposalKind.GlobalStableKnownRendering, 0.8);
        var review = new EvidentiaProposal(Analysis(sons), Analysis(hisSons), EvidentiaProposalKind.GlobalReviewKnownRendering, 0.6);
        var attached = EvidentiaAttachedWords.Resolve(
            EvidentiaAuxiliaryWords.Mark([.. new[] { and, the, waters, his, sons }.Select(Analysis)]),
            [.. new[] { vav, article, water, hisSons }.Select(Analysis)],
            [safe, review]);

        EvidentiaAttachedWords.Safe([safe], attached).Should().BeEquivalentTo([(3L, 13L), (2L, 12L)]);
        attached.Select(proposal => (proposal.Source.Token.Id, proposal.Target.Token.Id))
            .Should().Contain([(1L, 11L), (4L, 14L)]);
    }

    private static IReadOnlyList<(long Source, long Target)> Attach(
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

    private static EvidentiaToken English(long id, EvidentiaAddress address, int position, string surface, string partOfSpeech) =>
        new(id, address, position, surface, "eng", PartOfSpeech: partOfSpeech);

    private static EvidentiaToken Hebrew(
        long id, EvidentiaAddress address, int position, string surface, string strong, string partOfSpeech,
        params (string Name, string Value)[] morphology) =>
        new(id, address, position, surface, "hbo", StrongNumber: strong, PartOfSpeech: partOfSpeech,
            Morphology: Features(partOfSpeech, morphology));

    private static EvidentiaToken Greek(
        long id, EvidentiaAddress address, int position, string surface, string strong, string partOfSpeech, string? lemma = null,
        (string Name, string Value)[]? morphology = null) =>
        new(id, address, position, surface, "grc", Lemma: lemma, StrongNumber: strong, PartOfSpeech: partOfSpeech,
            Morphology: Features(partOfSpeech, morphology ?? []));

    private static Dictionary<string, string> Features(string partOfSpeech, (string Name, string Value)[] morphology) =>
        morphology.Append(("pos", partOfSpeech)).ToDictionary(feature => feature.Item1, feature => feature.Item2);
}
