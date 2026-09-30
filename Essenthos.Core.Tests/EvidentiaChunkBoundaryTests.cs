using Essenthos.Core.Loading.Links.Evidentia;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// The placements the key counts wrong only because it chunks by the English phrase: a conjunction or a
/// preposition on the free prefix of the word it stands before, and an auxiliary or a pronoun on the word
/// it belongs to. Each is named from the key and the texts, and whatever is in doubt stays wrong.
/// </summary>
public class EvidentiaChunkBoundaryTests
{
    private static readonly EvidentiaAddress Verse = new(27, 10, 2);

    private static readonly LanguagePackRegistry Packs = new([new EnglishLanguagePack(), new OriginalLanguagePack()]);

    private static readonly (string, string)[] ThirdMasculine = [("person", "p3"), ("number", "sg"), ("gender", "m")];

    [Fact]
    public void APrepositionOnTheFreePrefixOfTheWordItStandsBeforeIsOfTheClass()
    {
        // In those days: the key has In those on הָהֵם and days on בַּיָּמִים.
        var found = Classes(
            [English(1, "In", "ADP"), English(2, "those", "DET"), English(3, "days", "NOUN")],
            [Prefix(11, "בַּ", "H9003", "prep"), Hebrew(12, "יָּמִים", "H3117", "subs"), Prefix(13, "הָ", "H9009", "art"), Hebrew(14, "הֵם", "H1992", "prde")],
            [([1, 2], [13, 14]), ([3], [11, 12])],
            (1, 11), (3, 12));

        found.Should().BeEquivalentTo(new Dictionary<(long, long), EvidentiaBoundaryCase>
        {
            [(1, 11)] = new(EvidentiaBoundaryClass.Prefix),
        });
    }

    [Fact]
    public void AConjunctionBeforeTheWordsOfItsOwnLinkReachesThePrefixOfTheNextRendering()
    {
        // or take their daughters: or take on תִּשְׂאוּ, their daughters on וּבְנֹתֵיהֶם.
        var found = Classes(
            [English(1, "or", "CCONJ"), English(2, "take", "VERB"), English(3, "their", "PRON"), English(4, "daughters", "NOUN")],
            [Prefix(11, "וּ", "H9000", "conj"), Hebrew(12, "בְנֹתֵיהֶם", "H1323", "subs"), Hebrew(13, "תִּשְׂאוּ", "H5375", "verb")],
            [([1, 2], [13]), ([3, 4], [11, 12])],
            (1, 11));

        found[(1, 11)].Should().Be(new EvidentiaBoundaryCase(EvidentiaBoundaryClass.Prefix, Conjunction: true));
    }

    [Fact]
    public void APrefixOnAWordTheKeyLeavesOutCountsBeforeTheWordItsOwnLinkNames()
    {
        // and all their stars: the whole chunk on צְבָאָם, and nothing on וְכָל.
        var found = Classes(
            [English(1, "and", "CCONJ"), English(2, "all", "DET"), English(3, "their", "PRON"), English(4, "stars", "NOUN")],
            [Prefix(11, "וְ", "H9000", "conj"), Hebrew(12, "כָל", "H3605", "subs"), Hebrew(13, "צְבָאָם", "H6635", "subs")],
            [([1, 2, 3, 4], [13])],
            (1, 11));

        found[(1, 11)].Should().Be(new EvidentiaBoundaryCase(EvidentiaBoundaryClass.Prefix, Conjunction: true, WordLeftOut: true));
    }

    [Fact]
    public void APrefixAnotherWordOfTheKeyTakesStaysWrong()
    {
        // for in the day: in takes the בְּ of בְּיוֹם, and for is כִּי.
        var found = Classes(
            [English(1, "for", "ADP"), English(2, "in", "ADP"), English(3, "the", "DET"), English(4, "day", "NOUN")],
            [Hebrew(11, "כִּי", "H3588", "conj"), Prefix(12, "בְּ", "H9003", "prep"), Hebrew(13, "יוֹם", "H3117", "subs")],
            [([1], [11]), ([2, 3, 4], [12, 13])],
            (1, 12));

        found.Should().BeEmpty();
    }

    [Fact]
    public void AWordWhoseLinkNamesAWordOfItsKindElsewhereStaysWrong()
    {
        // eat from the tree: the key has from on מִמֶּנּוּ, written further on.
        var found = Classes(
            [English(1, "eat", "VERB"), English(2, "from", "ADP"), English(3, "the", "DET"), English(4, "tree", "NOUN")],
            [Prefix(11, "מֵ", "H4480", "prep"), Hebrew(12, "עֵץ", "H6086", "subs"), Hebrew(13, "תֹאכַל", "H398", "verb"), Hebrew(14, "מִמֶּנּוּ", "H4480", "prep")],
            [([1], [13]), ([2], [14]), ([3, 4], [11, 12])],
            (2, 11));

        found.Should().BeEmpty();
    }

