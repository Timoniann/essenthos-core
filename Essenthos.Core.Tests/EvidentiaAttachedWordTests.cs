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
    public void AnAuxiliaryIsNotPlacedWhereItsVerbWasPlacedOnAWordThatIsNoVerbOrOnAnInfinitive()
    {
        var had = English(1, Genesis122, 1, "had", "AUX");
        var followed = English(2, Genesis122, 2, "followed", "VERB");
        var after = Hebrew(11, Genesis122, 1, "אַחֲרֵי", "H310", "subs");
        var went = Hebrew(12, Genesis122, 2, "הָלְכוּ", "H1980", "verb", ("tense", "perf"));
        var toGo = Hebrew(13, Genesis122, 3, "לֶכֶת", "H1980", "verb", ("tense", "infc"));
        var going = Hebrew(14, Genesis122, 4, "הָלוֹךְ", "H1980", "verb", ("tense", "infa"));

        Attach([had, followed], [after, went], (followed, after)).Should().BeEmpty();
        Attach([had, followed], [after, went], (followed, went)).Should().Equal((1L, 12L));
        Attach([had, followed], [toGo, going], (followed, toGo)).Should().BeEmpty();
        Attach([had, followed], [toGo, going], (followed, going)).Should().BeEmpty();
    }

    [Fact]
    public void AnAuxiliaryIsNotPlacedOnAVerbWhoseEndingNamesAnotherPersonThanTheSubjectOfItsVerb()
    {
        var may = English(1, Genesis122, 1, "May", "AUX");
        var lord = English(2, Genesis122, 2, "LORD", "PROPN") with { SyntacticHead = 3, Relation = "nsubj" };
        var make = English(3, Genesis122, 3, "make", "VERB");
        var imperative = Hebrew(11, Genesis122, 1, "עֲשֵׂה", "H6213", "verb", ("tense", "impv"), ("person", "p2"), ("number", "sg"), ("gender", "m"));
        var jussive = Hebrew(12, Genesis122, 2, "יִתֵּן", "H5414", "verb", ("tense", "impf"), ("person", "p3"), ("number", "sg"), ("gender", "m"));
        var participle = Hebrew(13, Genesis122, 3, "עֹשֶׂה", "H6213", "verb", ("tense", "ptca"), ("number", "sg"), ("gender", "m"));

        Attach([may, lord, make], [imperative, jussive], (make, imperative)).Should().BeEmpty();
        Attach([may, lord, make], [imperative, jussive], (make, jussive)).Should().Equal((1L, 12L));
        // A participle names no person and takes any subject.
        Attach([may, lord, make], [participle], (make, participle)).Should().Equal((1L, 13L));

        var he = English(4, Genesis122, 4, "he", "PRON") with
        {
            SyntacticHead = 6, Relation = "nsubj",
            Morphology = new Dictionary<string, string>
            {
                ["PronType"] = "Prs", ["Person"] = "3", ["Number"] = "Sing", ["Case"] = "Nom", ["Gender"] = "Masc",
            },
        };
        var had = English(5, Genesis122, 5, "had", "AUX");
        var become = English(6, Genesis122, 6, "become", "VERB");
        var theyWere = Hebrew(14, Genesis122, 4, "יִּהְיוּ", "H1961", "verb", ("tense", "wayq"), ("person", "p3"), ("number", "pl"), ("gender", "m"));
        var heWas = Hebrew(15, Genesis122, 5, "יְהִי", "H1961", "verb", ("tense", "wayq"), ("person", "p3"), ("number", "sg"), ("gender", "m"));

        Attach([he, had, become], [theyWere], (become, theyWere)).Should().BeEmpty();
        Attach([he, had, become], [heWas], (become, heWas)).Should().BeEquivalentTo([(4L, 15L), (5L, 15L)]);

        // An object the parse took for the subject says nothing of the verb.
        var me = he with
        {
            Surface = "me",
            Morphology = new Dictionary<string, string> { ["PronType"] = "Prs", ["Person"] = "1", ["Number"] = "Sing", ["Case"] = "Acc" },
        };
        Attach([me, had, become], [heWas], (become, heWas)).Should().Contain((5L, 15L));
    }

    [Fact]
    public void AnObjectOfAVerbGoesOnTheSuffixOfAVerbAndNotOnTheSuffixOfANounItsVerbWasPlacedOn()
    {
        var named = English(1, Genesis122, 1, "named", "VERB");
        var him = English(2, Genesis122, 2, "him", "PRON");
        var before = English(3, Genesis122, 3, "before", "ADP");
        var himAgain = English(4, Genesis122, 4, "him", "PRON");
        (string, string)[] his = [("suffixPerson", "p3"), ("suffixNumber", "sg"), ("suffixGender", "m")];
        var hisName = Hebrew(11, Genesis122, 1, "שְׁמוֹ", "H8034", "subs", his);
        var calledHim = Hebrew(12, Genesis122, 2, "יִּקְרָאֵהוּ", "H7121", "verb", his);
        var hisFace = Hebrew(13, Genesis122, 3, "פָנָיו", "H6440", "subs", his);

        Attach([named, him], [hisName, calledHim], (named, hisName)).Should().BeEmpty();
        Attach([named, him], [hisName, calledHim], (named, calledHim)).Should().Equal((2L, 12L));
        // The object of a preposition still goes on the noun Hebrew writes that preposition with.
        Attach([before, himAgain], [hisFace], (before, hisFace)).Should().Equal((4L, 13L));
    }

    [Fact]
    public void TheBeforeANounItsConstructOrItsNameMakesDefiniteGoesWithTheNounWhenAsked()
    {
        var the = English(1, Genesis122, 1, "the", "DET");
        var word = English(2, Genesis122, 2, "word", "NOUN");
        var of = English(3, Genesis122, 3, "of", "ADP");
        var theLord = English(4, Genesis122, 4, "the", "DET");
        var lord = English(5, Genesis122, 5, "LORD", "PROPN");
        var davar = Hebrew(11, Genesis122, 1, "דְּבַר", "H1697", "subs", ("state", "c"));
        var yhwh = Hebrew(12, Genesis122, 2, "יְהוָה", "H3068", "nmpr");

        Attach([the, word, of, theLord, lord], [davar, yhwh], (word, davar), (lord, yhwh)).Should().NotContain((1L, 11L));
        AttachDefinite([the, word, of, theLord, lord], [davar, yhwh], (word, davar), (lord, yhwh))
            .Should().Contain((1L, 11L)).And.Contain((4L, 12L));
    }

    [Fact]
    public void TheBeforeAConstructChainThatWritesItsOwnArticleFurtherOnIsNotPutOnTheConstruct()
    {
        var the = English(1, Genesis122, 1, "the", "DET");
        var clans = English(2, Genesis122, 2, "clans", "NOUN");
        var of = English(3, Genesis122, 3, "of", "ADP");
        var theOther = English(4, Genesis122, 4, "the", "DET");
        var canaanites = English(5, Genesis122, 5, "Canaanites", "NOUN");
        var families = Hebrew(11, Genesis122, 1, "מִשְׁפְּחֹות", "H4940", "subs", ("state", "c"));
        var article = Hebrew(12, Genesis122, 2, "הַ", "H9009", "art");
        var canaanite = Hebrew(13, Genesis122, 3, "כְּנַעֲנִי", "H3669", "subs");

        var attached = AttachDefinite([the, clans, of, theOther, canaanites], [families, article, canaanite],
            (clans, families), (canaanites, canaanite));

        attached.Should().NotContain((1L, 11L)).And.Contain((4L, 12L));
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
    public void WhenAskedOfGoesWithTheConstructAndEachOfALongerChainWithTheNounBeforeIt()
    {
        var house = English(1, Genesis122, 1, "house", "NOUN");
        var of = English(2, Genesis122, 2, "of", "ADP");
        var the = English(3, Genesis122, 3, "the", "DET");
        var god = English(4, Genesis122, 4, "God", "PROPN");
        var of2 = English(5, Genesis122, 5, "of", "ADP");
        var jacob = English(6, Genesis122, 6, "Jacob", "PROPN");
        var houseNoun = Hebrew(11, Genesis122, 1, "בֵּית", "H1004", "subs", ("state", "c"));
        var godNoun = Hebrew(12, Genesis122, 2, "אֱלֹהֵי", "H430", "subs", ("state", "c"));
        var jacobName = Hebrew(13, Genesis122, 3, "יַעֲקֹב", "H3290", "nmpr", ("state", "a"));
        (EvidentiaToken, EvidentiaToken)[] placed = [(house, houseNoun), (god, godNoun), (jacob, jacobName)];

        AttachOf([house, of, the, god, of2, jacob], [houseNoun, godNoun, jacobName], placed)
            .Should().Contain([(2L, 11L), (5L, 12L)]);
        Attach([house, of, the, god, of2, jacob], [houseNoun, godNoun, jacobName], placed)
            .Should().Contain([(2L, 12L), (5L, 13L)]);
    }

    [Fact]
    public void WhenAskedOfGoesWithTheConstructOfThePhrasesNounPastTheModifiersEnglishWritesBeforeIt()
    {
        var beginning = English(1, Genesis122, 1, "beginning", "NOUN");
        var of = English(2, Genesis122, 2, "of", "ADP") with { Relation = "case", SyntacticHead = 5 };
        var the = English(3, Genesis122, 3, "the", "DET");
        var barley = English(4, Genesis122, 4, "barley", "NOUN") with { Relation = "compound", SyntacticHead = 5 };
        var harvest = English(5, Genesis122, 5, "harvest", "NOUN");
        var start = Hebrew(11, Genesis122, 1, "תְחִלַּת", "H8462", "subs", ("state", "c"));
        var reaping = Hebrew(12, Genesis122, 2, "קְצִיר", "H7105", "subs", ("state", "c"));
        var grain = Hebrew(13, Genesis122, 3, "שְׂעֹרִים", "H8184", "subs", ("state", "a"));
        AttachOf([beginning, of, the, barley, harvest], [start, reaping, grain], (beginning, start), (barley, grain), (harvest, reaping))
            .Should().Contain((2L, 11L));
    }

    [Fact]
    public void WhenAskedOfTheOriginalWritesNoWordForGoesOnTheConstructBeforeItsNounPastAPossessiveAndNotAfterAPreposition()
    {
        var cry = English(1, Genesis122, 1, "cry", "NOUN");
        var of = English(2, Genesis122, 2, "of", "ADP") with { Relation = "case" };
        var the = English(3, Genesis122, 3, "the", "DET");
        var afflicted = English(4, Genesis122, 4, "afflicted", "NOUN");
        var outOf = English(5, Genesis122, 5, "of", "ADP") with { Relation = "case" };
        var ark = English(6, Genesis122, 6, "ark", "NOUN");
        var ofHis = English(7, Genesis122, 7, "of", "ADP") with { Relation = "case" };
        var his = English(8, Genesis122, 8, "his", "PRON") with { Relation = "nmod:poss", SyntacticHead = 9 };
        var heart = English(9, Genesis122, 9, "heart", "NOUN");
        var construct = Hebrew(11, Genesis122, 1, "צַעֲקַת", "H6818", "subs", ("state", "c"));
        var humble = Hebrew(12, Genesis122, 2, "עֲנִיִּים", "H6041", "adjv", ("state", "a"));
        var from = Hebrew(13, Genesis122, 3, "מִן", "H4480", "prep");
        var article = Hebrew(14, Genesis122, 4, "הַ", "H9009", "art");
        var arkNoun = Hebrew(15, Genesis122, 5, "תֵּבָה", "H8392", "subs", ("state", "a"));
        var inclination = Hebrew(16, Genesis122, 6, "יֵצֶר", "H3336", "subs", ("state", "c"));
        var heartNoun = Hebrew(17, Genesis122, 7, "לִבֹּו", "H3820", "subs", ("state", "a"));
        (EvidentiaToken, EvidentiaToken)[] placed = [(afflicted, humble), (ark, arkNoun), (heart, heartNoun)];

        AttachOf([cry, of, the, afflicted, outOf, ark, ofHis, his, heart], [construct, humble, from, article, arkNoun, inclination, heartNoun], placed)
            .Should().Contain([(2L, 11L), (7L, 16L)]).And.NotContain(pair => pair.Source == 2 && pair.Target != 11 || pair.Source == 5);
        Attach([cry, of, the, afflicted], [construct, humble], (afflicted, humble)).Should().NotContain(pair => pair.Source == 2);
    }

    [Fact]
    public void WhenAskedOfAgainstGreekGoesOnTheGenitiveOrOnItsArticleWhenEnglishWritesTheToo()
    {
        var mind = English(1, Mark114, 1, "mind", "NOUN");
        var of = English(2, Mark114, 2, "of", "ADP") with { Relation = "case" };
        var the = English(3, Mark114, 3, "the", "DET");
        var flesh = English(4, Mark114, 4, "flesh", "NOUN");
        var son = English(5, Mark114, 5, "Son", "NOUN");
        var ofGod = English(6, Mark114, 6, "of", "ADP") with { Relation = "case" };
        var god = English(7, Mark114, 7, "God", "PROPN");
        var outOf = English(8, Mark114, 8, "of", "ADP") with { Relation = "case" };
        var water = English(9, Mark114, 9, "water", "NOUN");
        (string, string)[] genitive = [("case", "genitive")];
        var tes = Greek(11, Mark114, 1, "τῆς", "G3588", "det", morphology: genitive);
        var sarkos = Greek(12, Mark114, 2, "σαρκός", "G4561", "noun", morphology: genitive);
        var tou = Greek(13, Mark114, 3, "τοῦ", "G3588", "det", morphology: genitive);
        var theou = Greek(14, Mark114, 4, "Θεοῦ", "G2316", "noun", morphology: genitive);
        var ek = Greek(15, Mark114, 5, "ἐκ", "G1537", "prep");
        var tou2 = Greek(16, Mark114, 6, "τοῦ", "G3588", "det", morphology: genitive);
        var hydatos = Greek(17, Mark114, 7, "ὕδατος", "G5204", "noun", morphology: genitive);

        AttachOf([mind, of, the, flesh, son, ofGod, god, outOf, water], [tes, sarkos, tou, theou, ek, tou2, hydatos],
                (flesh, sarkos), (god, theou), (water, hydatos))
            .Should().Contain([(2L, 11L), (6L, 14L)]).And.NotContain(pair => pair.Source == 8);
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
    public void WhenAskedIsGoesOnTheHeadOfThePredicateComplementOfAVerblessClause()
    {
        var god = English(1, Genesis122, 1, "God", "PROPN") with { SyntacticHead = 4, Relation = "nsubj" };
        var isJudge = English(2, Genesis122, 2, "is", "AUX") with { Lemma = "be", SyntacticHead = 4, Relation = "cop" };
        var a = English(3, Genesis122, 3, "a", "DET");
        var judge = English(4, Genesis122, 4, "judge", "NOUN");
        var elohim = Hebrew(11, Genesis122, 1, "אֱלֹהִים", "H430", "subs");
        var shofet = Hebrew(12, Genesis122, 2, "שֹׁפֵט", "H8199", "subs");
        var nominal = Clauses((elohim, 1, "NmCl", 1, "Subj"), (shofet, 1, "NmCl", 2, "PreC"));
        AttachIs([god, isJudge, a, judge], [elohim, shofet], nominal, (god, elohim), (judge, shofet)).Should().Contain((2L, 12L));
        AttachOf([god, isJudge, a, judge], [elohim, shofet], (god, elohim), (judge, shofet)).Should().NotContain(pair => pair.Source == 2);
        Attach([god, isJudge, a, judge], [elohim, shofet], (god, elohim), (judge, shofet)).Should().NotContain(pair => pair.Source == 2);

        AttachIs([god, isJudge, a, judge], [elohim, shofet], nominal, (god, elohim)).Should().NotContain(pair => pair.Source == 2);

        var verbal = Clauses((elohim, 1, "xQtX", 1, "Subj"), (shofet, 1, "xQtX", 2, "Pred"));
        AttachIs([god, isJudge, a, judge], [elohim, shofet], verbal, (god, elohim), (judge, shofet)).Should().NotContain(pair => pair.Source == 2);

        var hu = Hebrew(13, Genesis122, 3, "הוּא", "H1931", "prps");
        var apart = Clauses((elohim, 1, "CPen", 1, "Frnt"), (hu, 2, "NmCl", 3, "Subj"), (shofet, 2, "NmCl", 2, "PreC"));
        AttachIs([god, isJudge, a, judge], [elohim, hu, shofet], apart, (god, elohim), (judge, shofet)).Should().NotContain(pair => pair.Source == 2);

        var subjectless = Clauses((elohim, 1, "WayX", 1, "Objc"), (shofet, 2, "AjCl", 2, "PreC"));
        AttachIs([god, isJudge, a, judge], [elohim, shofet], subjectless, (god, elohim), (judge, shofet)).Should().Contain((2L, 12L));
    }

    [Fact]
    public void WhenAskedIsGoesOnTheFrontedPredicateTheInterrogativeTheDemonstrativeSubjectAndTheNounOfAPreposition()
    {
        var great = English(1, Genesis122, 1, "Great", "ADJ");
        var isGlory = English(2, Genesis122, 2, "is", "AUX") with { Lemma = "be", SyntacticHead = 4, Relation = "cop" };
        var his = English(3, Genesis122, 3, "his", "PRON");
        var glory = English(4, Genesis122, 4, "glory", "NOUN");
        var gadol = Hebrew(11, Genesis122, 1, "גָּדֹול", "H1419", "adjv");
        var kevodo = Hebrew(12, Genesis122, 2, "כְּבֹודֹו", "H3519", "subs");
        AttachIs([great, isGlory, his, glory], [gadol, kevodo], Clauses((gadol, 1, "AjCl", 1, "PreC"), (kevodo, 1, "AjCl", 2, "Subj")),
            (glory, kevodo)).Should().Contain((2L, 11L));

        var who = English(1, Genesis122, 1, "who", "PRON");
        var isPhilistine = English(2, Genesis122, 2, "is", "AUX") with { Lemma = "be", SyntacticHead = 4, Relation = "cop" };
        var this_ = English(3, Genesis122, 3, "this", "DET");
        var philistine = English(4, Genesis122, 4, "Philistine", "PROPN");
        var mi = Hebrew(11, Genesis122, 1, "מִי", "H4310", "prin");
        var the = Hebrew(12, Genesis122, 2, "הַ", "H9009", "art");
        var pelishti = Hebrew(13, Genesis122, 3, "פְּלִשְׁתִּי", "H6430", "adjv");
        var zeh = Hebrew(14, Genesis122, 4, "זֶּה", "H2088", "prde");
        AttachIs([who, isPhilistine, this_, philistine], [mi, the, pelishti, zeh],
                Clauses((mi, 1, "NmCl", 1, "PreC"), (the, 1, "NmCl", 2, "Subj"), (pelishti, 1, "NmCl", 2, "Subj"), (zeh, 1, "NmCl", 2, "Subj")),
                (philistine, pelishti), (this_, zeh))
            .Should().Contain((2L, 11L));

        var these = English(1, Genesis122, 1, "These", "PRON") with { SyntacticHead = 4, Relation = "nsubj" };
        var are = English(2, Genesis122, 2, "are", "AUX") with { Lemma = "be", SyntacticHead = 4, Relation = "cop" };
        var theSons = English(3, Genesis122, 3, "the", "DET");
        var sons = English(4, Genesis122, 4, "sons", "NOUN");
        var all = Hebrew(10, Genesis122, 1, "כָּל", "H3605", "subs", ("state", "c"));
        var elleh = Hebrew(11, Genesis122, 2, "אֵלֶּה", "H428", "prde");
        var bene = Hebrew(12, Genesis122, 3, "בְנֵי", "H1121", "subs", ("state", "c"));
        AttachIs([these, are, theSons, sons], [all, elleh, bene],
                Clauses((all, 1, "NmCl", 1, "Subj"), (elleh, 1, "NmCl", 1, "Subj"), (bene, 1, "NmCl", 2, "PreC")),
                (sons, bene))
            .Should().Contain((2L, 11L));

        var darkness = English(1, Genesis122, 1, "darkness", "NOUN") with { SyntacticHead = 5, Relation = "nsubj" };
        var was = English(2, Genesis122, 2, "was", "AUX") with { Lemma = "be", SyntacticHead = 5, Relation = "cop" };
        var over = English(3, Genesis122, 3, "over", "ADP");
        var theSurface = English(4, Genesis122, 4, "the", "DET");
        var surface = English(5, Genesis122, 5, "surface", "NOUN");
        var choshech = Hebrew(11, Genesis122, 1, "חֹשֶׁךְ", "H2822", "subs");
        var al = Hebrew(12, Genesis122, 2, "עַל", "H5921", "prep");
        var pene = Hebrew(13, Genesis122, 3, "פְּנֵי", "H6440", "subs", ("state", "c"));
        AttachIs([darkness, was, over, theSurface, surface], [choshech, al, pene],
                Clauses((choshech, 1, "NmCl", 1, "Subj"), (al, 1, "NmCl", 2, "PreC"), (pene, 1, "NmCl", 2, "PreC")),
                (darkness, choshech), (surface, pene))
            .Should().Contain((2L, 13L));

        var blessed = English(1, Genesis122, 1, "Blessed", "ADJ");
        var be = English(2, Genesis122, 2, "be", "AUX") with { Lemma = "be", SyntacticHead = 4, Relation = "cop" };
        var theLord = English(3, Genesis122, 3, "the", "DET");
        var lord = English(4, Genesis122, 4, "LORD", "PROPN");
        var baruch = Hebrew(11, Genesis122, 1, "בָּרוּךְ", "H1288", "verb", ("tense", "ptcp"));
        var yhwh = Hebrew(12, Genesis122, 2, "יְהוָה", "H3068", "nmpr");
        AttachIs([blessed, be, theLord, lord], [baruch, yhwh], Clauses((baruch, 1, "Ptcp", 1, "PreC"), (yhwh, 1, "Ptcp", 2, "Subj")),
            (lord, yhwh)).Should().Contain((2L, 11L));
    }

    [Fact]
    public void WhenAskedBeGoesOnTheFiniteVerbItsPredicateWasPlacedOnWhereTheClauseWritesNoBe()
    {
        var earth = English(1, Genesis122, 1, "earth", "NOUN") with { SyntacticHead = 3, Relation = "nsubj" };
        var was = English(2, Genesis122, 2, "was", "AUX") with { Lemma = "be", SyntacticHead = 3, Relation = "cop" };
        var corrupt = English(3, Genesis122, 3, "corrupt", "ADJ");
        var tishachet = Hebrew(11, Genesis122, 1, "תִּשָּׁחֵת", "H7843", "verb", ("tense", "wayq"));
        var arets = Hebrew(12, Genesis122, 2, "אָרֶץ", "H776", "subs");
        var verbal = Clauses((tishachet, 1, "WayX", 1, "Pred"), (arets, 1, "WayX", 2, "Subj"));
        AttachIs([earth, was, corrupt], [tishachet, arets], verbal, (earth, arets), (corrupt, tishachet)).Should().Contain((2L, 11L));
        AttachOf([earth, was, corrupt], [tishachet, arets], (earth, arets), (corrupt, tishachet)).Should().NotContain(pair => pair.Source == 2);

        var be = Hebrew(13, Genesis122, 3, "הָיָה", "H1961", "verb", ("tense", "perf"));
        AttachIs([earth, was, corrupt], [tishachet, arets, be], Clauses((tishachet, 1, "WayX", 1, "Pred"), (arets, 1, "WayX", 2, "Subj"), (be, 1, "WayX", 3, "Pred")),
            (earth, arets), (corrupt, tishachet)).Should().NotContain(pair => pair.Source == 2);

        var corruption = Hebrew(14, Genesis122, 1, "שַׁחַת", "H7845", "subs");
        AttachIs([earth, was, corrupt], [corruption, arets], Clauses((corruption, 1, "WayX", 1, "Objc"), (arets, 1, "WayX", 2, "Subj")),
            (earth, arets), (corrupt, corruption)).Should().NotContain(pair => pair.Source == 2);

        var beFruitful = English(1, Genesis122, 1, "Be", "AUX") with { Lemma = "be", SyntacticHead = 2, Relation = "cop" };
        var fruitful = English(2, Genesis122, 2, "fruitful", "ADJ");
        var peru = Hebrew(11, Genesis122, 1, "פְּרוּ", "H6509", "verb", ("tense", "impv"));
        AttachIs([beFruitful, fruitful], [peru], Clauses((peru, 1, "ZIm0", 1, "Pred")), (fruitful, peru)).Should().Contain((1L, 11L));
    }

    [Fact]
    public void WhenAskedIsIsNotPlacedWhereTheClauseWritesItsOwnBe()
    {
        var god = English(1, Genesis122, 1, "God", "PROPN") with { SyntacticHead = 4, Relation = "nsubj" };
        var isJudge = English(2, Genesis122, 2, "is", "AUX") with { Lemma = "be", SyntacticHead = 4, Relation = "cop" };
        var a = English(3, Genesis122, 3, "a", "DET");
        var judge = English(4, Genesis122, 4, "judge", "NOUN");
        var elohim = Hebrew(11, Genesis122, 1, "אֱלֹהִים", "H430", "subs");
        var hu = Hebrew(12, Genesis122, 2, "הוּא", "H1931", "prps");
        var shofet = Hebrew(13, Genesis122, 3, "שֹׁפֵט", "H8199", "subs");
        var yesh = Hebrew(14, Genesis122, 4, "יֵשׁ", "H3426", "subs");
        var withPronoun = Clauses((elohim, 1, "NmCl", 1, "Subj"), (hu, 1, "NmCl", 2, "Subj"), (shofet, 1, "NmCl", 3, "PreC"));
        AttachIs([god, isJudge, a, judge], [elohim, hu, shofet], withPronoun, (god, elohim), (judge, shofet))
            .Should().NotContain(pair => pair.Source == 2);
        var withExistence = Clauses((elohim, 1, "NmCl", 1, "Subj"), (shofet, 1, "NmCl", 3, "PreC"), (yesh, 1, "NmCl", 4, "Exst"));
        AttachIs([god, isJudge, a, judge], [elohim, shofet, yesh], withExistence, (god, elohim), (judge, shofet))
            .Should().NotContain(pair => pair.Source == 2);

        var he = English(1, Genesis122, 1, "He", "PRON") with { SyntacticHead = 4, Relation = "nsubj" };
        AttachIs([he, isJudge, a, judge], [hu, shofet], Clauses((hu, 1, "NmCl", 2, "Subj"), (shofet, 1, "NmCl", 3, "PreC")), (he, hu), (judge, shofet))
            .Should().Contain((2L, 13L));
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

    [Fact]
    public void ASentenceConjunctionGoesOnTheVavOfItsVerbOnlyWhereBothStandFirstInTheirVerse()
    {
        var then = English(1, Genesis122, 1, "Then", "ADV") with { SyntacticHead = 3, Relation = "advmod" };
        var god = English(2, Genesis122, 2, "God", "PROPN");
        var said = English(3, Genesis122, 3, "said", "VERB");
        var vav = Hebrew(11, Genesis122, 1, "וַ", "H9000", "conj");
        var say = Hebrew(12, Genesis122, 2, "יֹּאמֶר", "H559", "verb");
        var elohim = Hebrew(13, Genesis122, 3, "אֱלֹהִים", "H430", "subs");

        Attach([then, god, said], [vav, say, elohim], (god, elohim), (said, say)).Should().Equal((1L, 11L));

        var first = Hebrew(21, Genesis122, 1, "אֱלֹהִים", "H430", "subs");
        var later = Hebrew(22, Genesis122, 2, "וַ", "H9000", "conj");
        var saying = Hebrew(23, Genesis122, 3, "יֹּאמֶר", "H559", "verb");

        Attach([then, god, said], [first, later, saying], (god, first), (said, saying)).Should().BeEmpty();
    }

    [Fact]
    public void OGoesWithTheNameItCallsOnTheOneWordTheOriginalWrites()
    {
        var o = English(1, Genesis122, 1, "O", "INTJ");
        var lord = English(2, Genesis122, 2, "LORD", "PROPN");
        var yhwh = Hebrew(11, Genesis122, 1, "יְהוָה", "H3068", "nmpr");

        Attach([o, lord], [yhwh], (lord, yhwh)).Should().Equal((1L, 11L));
    }

    [Fact]
    public void AParticleOfDirectionGoesWithItsVerbAndAnIdiomaticOneDoesNot()
    {
        var went = English(1, Genesis122, 1, "went", "VERB");
        var out_ = English(2, Genesis122, 2, "out", "ADP") with { SyntacticHead = 1, Relation = "compound:prt" };
        var gave = English(3, Genesis122, 3, "gave", "VERB");
        var up = English(4, Genesis122, 4, "up", "ADP") with { SyntacticHead = 3, Relation = "compound:prt" };
        var goOut = Hebrew(11, Genesis122, 1, "יֵּצֵא", "H3318", "verb");
        var give = Hebrew(12, Genesis122, 2, "נָתַן", "H5414", "verb");

        Attach([went, out_, gave, up], [goOut, give], (went, goOut), (gave, give)).Should().Equal((2L, 11L));
    }

    [Fact]
    public void AVerbGoesWhereItsParticleWasPlacedWhenOnlyTheParticleFoundTheOriginalsVerb()
    {
        var went = English(1, Mark114, 1, "went", "VERB");
        var out_ = English(2, Mark114, 2, "out", "ADP") with { SyntacticHead = 1, Relation = "compound:prt" };
        var goOut = Greek(11, Mark114, 1, "ἐξῆλθεν", "G1831", "verb");

        Attach([went, out_], [goOut], (out_, goOut)).Should().Equal((1L, 11L));
    }

    [Fact]
    public void LetOfLetThereBeGoesOnTheJussiveBeAndNowhereElse()
    {
        var let = English(1, Genesis122, 1, "Let", "VERB");
        var there = English(2, Genesis122, 2, "there", "PRON");
        var be = English(3, Genesis122, 3, "be", "AUX") with { Lemma = "be" };
        var light = English(4, Genesis122, 4, "light", "NOUN");
        var yehi = Hebrew(11, Genesis122, 1, "יְהִי", "H1961", "verb");
        var or = Hebrew(12, Genesis122, 2, "אוֹר", "H216", "subs");
        var sprout = Hebrew(21, Genesis122, 1, "תַּדְשֵׁא", "H1876", "verb");

        Attach([let, there, be, light], [yehi, or], (be, yehi), (light, or)).Should().Contain((1L, 11L));
        Attach([let, there, be, light], [sprout, or], (be, sprout), (light, or)).Should().NotContain(pair => pair.Source == 1L);
    }

    [Fact]
    public void SurelyGoesOnTheInfinitiveAbsoluteOfItsVerbsOwnLexeme()
    {
        var surely = English(1, Genesis122, 1, "surely", "ADV") with { SyntacticHead = 2, Relation = "advmod" };
        var die = English(2, Genesis122, 2, "die", "VERB");
        var dying = Hebrew(11, Genesis122, 1, "מוֹת", "H4191", "verb", ("tense", "infa"));
        var shallDie = Hebrew(12, Genesis122, 2, "תָּמוּת", "H4191", "verb", ("tense", "impf"));

        Attach([surely, die], [dying, shallDie], (die, shallDie)).Should().Equal((1L, 11L));
    }

    [Fact]
    public void OfGoesOnTheLamedBeforeItsNounWhereNoConstructStandsBeforeIt()
    {
        var psalm = English(1, Genesis122, 1, "Psalm", "NOUN");
        var of = English(2, Genesis122, 2, "of", "ADP");
        var david = English(3, Genesis122, 3, "David", "PROPN");
        var song = Hebrew(11, Genesis122, 1, "מִזְמוֹר", "H4210", "subs", ("state", "a"));
        var lamed = Hebrew(12, Genesis122, 2, "לְ", "H9005", "prep");
        var dawid = Hebrew(13, Genesis122, 3, "דָוִד", "H1732", "nmpr");

        Attach([psalm, of, david], [song, lamed, dawid], (psalm, song), (david, dawid)).Should().Equal((2L, 12L));
    }

    [Fact]
    public void APrepositionGoesBeforeTheFirstRenderingOfItsPhraseWhenAPossessiveOrAQuantifierCameBetween()
    {
        var @in = English(1, Mark114, 1, "in", "ADP") with { SyntacticHead = 3 };
        var his = English(2, Mark114, 2, "his", "PRON");
        var vineyard = English(3, Mark114, 3, "vineyard", "NOUN");
        var en = Greek(11, Mark114, 1, "ἐν", "G1722", "prep");
        var article = Greek(12, Mark114, 2, "τῷ", "G3588", "det");
        var vineyardNoun = Greek(13, Mark114, 3, "ἀμπελῶνι", "G290", "noun");
        var autou = Greek(14, Mark114, 4, "αὐτοῦ", "G846", "pron");

        Attach([@in, his, vineyard], [en, article, vineyardNoun, autou], (his, autou), (vineyard, vineyardNoun))
            .Should().Contain((1L, 11L));

        var to = English(4, Genesis122, 1, "to", "ADP") with { SyntacticHead = 7 };
        var all = English(5, Genesis122, 2, "all", "DET");
        var the = English(6, Genesis122, 3, "the", "DET");
        var kings = English(7, Genesis122, 4, "kings", "NOUN");
        var lamed = Hebrew(21, Genesis122, 1, "לְ", "H9005", "prep");
        var kol = Hebrew(22, Genesis122, 2, "כָל", "H3605", "subs");
        var kingsNoun = Hebrew(23, Genesis122, 3, "מַלְכֵי", "H4428", "subs");

        Attach([to, all, the, kings], [lamed, kol, kingsNoun], (all, kol), (kings, kingsNoun))
            .Should().Contain((4L, 21L));
    }

    [Fact]
    public void ACoordinatorReachesTheVavWrittenOnTheWordOfTheFirstPlacedWordAfterIt()
    {
        var and = English(1, Genesis122, 1, "and", "CCONJ");
        var his = English(2, Genesis122, 2, "his", "PRON");
        var voice = English(3, Genesis122, 3, "voice", "NOUN");
        var but = English(4, Genesis122, 4, "But", "CCONJ");
        var you = English(5, Genesis122, 5, "you", "PRON");
        var vav = Hebrew(11, Genesis122, 1, "וְ", "H9000", "conj");
        var sound = Hebrew(12, Genesis122, 2, "קוֹל", "H6963", "subs");
        var secondVav = Hebrew(13, Genesis122, 3, "וְ", "H9000", "conj");
        var thou = Hebrew(14, Genesis122, 4, "אַתָּה", "H859", "prps");

        Attach([and, his, voice, but, you], [vav, sound, secondVav, thou], (voice, sound), (you, thou))
            .Should().Equal((1L, 11L), (4L, 13L));
    }

    [Fact]
    public void ACoordinatorDoesNotReachPastAnotherOneNorBackBeforeTheWordPlacedBeforeIt()
    {
        var bread = English(1, Genesis122, 1, "bread", "NOUN");
        var and = English(2, Genesis122, 2, "and", "CCONJ");
        var much = English(3, Genesis122, 3, "much", "ADJ");
        var or = English(4, Genesis122, 4, "or", "CCONJ");
        var wine = English(5, Genesis122, 5, "wine", "NOUN");
        var vav = Hebrew(11, Genesis122, 1, "וְ", "H9000", "conj");
        var yayin = Hebrew(12, Genesis122, 2, "יַיִן", "H3196", "subs");
        var lechem = Hebrew(13, Genesis122, 3, "לֶחֶם", "H3899", "subs");

        Attach([and, much, wine], [vav, yayin, lechem], (wine, yayin))
            .Should().Equal((2L, 11L));

        // and is stopped by or, which stands nearer the word and takes its ו.
        Attach([and, much, or, wine], [vav, yayin, lechem], (wine, yayin))
            .Should().Equal((4L, 11L));

        // The ו stands before the rendering of bread: it opens an earlier clause than the one and joins.
        Attach([bread, and, much, wine], [vav, yayin, lechem], (bread, lechem), (wine, yayin))
            .Should().BeEmpty();
    }

    [Fact]
    public void ToAndAsReachThePrefixOfTheirKindAcrossModifiersOnly()
    {
        var to = English(1, Genesis122, 1, "to", "ADP");
        var everlasting = English(2, Genesis122, 2, "everlasting", "ADJ");
        var shame = English(3, Genesis122, 3, "shame", "NOUN");
        var asWord = English(4, Genesis122, 4, "as", "ADP");
        var the = English(5, Genesis122, 5, "the", "DET");
        var owl = English(6, Genesis122, 6, "owl", "NOUN");
        var lamed = Hebrew(11, Genesis122, 1, "לַ", "H9005", "prep");
        var reproach = Hebrew(12, Genesis122, 2, "חֲרָפוֹת", "H2781", "subs");
        var kaph = Hebrew(13, Genesis122, 3, "כְּ", "H9004", "prep");
        var yanshuf = Hebrew(14, Genesis122, 4, "יַנְשׁוּף", "H3244", "subs");

        Attach([to, everlasting, shame, asWord, the, owl], [lamed, reproach, kaph, yanshuf], (shame, reproach), (owl, yanshuf))
            .Should().Equal((1L, 11L), (4L, 13L));

        // Across a verb to is the infinitive's, and on a ב it is no to at all.
        var be = English(2, Genesis122, 2, "be", "AUX");
        var alone = English(3, Genesis122, 3, "alone", "ADJ");
        var toPart = English(1, Genesis122, 1, "to", "PART");
        Attach([toPart, be, alone], [lamed, reproach], (alone, reproach)).Should().BeEmpty();
        var bet = Hebrew(11, Genesis122, 1, "בְּ", "H9003", "prep");
        Attach([to, everlasting, shame], [bet, reproach], (shame, reproach)).Should().BeEmpty();
    }

    [Fact]
    public void AnEnglishWasBeforeAParticipleGoesOnTheHayahTheHebrewWritesBesideIt()
    {
        var daniel = English(1, Genesis122, 1, "Daniel", "PROPN");
        var was = English(2, Genesis122, 2, "was", "AUX") with { Lemma = "be" };
        var mourning = English(3, Genesis122, 3, "mourning", "VERB");
        var hayiti = Hebrew(11, Genesis122, 1, "הָיִיתִי", "H1961", "verb", ("tense", "perf"), ("person", "p1"), ("number", "sg"));
        var mourner = Hebrew(12, Genesis122, 2, "מִתְאַבֵּל", "H56", "verb", ("tense", "ptca"), ("number", "sg"), ("gender", "m"));

        Attach([daniel, was, mourning], [hayiti, mourner], (mourning, mourner)).Should().Equal((2L, 11L));
    }

    [Fact]
    public void AnEnglishWasStaysOnItsParticipleWhereTheHebrewWritesNoBeBesideIt()
    {
        var was = English(2, Genesis122, 2, "was", "AUX") with { Lemma = "be" };
        var mourning = English(3, Genesis122, 3, "mourning", "VERB");
        var mourner = Hebrew(12, Genesis122, 2, "מִתְאַבֵּל", "H56", "verb", ("tense", "ptca"), ("number", "sg"), ("gender", "m"));

        Attach([was, mourning], [mourner], (mourning, mourner)).Should().Equal((2L, 12L));
    }

    [Fact]
    public void AnEnglishWasIsLeftUnplacedWhereTheHebrewBeBesideTheParticipleIsAlreadyTaken()
    {
        var was = English(2, Genesis122, 2, "was", "AUX") with { Lemma = "be" };
        var mourning = English(3, Genesis122, 3, "mourning", "VERB");
        var itself = English(4, Genesis122, 4, "there", "ADV");
        var hayiti = Hebrew(11, Genesis122, 1, "הָיִיתִי", "H1961", "verb", ("tense", "perf"), ("person", "p1"), ("number", "sg"));
        var mourner = Hebrew(12, Genesis122, 2, "מִתְאַבֵּל", "H56", "verb", ("tense", "ptca"), ("number", "sg"), ("gender", "m"));

        Attach([was, mourning, itself], [hayiti, mourner], (mourning, mourner), (itself, hayiti)).Should().NotContain((2L, 12L));
    }

    [Fact]
    public void AnEnglishAuxiliaryThatIsNotAFormOfBeStaysOnTheParticiple()
    {
        var will = English(2, Genesis122, 2, "will", "AUX");
        var mourn = English(3, Genesis122, 3, "mourn", "VERB");
        var hayiti = Hebrew(11, Genesis122, 1, "הָיִיתִי", "H1961", "verb", ("tense", "perf"), ("person", "p1"), ("number", "sg"));
        var mourner = Hebrew(12, Genesis122, 2, "מִתְאַבֵּל", "H56", "verb", ("tense", "ptca"), ("number", "sg"), ("gender", "m"));

        Attach([will, mourn], [hayiti, mourner], (mourn, mourner)).Should().Equal((2L, 12L));
    }

    [Fact]
    public void WhenAskedAnAuxiliaryGoesOnTheGreekVerbWhoseFormCarriesItAndNotOnOneWhoseFormSaysOtherwise()
    {
        var will = English(1, Mark114, 1, "will", "AUX") with { Lemma = "will", SyntacticHead = 2, Relation = "aux" };
        var die = English(2, Mark114, 2, "die", "VERB");
        var might = English(3, Mark114, 3, "might", "AUX") with { Lemma = "might", SyntacticHead = 4, Relation = "aux" };
        var believe = English(4, Mark114, 4, "believe", "VERB");
        var was = English(5, Mark114, 5, "was", "AUX") with { Lemma = "be", SyntacticHead = 6, Relation = "aux:pass" };
        var baptized = English(6, Mark114, 6, "baptized", "VERB");
        var shall = English(7, Mark114, 7, "shall", "AUX") with { Lemma = "shall", SyntacticHead = 8, Relation = "aux" };
        var see = English(8, Mark114, 8, "see", "VERB");
        var apothaneitai = Greek(11, Mark114, 1, "ἀποθανεῖται", "G599", "verb", morphology: [("tense", "future"), ("mood", "indicative")]);
        var pisteusosin = Greek(12, Mark114, 2, "πιστεύσωσιν", "G4100", "verb", morphology: [("tense", "aorist"), ("mood", "subjunctive")]);
        var ebaptisthe = Greek(13, Mark114, 3, "ἐβαπτίσθη", "G907", "verb", morphology: [("tense", "aorist"), ("mood", "indicative"), ("voice", "passive")]);
        var eiden = Greek(14, Mark114, 4, "εἶδεν", "G3708", "verb", morphology: [("tense", "aorist"), ("mood", "indicative"), ("voice", "active")]);
        (EvidentiaToken, EvidentiaToken)[] placed = [(die, apothaneitai), (believe, pisteusosin), (baptized, ebaptisthe), (see, eiden)];

        AttachOf([will, die, might, believe, was, baptized, shall, see], [apothaneitai, pisteusosin, ebaptisthe, eiden], placed)
            .Should().BeEquivalentTo([(1L, 11L), (3L, 12L), (5L, 13L)]);
        Attach([will, die, might, believe, was, baptized, shall, see], [apothaneitai, pisteusosin, ebaptisthe, eiden], placed)
            .Should().BeEmpty();
    }

    [Fact]
    public void WhenAskedAGreekAuxiliaryGoesOnTheEimiOrMelloTheOriginalWritesForItButBeingStaysWithItsParticiple()
    {
        var were = English(1, Mark114, 1, "were", "AUX") with { Lemma = "be", SyntacticHead = 2, Relation = "aux" };
        var sitting = English(2, Mark114, 2, "sitting", "VERB");
        var will = English(3, Mark114, 3, "will", "AUX") with { Lemma = "will", SyntacticHead = 4, Relation = "aux" };
        var die = English(4, Mark114, 4, "die", "VERB");
        var being = English(5, Mark114, 5, "being", "AUX") with { Lemma = "be", SyntacticHead = 6, Relation = "aux:pass" };
        var tempted = English(6, Mark114, 6, "tempted", "VERB");
        (string, string)[] participle = [("tense", "present"), ("mood", "participle"), ("voice", "middlepassive")];
        var esan = Greek(11, Mark114, 1, "ἦσαν", "G1510", "verb", morphology: [("tense", "imperfect"), ("mood", "indicative")]);
        var tines = Greek(12, Mark114, 2, "τινες", "G5100", "pron");
        var kathemenoi = Greek(13, Mark114, 3, "καθήμενοι", "G2521", "verb", morphology: participle);
        var mellete = Greek(14, Mark114, 4, "μέλλετε", "G3195", "verb", morphology: [("tense", "present"), ("mood", "indicative")]);
        var apothneskein = Greek(15, Mark114, 5, "ἀποθνήσκειν", "G599", "verb", morphology: [("tense", "present"), ("mood", "infinitive")]);
        var peirazomenos = Greek(16, Mark114, 6, "πειραζόμενος", "G3985", "verb", morphology: participle);

        AttachOf([were, sitting, will, die, being, tempted], [esan, tines, kathemenoi, mellete, apothneskein, peirazomenos],
                (sitting, kathemenoi), (die, apothneskein), (tempted, peirazomenos))
            .Should().BeEquivalentTo([(1L, 11L), (3L, 14L), (5L, 16L)]);
    }

    [Fact]
    public void WhenAskedAnAuxiliaryGoesOnTheHebrewVerbAcrossTheNegationTheOriginalWritesApart()
    {
        var did = English(1, Genesis122, 1, "did", "AUX") with { Lemma = "do", SyntacticHead = 3, Relation = "aux" };
        var not = English(2, Genesis122, 2, "not", "PART");
        var see = English(3, Genesis122, 3, "see", "VERB");
        var can = English(4, Genesis122, 4, "can", "AUX") with { Lemma = "can", SyntacticHead = 6, Relation = "aux" };
        var notAgain = English(5, Genesis122, 5, "not", "PART");
        var go = English(6, Genesis122, 6, "go", "VERB");
        var lo = Hebrew(11, Genesis122, 1, "לֹא", "H3808", "nega");
        var rau = Hebrew(12, Genesis122, 2, "רָאוּ", "H7200", "verb", ("tense", "perf"));
        var loAgain = Hebrew(13, Genesis122, 3, "לֹא", "H3808", "nega");
        var nelekh = Hebrew(14, Genesis122, 4, "נֵלֵךְ", "H1980", "verb", ("tense", "impf"));
        (EvidentiaToken, EvidentiaToken)[] placed = [(not, lo), (see, rau), (notAgain, loAgain), (go, nelekh)];

        AttachOf([did, not, see, can, notAgain, go], [lo, rau, loAgain, nelekh], placed).Should().Equal((1L, 12L));
        Attach([did, not, see, can, notAgain, go], [lo, rau, loAgain, nelekh], placed).Should().BeEmpty();
    }

    [Fact]
    public void WhenAskedLetAndItsPronounGoOnTheVerbWhoseFormSaysTheWishAndLetBeforeANounToo()
    {
        var let = English(1, Genesis122, 1, "Let", "VERB");
        var us = English(2, Genesis122, 2, "us", "PRON") with { SyntacticHead = 1, Relation = "obj" };
        var make = English(3, Genesis122, 3, "make", "VERB") with { SyntacticHead = 1, Relation = "xcomp" };
        var naaseh = Hebrew(11, Genesis122, 1, "נַעֲשֶׂה", "H6213", "verb", ("tense", "impf"), ("person", "p1"), ("number", "pl"));

        AttachOf([let, us, make], [naaseh], (make, naaseh)).Should().BeEquivalentTo([(1L, 11L), (2L, 11L)]);

        var earth = English(2, Genesis122, 2, "earth", "NOUN") with { SyntacticHead = 1, Relation = "obj" };
        var bring = English(3, Genesis122, 3, "bring", "VERB") with { SyntacticHead = 1, Relation = "xcomp" };
        var totse = Hebrew(12, Genesis122, 2, "תֹּוצֵא", "H3318", "verb", ("tense", "impf"), ("person", "p3"), ("number", "sg"));
        var arets = Hebrew(13, Genesis122, 3, "אֶרֶץ", "H776", "subs");

        // The owner's ruling of 2026-10-16 (TSK-1095): the wish is in the verb's form whatever English's subject is,
        // so 'Let' goes on the third person's verb and the noun keeps its own word.
        AttachOf([let, earth, bring], [totse, arets], (earth, arets), (bring, totse)).Should().Equal((1L, 12L));

        var naaseh2 = Hebrew(14, Genesis122, 2, "נַעֲשֶׂה", "H6213", "verb", ("tense", "impf"), ("person", "p1"), ("number", "pl"));
        AttachOf([let, earth, bring], [naaseh2, arets], (earth, arets), (bring, naaseh2)).Should().BeEmpty(
            "a verb of the first person says nothing of a noun subject's wish");
    }

    [Fact]
    public void WhenAskedToOfAGreekDativeGoesOnTheNounOrOnItsArticleWhereEnglishWritesTheAndNotAfterAPreposition()
    {
        var to = English(1, Mark114, 1, "to", "ADP") with { SyntacticHead = 2, Relation = "case" };
        var god = English(2, Mark114, 2, "God", "PROPN");
        var toThe = English(3, Mark114, 3, "to", "ADP") with { SyntacticHead = 5, Relation = "case" };
        var the = English(4, Mark114, 4, "the", "DET");
        var servants = English(5, Mark114, 5, "servants", "NOUN");
        var toHouse = English(6, Mark114, 6, "to", "ADP") with { SyntacticHead = 7, Relation = "case" };
        var house = English(7, Mark114, 7, "house", "NOUN");
        (string, string)[] dative = [("case", "dative")];
        var to1 = Greek(11, Mark114, 1, "τῷ", "G3588", "det", morphology: dative);
        var theo = Greek(12, Mark114, 2, "Θεῷ", "G2316", "noun", morphology: dative);
        var tois = Greek(13, Mark114, 3, "τοῖς", "G3588", "det", morphology: dative);
        var diakonois = Greek(14, Mark114, 4, "διακόνοις", "G1249", "noun", morphology: dative);
        var en = Greek(15, Mark114, 5, "ἐν", "G1722", "prep");
        var oiko = Greek(16, Mark114, 6, "οἴκῳ", "G3624", "noun", morphology: dative);

        var attached = AttachOf([to, god, toThe, the, servants, toHouse, house], [to1, theo, tois, diakonois, en, oiko],
            (god, theo), (servants, diakonois), (house, oiko));

        attached.Should().Contain([(1L, 12L), (3L, 13L), (6L, 15L)]).And.NotContain((6L, 16L));
    }

    [Fact]
    public void WhenAskedTheRelativeOfAParticipleGoesOnItsArticleInGreekAndOnTheParticipleInHebrewButNotWhereAsherIsWritten()
    {
        var relative = new Dictionary<string, string> { ["PronType"] = "Rel" };
        var the = English(1, Mark114, 1, "the", "DET") with { SyntacticHead = 2, Relation = "det" };
        var one = English(2, Mark114, 2, "One", "NOUN");
        var who = English(3, Mark114, 3, "who", "PRON") with { SyntacticHead = 4, Relation = "nsubj", Morphology = relative };
        var comes = English(4, Mark114, 4, "comes", "VERB") with { SyntacticHead = 2, Relation = "acl:relcl" };
        (string, string)[] agreeing = [("case", "nominative"), ("number", "singular"), ("gender", "masculine")];
        var ho = Greek(11, Mark114, 1, "ὁ", "G3588", "det", morphology: agreeing);
        var erchomenos = Greek(12, Mark114, 2, "ἐρχόμενος", "G2064", "verb", morphology: [.. agreeing, ("mood", "participle")]);

        AttachOf([the, one, who, comes], [ho, erchomenos], (comes, erchomenos)).Should().BeEquivalentTo([(1L, 11L), (2L, 11L), (3L, 11L)]);

        var those = English(1, Genesis122, 1, "those", "PRON");
        var whoWalk = English(2, Genesis122, 2, "who", "PRON") with { SyntacticHead = 3, Relation = "nsubj", Morphology = relative };
        var walk = English(3, Genesis122, 3, "walk", "VERB") with { SyntacticHead = 1, Relation = "acl:relcl" };
        var article = Hebrew(11, Genesis122, 1, "הַ", "H9009", "art");
        var holekhim = Hebrew(12, Genesis122, 2, "הֹלְכִים", "H1980", "verb", ("tense", "ptca"));

        AttachOf([those, whoWalk, walk], [article, holekhim], (walk, holekhim)).Should().BeEquivalentTo([(1L, 12L), (2L, 12L)]);

        var asher = Hebrew(13, Genesis122, 1, "אֲשֶׁר", "H834", "conj");
        AttachOf([those, whoWalk, walk], [asher, holekhim], (walk, holekhim)).Should().BeEmpty();
    }

    private static IReadOnlyList<(long Source, long Target)> Attach(
        IReadOnlyList<EvidentiaToken> source,
        IReadOnlyList<EvidentiaToken> target,
        params (EvidentiaToken Source, EvidentiaToken Target)[] placed) => Attach(source, target, false, placed);

    private static IReadOnlyList<(long Source, long Target)> AttachOf(
        IReadOnlyList<EvidentiaToken> source,
        IReadOnlyList<EvidentiaToken> target,
        params (EvidentiaToken Source, EvidentiaToken Target)[] placed) => Attach(source, target, false, placed, functionWords: true);

    private static IReadOnlyList<(long Source, long Target)> AttachIs(
        IReadOnlyList<EvidentiaToken> source,
        IReadOnlyList<EvidentiaToken> target,
        EvidentiaClauses clauses,
        params (EvidentiaToken Source, EvidentiaToken Target)[] placed) => Attach(source, target, false, placed, functionWords: true, clauses);

    private static EvidentiaClauses Clauses(params (EvidentiaToken Word, long Clause, string Type, long Phrase, string Function)[] words) =>
        EvidentiaClauses.Of([.. words.Select(word => (word.Word.Id, new EvidentiaClausePlace(word.Clause, word.Type, word.Phrase, word.Function)))]);

    private static IReadOnlyList<(long Source, long Target)> AttachDefinite(
        IReadOnlyList<EvidentiaToken> source,
        IReadOnlyList<EvidentiaToken> target,
        params (EvidentiaToken Source, EvidentiaToken Target)[] placed) => Attach(source, target, true, placed);

    private static IReadOnlyList<(long Source, long Target)> Attach(
        IReadOnlyList<EvidentiaToken> source,
        IReadOnlyList<EvidentiaToken> target,
        bool definiteNouns,
        (EvidentiaToken Source, EvidentiaToken Target)[] placed,
        bool functionWords = false,
        EvidentiaClauses? clauses = null)
    {
        var proposals = placed
            .Select(pair => new EvidentiaProposal(
                Analysis(pair.Source), Analysis(pair.Target), EvidentiaProposalKind.GlobalReviewKnownRendering, 0.8))
            .ToList();
        return EvidentiaAttachedWords.Resolve(
                EvidentiaAuxiliaryWords.Mark([.. source.Select(Analysis)]),
                [.. target.Select(Analysis)],
                proposals,
                definiteNouns,
                functionWords,
                clauses)
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