    [Fact]
    public void AConjunctionAwayFromTheRenderingOrAcrossPunctuationStaysWrong()
    {
        EvidentiaToken[] original = [Prefix(11, "וְ", "H9000", "conj"), Hebrew(12, "עֵץ", "H6086", "subs"), Hebrew(13, "תּוֹךְ", "H8432", "subs"), Hebrew(14, "גָּן", "H1588", "subs")];
        (long[], long[])[] key = [([1, 2], [13]), ([3], [14]), ([4], [11, 12])];

        // And in the middle of the garden were the tree: another link's word stands between.
        Classes(
            [English(1, "And", "CCONJ"), English(2, "middle", "NOUN"), English(3, "garden", "NOUN"), English(4, "tree", "NOUN")],
            original, key, (1, 11)).Should().BeEmpty();

        // And, tree: nothing between but a comma.
        (long[], long[])[] beside = [([1], [13]), ([2], [11, 12])];
        Classes([English(1, "And", "CCONJ", trailer: ", "), English(2, "tree", "NOUN")], original, beside, (1, 11)).Should().BeEmpty();
        Classes([English(1, "And", "CCONJ"), English(2, "tree", "NOUN")], original, beside, (1, 11)).Should().HaveCount(1);
    }

    [Fact]
    public void AnArticleIsNeverOfTheClass()
    {
        var found = Classes(
            [English(1, "The", "DET"), English(2, "clean", "ADJ")],
            [Hebrew(11, "מִן", "H4480", "prep"), Prefix(12, "הַ", "H9009", "art"), Hebrew(13, "טְּהוֹרָה", "H2889", "adjv")],
            [([1], [11]), ([2], [12, 13])],
            (1, 12));

        found.Should().BeEmpty();
    }

    [Fact]
    public void AnAuxiliaryOnItsVerbIsOfTheClassWhereTheKeyChunksItWithTheWordBeside()
    {
        // May the LORD answer: the key has May the LORD on יהוה.
        var found = Classes(
            [English(1, "May", "AUX", head: 4, relation: "aux"), English(2, "the", "DET"), English(3, "LORD", "PROPN"), English(4, "answer", "VERB")],
            [Hebrew(11, "יַעַנְךָ", "H6030", "verb", ThirdMasculine), Hebrew(12, "יהוה", "H3068", "nmpr")],
            [([1, 2, 3], [12]), ([4], [11])],
            (1, 11), (4, 11));

        found[(1, 11)].Should().Be(new EvidentiaBoundaryCase(EvidentiaBoundaryClass.Attached, Auxiliary: true));
    }

    [Fact]
    public void AnAuxiliaryWhoseKeyNamesAnotherVerbOrWhoseVerbTheKeyPutsElsewhereStaysWrong()
    {
        EvidentiaToken[] english =
        [
            English(1, "will", "AUX", head: 3, relation: "aux"), English(2, "be", "AUX", head: 3, relation: "aux:pass"),
            English(3, "opened", "VERB"),
        ];
        EvidentiaToken[] original = [Hebrew(11, "נִפְקְחוּ", "H6491", "verb", ThirdMasculine), Hebrew(12, "הְיִיתֶם", "H1961", "verb", ThirdMasculine)];

        // will be opened: the key has will be on another verb.
        Classes(english, original, [([1, 2], [12]), ([3], [11])], (1, 11), (3, 11)).Should().BeEmpty();

        // The key puts opened itself elsewhere: the head is what is wrong.
        Classes(english, original, [([1, 2, 3], [12])], (1, 11), (3, 11)).Should().BeEmpty();
    }

    [Fact]
    public void ASubjectOnTheEndingOfItsVerbIsOfTheClassUnlessTheKeyNamesTheSubjectTheOriginalWrites()
    {
        var first = new[] { ("person", "p1"), ("number", "sg") };

        // I will now arise: the key has I will now on עַתָּה.
        var chunked = Classes(
            [
                English(1, "I", "PRON", head: 4, relation: "nsubj", morphology: [("PronType", "Prs"), ("Person", "1"), ("Number", "Sing"), ("Case", "Nom")]),
                English(2, "will", "AUX", head: 4, relation: "aux"), English(3, "now", "ADV"), English(4, "arise", "VERB"),
            ],
            [Hebrew(11, "עַתָּה", "H6258", "advb"), Hebrew(12, "אָקוּם", "H6965", "verb", first)],
            [([1, 2, 3], [11]), ([4], [12])],
            (1, 12), (2, 12), (4, 12));

        chunked.Should().BeEquivalentTo(new Dictionary<(long, long), EvidentiaBoundaryCase>
        {
            [(1, 12)] = new(EvidentiaBoundaryClass.Attached),
            [(2, 12)] = new(EvidentiaBoundaryClass.Attached, Auxiliary: true),
        });

        // and He separated: He is the אֱלֹהִים the original writes.
        Classes(
            [
                English(1, "and", "CCONJ"),
                English(2, "He", "PRON", head: 3, relation: "nsubj", morphology: [("PronType", "Prs"), ("Person", "3"), ("Number", "Sing"), ("Case", "Nom"), ("Gender", "Masc")]),
                English(3, "separated", "VERB"),
            ],
            [Hebrew(11, "יַּבְדֵּל", "H914", "verb", ThirdMasculine), Hebrew(12, "אֱלֹהִים", "H430", "subs")],
            [([1, 2], [12]), ([3], [11])],
            (2, 11), (3, 11)).Should().BeEmpty();
    }

    [Fact]
    public void AnAuxiliaryAndASubjectOnTheFiniteVerbAreOfTheClassWhereTheKeyNamesOnlyTheInfinitiveBesideIt()
    {
        // you will surely die: the key has the whole phrase on מוֹת and leaves תָּמוּת out.
        EvidentiaToken[] english =
        [
            English(1, "you", "PRON", head: 4, relation: "nsubj", morphology: [("PronType", "Prs"), ("Person", "2")]),
            English(2, "will", "AUX", head: 4, relation: "aux"), English(3, "surely", "ADV"), English(4, "die", "VERB"),
        ];
        (string, string)[] second = [("person", "p2"), ("number", "sg"), ("gender", "m"), ("tense", "impf")];
        EvidentiaToken[] original = [Hebrew(11, "מוֹת", "H4191", "verb", [("tense", "infa")]), Hebrew(12, "תָּמוּת", "H4191", "verb", second)];

        var found = Classes(english, original, [([1, 2, 3, 4], [11])], (1, 12), (2, 12), (4, 12));

        found.Should().BeEquivalentTo(new Dictionary<(long, long), EvidentiaBoundaryCase>
        {
            [(1, 12)] = new(EvidentiaBoundaryClass.Attached, BesideItsInfinitive: true),
            [(2, 12)] = new(EvidentiaBoundaryClass.Attached, Auxiliary: true, BesideItsInfinitive: true),
        });

        // Another verb than the infinitive of its own lexeme, or one the key links, is the key's own choice.
        EvidentiaToken[] other = [Hebrew(11, "יָדֹעַ", "H3045", "verb", [("tense", "infa")]), Hebrew(12, "תָּמוּת", "H4191", "verb", second)];
        Classes(english, other, [([1, 2, 3, 4], [11])], (2, 12)).Should().BeEmpty();
        Classes(english, original, [([1, 2, 3, 4], [11]), ([3], [12])], (2, 12)).Should().BeEmpty();
    }

    [Fact]
    public void APossessiveOnTheSuffixOfItsNounIsOfTheClassWhereTheKeyChunksItWithANumeral()
    {
        // his two sons: the key has his two on שְׁנֵי.
        var found = Classes(
            [
                English(1, "his", "PRON", head: 3, relation: "nmod:poss", morphology: [("PronType", "Prs"), ("Person", "3"), ("Number", "Sing"), ("Poss", "Yes"), ("Gender", "Masc")]),
                English(2, "two", "NUM"), English(3, "sons", "NOUN"),
            ],
            [Hebrew(11, "שְׁנֵי", "H8147", "subs"), Hebrew(12, "בָנָיו", "H1121", "subs", [("suffixPerson", "p3"), ("suffixNumber", "sg"), ("suffixGender", "m")])],
            [([1, 2], [11]), ([3], [12])],
            (1, 12), (3, 12));

        found[(1, 12)].Should().Be(new EvidentiaBoundaryCase(EvidentiaBoundaryClass.Attached));
    }

    [Fact]
    public void EveryFigureIsQuotedByTheKeyAndWithEachClassCountedRight()
    {
        EvidentiaToken[] source = [English(1, "In", "ADP"), English(2, "those", "DET"), English(3, "days", "NOUN")];
        EvidentiaToken[] target =
        [
            Prefix(11, "בַּ", "H9003", "prep"), Hebrew(12, "יָּמִים", "H3117", "subs"), Prefix(13, "הָ", "H9009", "art"), Hebrew(14, "הֵם", "H1992", "prde"),
        ];
        var gold = Gold(([1, 2], [13, 14]), ([3], [11, 12]));
        var english = source.Select(Analysis).ToList();
        var original = target.Select(Analysis).ToList();
        var key = EvidentiaKeySplit.Of(gold, english, original);
        var days = new EvidentiaProposal(english[2], original[1], EvidentiaProposalKind.GlobalReviewKnownRendering, 0.8);
        List<EvidentiaProposal> proposals =
        [
            days,
            new(english[0], original[0], EvidentiaProposalKind.AttachedWord, 0.7,
                new EvidentiaDecisionTrace("attached", "Preposition of 'days'", []), days),
        ];
        var accepted = new HashSet<(long, long)> { (3, 12) };
        var boundary = EvidentiaChunkBoundary.Of(english, original, proposals, gold, key, accepted);

        var measure = EvidentiaStateScore.Of(
            english, proposals, [], new Dictionary<long, bool?>(), gold, key, accepted, new HashSet<(long, long)>(), boundary, out var words);

        words[1].Right.Should().BeFalse();
        words[1].Boundary.Should().Be(EvidentiaBoundaryClass.Prefix);
        words[3].Boundary.Should().Be(EvidentiaBoundaryClass.None);
        measure.Of(EvidentiaWordState.Linked).Should().Be(new EvidentiaStateCount(2, 2, 1, 0, 0, Boundary: 1));
        measure.Rules[(EvidentiaWordState.Linked, nameof(EvidentiaAttachment.Preposition))].Should().Be(new EvidentiaStateCount(1, 1, 0, 0, 0, Boundary: 1));
        measure.Boundary.Should().Be(new EvidentiaBoundaryCount(1, 0, 0, 0, 0, 0, 0, 0));
        (measure + measure).Boundary.Prefix.Should().Be(2);
        measure.Report().Should()
            .Contain("chunk boundary: chunk-boundary 1/2")
            .And.Contain("split key, pairs, both ways: 1/2 (50.00 %) by the key; with chunk-boundary counted right 2/2 (100.00 %)")
            .And.Contain("by rule, linked, Preposition: 1 words; right 0/1 (0.00 %); with chunk-boundary counted right 1/1 (100.00 %); with chunk-boundary-attached 0/1");
    }

    private static IReadOnlyDictionary<(long From, long To), EvidentiaBoundaryCase> Classes(
        IReadOnlyList<EvidentiaToken> source,
        IReadOnlyList<EvidentiaToken> target,
        (long[] Source, long[] Target)[] links,
        params (long From, long To)[] placed)
    {
        var gold = Gold(links);
        var english = source.Select(Analysis).ToList();
        var original = target.Select(Analysis).ToList();
        var key = EvidentiaKeySplit.Of(gold, english, original);
        List<EvidentiaProposal> proposals =
        [
            .. placed.Select(pair => new EvidentiaProposal(
                english.Single(word => word.Token.Id == pair.From), original.Single(word => word.Token.Id == pair.To),
                EvidentiaProposalKind.GlobalReviewKnownRendering, 0.8)),
        ];
        var accepted = proposals.Select(proposal => (proposal.Source.Token.Id, proposal.Target.Token.Id)).Where(key.Pairs.Contains).ToHashSet();
        return EvidentiaChunkBoundary.Of(english, original, proposals, gold, key, accepted);
    }

    private static EvidentiaGold Gold(params (long[] Source, long[] Target)[] links)
    {
        var stated = links.Select((link, index) => new EvidentiaGoldLink(index, default, "test", link.Source, link.Target)).ToList();
        var pairs = stated.SelectMany(link => link.SourceWords.SelectMany(one => link.TargetWords.Select(two => (one, two)))).ToHashSet();
        return new EvidentiaGold(
            pairs,
            pairs.Select(pair => pair.one).ToHashSet(),
            new Dictionary<long, List<EvidentiaGoldLink>>(),
            stated,
            new HashSet<long>(),
            new HashSet<long>());
    }

    private static EvidentiaAnalysis Analysis(EvidentiaToken token)
    {
        Packs.TryAnalyse(token, out var analysis).Should().BeTrue();
        return analysis;
    }

    private static EvidentiaToken English(
        long id, string surface, string partOfSpeech, long? head = null, string? relation = null, string trailer = " ",
        (string Name, string Value)[]? morphology = null) =>
        new(id, Verse, (int)id, surface, "eng", Trailer: trailer, PartOfSpeech: partOfSpeech,
            Morphology: morphology?.ToDictionary(feature => feature.Name, feature => feature.Value),
            SyntacticHead: head, Relation: relation);

    private static EvidentiaToken Prefix(long id, string surface, string strong, string partOfSpeech) =>
        Hebrew(id, surface, strong, partOfSpeech, joined: true);

    private static EvidentiaToken Hebrew(
        long id, string surface, string strong, string partOfSpeech, (string Name, string Value)[]? morphology = null, bool joined = false) =>
        new(id, Verse, (int)id, surface, "hbo", Trailer: joined ? string.Empty : " ", StrongNumber: strong,
            PartOfSpeech: partOfSpeech,
            Morphology: (morphology ?? []).Append(("pos", partOfSpeech)).ToDictionary(feature => feature.Item1, feature => feature.Item2));
}
